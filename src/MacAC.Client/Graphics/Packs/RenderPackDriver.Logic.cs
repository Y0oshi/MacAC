using MacAC.Client.Graphics.Gpu.Vulkan;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal sealed partial class RenderPackDriver
{
    internal RenderPackActivationCapture Capture { get; private set; } = new(
        RenderPackActivationPhase.Retail,
        RenderPackPick.Retail,
        ActivePackDisplayName: null,
        Reason: null,
        ActivationGeneration: 0);

    internal IRenderPackEngine? ActiveRuntime { get; private set; }

    internal RenderPackPerformanceCapture Performance => _performance.Freeze();

    internal int FloorPerformanceSpecimenTally =>
        _performance.FloorSpecimenTally;

    internal AtmosphericAutoQualityCapture? AutoFidelity => _autoFidelity?.Capture;

    public RenderPackTelemetryCapture SnapTelemetry()
    {
        RenderPackEngineTelemetry core =
            (ActiveRuntime as IRenderPackEngineTelemetrySource)?.GrabTelemetry()
            ?? RenderPackEngineTelemetry.Empty(Capture.Selection.PresetId);
        var performance = _performance.Freeze();
        return new RenderPackTelemetryCapture(
            Capture.State,
            Capture.Selection.PackId,
            Capture.Selection.PackVersion,
            Capture.Selection.PresetId,
            ActiveRuntime?.Preset.Id ?? core.EffectiveQuality,
            Capture.Reason,
            Capture.ActivationGeneration,
            core.RetainedGpuBytes,
            core.TransientGpuBytes,
            core.ImageCount,
            core.BufferCount,
            core.DrawCalls,
            core.DispatchCalls,
            core.ShadowCasterCount,
            core.CascadeDrawCount,
            core.CpuClassificationCalls,
            core.SunElevationDegrees,
            core.ActiveDayGroup,
            core.Weather,
            core.WeatherIntensity,
            core.Outdoor,
            core.DirectionalShadowStrength,
            core.Passes,
            performance)
        {
            CpuStages = core.CpuJunctures,
            DirectionalShadowSourceKind = core.DirectedShadeSrcSort,
            DirectionalShadowSourceObjectIndex =
                core.DirectedShadeSrcObjectOrdinal,
            DirectionalShadowSourceGfxObjId =
                core.DirectedShadeSrcGfxObjRefIdent,
            DirectionalShadowSurfaceToLightDirection =
                core.DirectedShadeCanvasToLampDir,
            DirectionalShadowLightElevationSin =
                core.DirectedShadeLampElevationSin,
            ShadowTransformChurn = core.ShadeXformChurn,
            SharedWorldTransformUsedInstances =
                core.SharedWorldTransformUsedInstances,
        };
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _registrySrc?.Changed -= OnRegistryAltered;
        _queued = null;
        if (_prep is { } prep)
        {
            _prep = null;
            try
            {
                prep.Work.GetAwaiter().GetResult();
            }
            catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
            {
            }
            finally
            {
                prep.Dispose();
            }
        }
        WipeAutomaticFidelity();
        RetireEngaged();
    }

    internal void Request(
        RenderPackPick? pick,
        bool explicitUserChoice = false)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var normalized = Standardize(pick);
        if (explicitUserChoice)
            _failedSelections.Remove(normalized);
        if (normalized == Capture.Selection
            && _queued is null)
            return;
        _queuedAutoFidelity = null;
        _queuedAutoBackupCause = null;
        _queued = normalized;
        Capture = Capture with
        {
            State = RenderPackActivationPhase.CandidatePending,
            Selection = normalized,
            ActivePackDisplayName = ActiveRuntime?.Descriptor.DisplayName,
            Reason = $"Preparing render pack '{normalized.PackId}'.",
        };
    }

    internal void WatchEngagedCycle(
        in RasterizeBundleCyclePerformanceObservation observation)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        Validate(in observation);
        var engaged = ActiveRuntime;
        if (engaged is not IRenderPackEnginePerformanceSource src)
            return;

        var metrics =
            src.GrabPerformanceMetrics();
        Validate(in metrics);
        if (!ReferenceEquals(engaged, _observedPerformanceCore)
            || metrics.ResourceGeneration != _observedAssetGen)
        {
            _performance.Reset();
            _observedPerformanceCore = engaged;
            _observedAssetGen = metrics.ResourceGeneration;
            return;
        }
        if (!observation.StableFrameBoundary
            || !metrics.HasResolvedGpuMeasurement)

            return;

        _performance.Observe(
            observation.PackAddedCpuMilliseconds,
            observation.AbsoluteEnhancedWorldReceiverCpuMilliseconds,
            hasSettledGpuMeasurement: true,
            metrics.InclusiveResolvedGpuMilliseconds,
            metrics.RetainedGpuBytes,
            metrics.TransientGpuBytes);
        if (_autoFidelity is null
            || _queuedAutoFidelity is not null
            || _queuedAutoBackupCause is not null
            || Capture.State != RenderPackActivationPhase.Active)

            return;

        var performance = _performance.Freeze();
        var measurement = new AtmosphericFidelityReading(
            performance.InclusiveGpuMillisecondsP99,
            performance.IncrementalCpuMillisecondsP99,
            performance.ResidentGpuBytes,
            StableFrameBoundary: true);
        var allowance = _autoFidelity.LatestAllowance;
        long precedingGen = _autoFidelity.Capture.ChangeGeneration;
        var fidelity = _autoFidelity.Observe(in measurement);
        if (fidelity.ChangeGeneration == precedingGen)
            return;

        if (fidelity.SafeFallbackToRetailRequested)
        {
            _queuedAutoBackupCause = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Automatic quality disabled render pack '{0}' because {1} remained "
                + "over its declared performance budget for {2} stable samples: "
                + "GPU p99 {3:F3} ms (budget {4:F3} ms), CPU p99 {5:F3} ms "
                + "(budget {6:F3} ms), resident GPU bytes {7} (budget {8}).",
                Capture.Selection.PackId,
                fidelity.Current,
                AtmosphericAutoQualityDriver.DowngradeHysteresisCycles,
                measurement.InclusivePackGpuMillisecondsP99,
                allowance.GpuMillisecondsP99,
                measurement.IncrementalCpuMillisecondsP99,
                allowance.CpuMillisecondsP99,
                measurement.ResidentGpuBytes,
                allowance.ResidentGpuBytes);
            return;
        }

        _queuedAutoFidelity = fidelity.Current;
    }

    internal void OnCoreMiss(string cause)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(cause);
        var failed = Capture.Selection;
        if (!failed.IsCanon)
            _failedSelections[failed] = _engagedEnrollmentIdent;
        string? sunsetMiss = RetireEngaged();
        WipeAutomaticFidelity();
        Capture = new RenderPackActivationCapture(
            RenderPackActivationPhase.FailedToRetail,
            RenderPackPick.Retail,
            ActivePackDisplayName: null,
            cause + ComposeSunsetMiss(sunsetMiss),
            ActivationGeneration: checked(Capture.ActivationGeneration + 1));
    }

    private ActivationScheme? PlanPick(
        RenderPackPick pick,
        in RasterizeBundleActivationReach reach)
    {
        var registry = _registry();
        if (!registry.TryGet(pick.PackId, out RenderPackRegistryEntry listing))
        {
            if (AlreadyFailed(pick, enrollmentIdent: 0))
                return null;
            Fail(pick, $"Render pack '{pick.PackId}' is not installed.", 0);
            return null;
        }
        if (AlreadyFailed(pick, listing.RegistrationId))
            return null;
        if (!listing.IsCompatible)
        {
            Fail(pick, listing.IncompatibilityReason ?? "The render pack is incompatible.");
            return null;
        }
        if (!string.Equals(
                listing.Descriptor.PackVersion.ToString(),
                pick.PackVersion,
                StringComparison.Ordinal))
        {
            Fail(
                pick,
                $"Render pack '{pick.PackId}' version {pick.PackVersion ?? "(missing)"} "
                + $"was selected, but version {listing.Descriptor.PackVersion} is installed.");
            return null;
        }

        var chosenPreset = listing.Descriptor.QualityPresets.FirstOrDefault(
            val => string.Equals(val.Id, pick.PresetId, StringComparison.OrdinalIgnoreCase));
        if (chosenPreset is null)
        {
            Fail(pick, $"Render pack preset '{pick.PresetId}' is not available.");
            return null;
        }
        if (listing.PresetIncompatibilityReasons.TryGetValue(
                chosenPreset.Id,
                out string? presetMiss)
            && presetMiss is not null)
        {
            Fail(pick, presetMiss);
            return null;
        }

        var userPrefs =
            RenderPackPreferenceResolution.VetUserSubstitutions(
                listing.Descriptor,
                pick.SettingOverrides);
        if (!userPrefs.Success)
        {
            Fail(pick, userPrefs.Reason!);
            return null;
        }

        bool automaticSelector =
            chosenPreset.Semantic == RenderQualityRole.Automatic;
        bool automatic = TryLocateAutomaticSetting(
            listing.Descriptor,
            chosenPreset,
            pick.SettingOverrides,
            out bool configuredAutomatic)
                ? configuredAutomatic
                : automaticSelector;
        QualityLadderStep preset = chosenPreset;
        var autoStarting = AtmosphericFidelityTier.Medium;
        var autoCeiling = AtmosphericFidelityTier.High;
        if (automaticSelector || automatic)
        {
            if (!TryLocateAutomaticSpan(
                    listing,
                    out preset,
                    out autoStarting,
                    out autoCeiling,
                    out string? automaticMiss))
            {
                Fail(pick, automaticMiss!);
                return null;
            }
            if (automatic
                && !automaticSelector
                && TryFidelityTier(chosenPreset.Semantic, out AtmosphericFidelityTier preferred))
            {
                preset = chosenPreset;
                autoStarting = preferred;
            }
        }

        AutomaticBulletin automaticBulletin = automatic
            ? new AutomaticBulletin(
                AutomaticBulletinManner.Initialise,
                AutomaticBudgets(listing),
                autoStarting,
                autoCeiling)
            : AutomaticBulletin.Disabled;
        return new ActivationScheme(
            listing,
            pick,
            preset,
            reach,
            RequirePerformanceSource: automatic,
            automaticBulletin);
    }

    private ActivationScheme? PlanAutomaticFidelity(
        AtmosphericFidelityTier fidelity,
        in RasterizeBundleActivationReach reach)
    {
        var pick = Capture.Selection;
        if (ActiveRuntime is null
            || _autoFidelity is null
            || Capture.State != RenderPackActivationPhase.Active)

            return null;

        var registry = _registry();
        if (!registry.TryGet(pick.PackId, out RenderPackRegistryEntry listing))
        {
            Fail(pick, $"Render pack '{pick.PackId}' is no longer installed.");
            return null;
        }
        if (!listing.IsCompatible)
        {
            Fail(pick, listing.IncompatibilityReason ?? "The render pack is incompatible.");
            return null;
        }
        if (!string.Equals(
                listing.Descriptor.PackVersion.ToString(),
                pick.PackVersion,
                StringComparison.Ordinal))
        {
            Fail(
                pick,
                $"Render pack '{pick.PackId}' changed version during automatic quality selection.");
            return null;
        }

        var preset = SeekNetPreset(listing, fidelity);
        if (preset is null)
        {
            Fail(
                pick,
                $"Automatic quality cannot select a missing or ineligible "
                + $"'{FidelitySemantic(fidelity)}' semantic preset.");
            return null;
        }
        if (listing.PresetIncompatibilityReasons.TryGetValue(
                preset.Id,
                out string? presetMiss)
            && presetMiss is not null)
        {
            Fail(pick, presetMiss);
            return null;
        }

        var userPrefs =
            RenderPackPreferenceResolution.VetUserSubstitutions(
                listing.Descriptor,
                pick.SettingOverrides);
        if (!userPrefs.Success)
        {
            Fail(pick, userPrefs.Reason!);
            return null;
        }

        Capture = Capture with
        {
            State = RenderPackActivationPhase.CandidatePending,
            ActivePackDisplayName = ActiveRuntime.Descriptor.DisplayName,
            Reason = $"Preparing automatic quality preset '{preset.DisplayName}'.",
        };
        return new ActivationScheme(
            listing,
            pick,
            preset,
            reach,
            RequirePerformanceSource: true,
            AutomaticBulletin.Preserve);
    }

    private static string PrepMiss(
        RenderPackPick pick,
        string cause) =>
        $"Render pack '{pick.PackId}' could not be prepared: {cause}";

    private bool AlreadyFailed(
        RenderPackPick pick,
        long enrollmentIdent)
    {
        if (!_failedSelections.TryGetValue(pick, out long failedEnrollmentIdent))
            return false;
        if (failedEnrollmentIdent != enrollmentIdent)
        {
            _failedSelections.Remove(pick);
            return false;
        }

        RetireEngaged();
        BroadcastCanon(
            pick,
            "This pack selection already failed for the current registration "
                + "and will not be retried.");
        return true;
    }

    private long LatestEnrollmentIdent(RenderPackPick pick)
    {
        return pick.IsCanon
            ? 0
            : _registry().TryGet(pick.PackId, out RenderPackRegistryEntry listing)
            ? listing.RegistrationId
            : 0;
    }

    private static AtmosphericQualityAllowance[] AutomaticBudgets(
        RenderPackRegistryEntry listing)
    {
        return [
        Allowance(listing, AtmosphericFidelityTier.Low),
        Allowance(listing, AtmosphericFidelityTier.Medium),
        Allowance(listing, AtmosphericFidelityTier.High),
    ];
    }

    private static AtmosphericQualityAllowance Allowance(
        RenderPackRegistryEntry listing,
        AtmosphericFidelityTier tier)
    {
        return AtmosphericQualityAllowance.FromPreset(
            listing.Descriptor.QualityPresets.Single(preset =>
                preset.Semantic == FidelitySemantic(tier)));
    }

    private static RenderQualityRole FidelitySemantic(
        AtmosphericFidelityTier quality)
    {
        return quality switch
        {
            AtmosphericFidelityTier.Low => RenderQualityRole.Low,
            AtmosphericFidelityTier.Medium => RenderQualityRole.Medium,
            AtmosphericFidelityTier.High => RenderQualityRole.High,
            _ => throw new ArgumentOutOfRangeException(nameof(quality)),
        };
    }

    private static string? FuseSunsetMisses(string? lead, string? second) =>
        lead is null ? second : second is null ? lead : lead + "; " + second;

    private static void FinestEffortTeardownForFatal(IDisposable? val)
    {
        if (val is null)
            return;
        try
        {
            val.Dispose();
        }
        catch
        {
        }
    }

    private void BeginPrep(ActivationScheme plan)
    {
        QueuedPrep prep = new QueuedPrep(plan);
        _prep = prep;
        Capture = Capture with
        {
            State = RenderPackActivationPhase.CandidatePending,
            Selection = plan.Selection,
            ActivePackDisplayName = ActiveRuntime?.Descriptor.DisplayName,
            Reason = $"Preparing render pack '{plan.Selection.PackId}' preset "
                + $"'{plan.Preset.DisplayName}'.",
        };
        try
        {
            prep.Work = _prepScheduler.Book(
                () => prep.Verdict = ReadyContender(plan));
        }
        catch (Exception problem) when (VkRenderFailureRule.IsFatal(problem))
        {
            _prep = null;
            prep.Dispose();
            throw;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            prep.Verdict = PreparationVerdict.Failed(
                PrepMiss(plan.Selection, problem.GetBaseException().Message));
            prep.Work = Task.CompletedTask;
        }
    }

    private PreparationVerdict ReadyContender(ActivationScheme plan)
    {
        IRenderPackEngine? contender = null;
        IRasterizeBundleRecipientPipeContender? recipientContender = null;
        try
        {
            var holdings = RenderPackValidator.VetChosenHoldings(
                plan.Entry.Descriptor,
                plan.Entry.Assets,
                out ValidatedRasterizeBundleShaderHoldings? validatedHoldings);
            if (!holdings.Success)
                return PreparationVerdict.Failed(holdings.Reason!);

            contender = _maker.Build(
                plan.Entry.Descriptor,
                validatedHoldings!,
                plan.Preset,
                plan.Selection.SettingOverrides) ?? throw new InvalidOperationException("The render-pack factory returned no candidate");
            if (!ReferenceEquals(contender.Descriptor, plan.Entry.Descriptor)
                && contender.Descriptor != plan.Entry.Descriptor)
                throw new InvalidOperationException("The candidate doesn't represent the selected descriptor");
            if (!ReferenceEquals(contender.Preset, plan.Preset)
                && contender.Preset != plan.Preset)
                throw new InvalidOperationException("The candidate doesn't represent the selected preset");
            if (plan.RequirePerformanceSource
                && contender is not IRenderPackEnginePerformanceSource)
            {
                throw new NotSupportedException(
                    "Automatic quality needs allocation-free runtime performance metrics");
            }
            if (contender is IAtmosphericRealmGraphEngine graph)
            {
                _ = graph.PrepareWorldTarget(
                    plan.Extent.Width,
                    plan.Extent.Height,
                    plan.Extent.SampleCount);
            }
            else if (plan.RequirePerformanceSource)
            {
                throw new NotSupportedException(
                    "Automatic quality needs a complete off-side world graph candidate");
            }

            IDirectionalShadeReceiverSource? recipientSrc =
                contender is IDirectionalShadeRealmGraphEngine directed
                    ? directed.DirectedShadeRecipients
                    : null;
            if (recipientSrc is not null && _recipientPipes is null)
            {
                throw new NotSupportedException(
                    "Directional-shadow activation needs an atomic receiver-pipeline coordinator");
            }
            if (_recipientPipes is not null)
            {
                recipientContender = _recipientPipes.Prepare(
                    recipientSrc,
                    plan.Extent.SampleCount);
            }
            PreparationVerdict verdict = PreparationVerdict.Ready(
                contender,
                recipientContender);
            contender = null;
            recipientContender = null;
            return verdict;
        }
        catch (Exception problem) when (VkRenderFailureRule.IsFatal(problem))
        {
            FinestEffortTeardownForFatal(recipientContender);
            FinestEffortTeardownForFatal(contender);
            throw;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            return new PreparationVerdict(
                contender,
                recipientContender,
                PrepMiss(
                    plan.Selection,
                    problem.GetBaseException().Message));
        }
    }

    private RenderPackActivationCapture BroadcastReadiedContender(
        ActivationScheme plan,
        PreparationVerdict verdict)
    {
        IRenderPackEngine? contender = verdict.GrabCore();
        var recipientContender =
            verdict.GrabRecipientContender();
        try
        {
            var earlier = ActiveRuntime;
            if (recipientContender is not null)
            {
                _recipientPipes!.Publish(recipientContender);
                recipientContender = null;
            }
            ActiveRuntime = contender;
            _engagedEnrollmentIdent = plan.Entry.RegistrationId;
            contender = null;
            RestartPerformanceTracking(ActiveRuntime);
            earlier?.Dispose();
            ImposeAutomaticBulletin(plan.AutomaticPublication);
            Capture = new RenderPackActivationCapture(
                RenderPackActivationPhase.Active,
                plan.Selection,
                plan.Entry.Descriptor.DisplayName,
                Reason: null,
                ActivationGeneration: checked(Capture.ActivationGeneration + 1));
            return Capture;
        }
        catch (Exception problem) when (VkRenderFailureRule.IsFatal(problem))
        {
            FinestEffortTeardownForFatal(recipientContender);
            FinestEffortTeardownForFatal(contender);
            throw;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            string? recipientSunset = TryTeardownRecipientContender(recipientContender);
            string? contenderSunset = TryTeardown(contender);
            return Fail(
                plan.Selection,
                $"Render pack '{plan.Selection.PackId}' could not be published: "
                + problem.GetBaseException().Message
                + ComposeSunsetMiss(recipientSunset)
                + ComposeSunsetMiss(contenderSunset),
                plan.Entry.RegistrationId);
        }
        finally
        {
            verdict.Dispose();
        }
    }

    private RenderPackActivationCapture BroadcastCanon(
        RenderPackPick asked,
        string? cause)
    {
        WipeAutomaticFidelity();
        Capture = new RenderPackActivationCapture(
            cause is null
                ? RenderPackActivationPhase.Retail
                : RenderPackActivationPhase.FailedToRetail,
            RenderPackPick.Retail,
            ActivePackDisplayName: null,
            cause,
            ActivationGeneration: checked(Capture.ActivationGeneration + 1));
        return Capture;
    }

    private void OnRegistryAltered(long rev)
    {
        _ = rev;
        Interlocked.Exchange(ref _registryAltered, 1);
    }

    private RenderPackActivationCapture Fail(
        RenderPackPick pick,
        string cause,
        long? enrollmentIdent = null)
    {
        _failedSelections[pick] = enrollmentIdent
            ?? LatestEnrollmentIdent(pick);
        string? sunsetMiss = RetireEngaged();
        WipeAutomaticFidelity();
        Capture = new RenderPackActivationCapture(
            RenderPackActivationPhase.FailedToRetail,
            RenderPackPick.Retail,
            ActivePackDisplayName: null,
            cause + ComposeSunsetMiss(sunsetMiss),
            ActivationGeneration: checked(Capture.ActivationGeneration + 1));
        return Capture;
    }

    private string? RetireEngaged()
    {
        var engaged = ActiveRuntime;
        ActiveRuntime = null;
        _engagedEnrollmentIdent = 0;
        RestartPerformanceTracking(null);
        string? recipientMiss = TryWipeRecipientPipes();
        string? coreMiss = TryTeardown(engaged);
        return FuseSunsetMisses(recipientMiss, coreMiss);
    }

    private void RestartPerformanceTracking(IRenderPackEngine? core)
    {
        _performance.Reset();
        _observedPerformanceCore = core;
        _observedAssetGen =
            core is IRenderPackEnginePerformanceSource src
                ? src.GrabPerformanceMetrics().ResourceGeneration
                : -1;
    }

    private void WipeAutomaticFidelity()
    {
        _autoFidelity = null;
        _queuedAutoFidelity = null;
        _queuedAutoBackupCause = null;
    }

    private static QualityLadderStep? SeekNetPreset(
        RenderPackRegistryEntry listing,
        AtmosphericFidelityTier fidelity)
    {
        return listing.Descriptor.QualityPresets.FirstOrDefault(preset =>
            preset.AutoEligible
            && preset.Semantic == FidelitySemantic(fidelity));
    }

    private static void Validate(in RasterizeBundleCyclePerformanceObservation value)
    {
        if (!double.IsFinite(value.PackAddedCpuMilliseconds)
            || value.PackAddedCpuMilliseconds < 0d
            || !double.IsFinite(value.AbsoluteEnhancedWorldReceiverCpuMilliseconds)
            || value.AbsoluteEnhancedWorldReceiverCpuMilliseconds < 0d
            || value.ViewportWidth <= 0
            || value.ViewportHeight <= 0
            || value.SampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Render-pack frame measurements and target dimensions has to be valid");
        }
    }

    private static void Validate(in RenderPackEnginePerformanceMetrics val)
    {
        if (val.ResourceGeneration < 0
            || (val.HasResolvedGpuMeasurement
                && (!double.IsFinite(val.InclusiveResolvedGpuMilliseconds)
                    || val.InclusiveResolvedGpuMilliseconds < 0d))
            || val.RetainedGpuBytes < 0
            || val.TransientGpuBytes < 0)
        {
            throw new InvalidOperationException(
                "The render-pack runtime published not valid performance metrics");
        }
    }

    private static string ComposeSunsetMiss(string? cause)
    {
        return cause is null ? string.Empty : $" Pack resource retirement also failed: {cause}";
    }

    private static RenderPackPick Standardize(
        RenderPackPick? pick)
    {
        if (pick is null
            || string.IsNullOrWhiteSpace(pick.PackId)
            || string.IsNullOrWhiteSpace(pick.PresetId))
            return RenderPackPick.Retail;
        return pick.IsCanon ? RenderPackPick.Retail : pick;
    }
}

using MacAC.Client.Graphics.Gpu.Vulkan;

namespace MacAC.Client.Graphics.Packs;

internal sealed partial class RenderPackDriver
{
    internal RenderPackActivationCapture ImposeAtCycleBoundary(
        RasterizeBundleActivationReach reach)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        reach.Validate();

        if (ImposeRegistryEditAtCycleBoundary() is { } registryMiss)
            return registryMiss;

        if (_prep is { } prep)
        {
            if (!prep.Work.IsCompleted)
                return Capture;

            _prep = null;
            if (prep.Work.IsCanceled)
            {
                prep.Dispose();
                return Fail(
                    prep.Plan.Selection,
                    PrepMiss(
                        prep.Plan.Selection,
                        "candidate preparation was cancelled"),
                    prep.Plan.Entry.RegistrationId);
            }
            if (prep.Work.IsFaulted)
            {
                Exception miss = prep.Work.Exception!.GetBaseException();
                prep.Dispose();
                return VkRenderFailureRule.IsFatal(miss)
                    ? throw miss
                    : Fail(
                    prep.Plan.Selection,
                    PrepMiss(prep.Plan.Selection, miss.Message),
                    prep.Plan.Entry.RegistrationId);
            }

            if (_queued is not null
                || prep.Plan.Selection != Capture.Selection)
            {
                prep.Dispose();
                if (_queued is null
                    && Capture.State == RenderPackActivationPhase.FailedToRetail
                    && Capture.Selection.IsCanon)

                    return Capture;
                _queued ??= Capture.Selection;
            }
            else if (prep.Plan.Extent != reach)
            {
                prep.Dispose();
                BeginPrep(prep.Plan with { Extent = reach });
                return Capture;
            }
            else
            {
                var verdict = prep.GrabVerdict();
                if (verdict.MissCause is not null)
                {
                    string? sunsetMiss = verdict.TeardownAssetList();
                    return Fail(
                        prep.Plan.Selection,
                        verdict.MissCause
                        + ComposeSunsetMiss(sunsetMiss),
                        prep.Plan.Entry.RegistrationId);
                }
                return BroadcastReadiedContender(prep.Plan, verdict);
            }
        }

        if (_queued is { } pick)
        {
            _queued = null;
            if (pick.IsCanon)
            {
                string? sunsetMiss = RetireEngaged();
                return BroadcastCanon(pick, sunsetMiss);
            }

            var plan = PlanPick(pick, in reach);
            if (plan is null)
                return Capture;
            BeginPrep(plan);
            return _prep!.Work.IsCompleted ? ImposeAtCycleBoundary(reach) : Capture;
        }

        if (_queuedAutoBackupCause is { } backupCause)
        {
            _queuedAutoBackupCause = null;
            return Fail(Capture.Selection, backupCause);
        }

        if (_queuedAutoFidelity is { } fidelity)
        {
            _queuedAutoFidelity = null;
            var plan = PlanAutomaticFidelity(fidelity, in reach);
            if (plan is null)
                return Capture;
            BeginPrep(plan);
            if (_prep!.Work.IsCompleted)
                return ImposeAtCycleBoundary(reach);
        }
        return Capture;
    }

    private void ImposeAutomaticBulletin(AutomaticBulletin bulletin)
    {
        if (bulletin.Mode == AutomaticBulletinManner.Preserve)
            return;
        _queuedAutoFidelity = null;
        _queuedAutoBackupCause = null;
        _autoFidelity = bulletin.Mode == AutomaticBulletinManner.Initialise
            ? new AtmosphericAutoQualityDriver(
                bulletin.Budgets!,
                bulletin.Initial,
                AtmosphericFidelityTier.Low,
                bulletin.Maximum)
            : null;
    }

    private RenderPackActivationCapture? ImposeRegistryEditAtCycleBoundary()
    {
        var src = _registrySrc;
        if (src is null || Interlocked.Exchange(ref _registryAltered, 0) is 0)
            return null;

        var pick = Capture.Selection;
        if (pick.IsCanon)
            return null;

        long chosenEnrollmentIdent = _prep?.Plan.Entry.RegistrationId
            ?? _engagedEnrollmentIdent;
        if (chosenEnrollmentIdent is 0)

            return null;
        var registry = src.Freeze();
        if (!registry.TryGet(pick.PackId, out RenderPackRegistryEntry latest))
        {
            _queued = null;
            return Fail(
                pick,
                $"Render pack '{pick.PackId}' was withdrawn; macac's default renderer is active.",
                chosenEnrollmentIdent);
        }
        if (!string.Equals(
                latest.Descriptor.PackVersion.ToString(),
                pick.PackVersion,
                StringComparison.Ordinal)
            || (chosenEnrollmentIdent is not 0
                && latest.RegistrationId != chosenEnrollmentIdent))
        {
            _queued = null;
            return Fail(
                pick,
                $"Render pack '{pick.PackId}' was replaced by registration "
                    + $"version {latest.Descriptor.PackVersion}; reselect it to activate the update.",
                chosenEnrollmentIdent);
        }
        return null;
    }
}

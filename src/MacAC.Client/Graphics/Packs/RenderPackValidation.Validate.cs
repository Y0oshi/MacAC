using System.Buffers.Binary;
using MacAC.Extensibility.RenderPacks;
using MacAC.Extensibility.RenderPacks.Spirv;

namespace MacAC.Client.Graphics.Packs;

internal static partial class RenderPackValidator
{
    internal static RenderPackAuditResult VetDescriptor(
        RenderPackCard? descriptor,
        RasterizeBundleHubCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (descriptor is null)
            return Invalid("The pack descriptor is missing.");
        if (!IsStableIdent(descriptor.Id))
            return Invalid("The pack id must be a stable lowercase logical id.");
        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
            return Invalid($"Pack '{descriptor.Id}' has no display name.");
        if (string.IsNullOrWhiteSpace(descriptor.FeatureSummary))
            return Invalid($"Pack '{descriptor.Id}' has no feature summary.");
        if (descriptor.PackVersion is null)
            return Invalid($"Pack '{descriptor.Id}' has no version.");
        if (!RenderPackContract.IsSupported(descriptor.PackApiVersion))
        {
            return Invalid(
                $"Pack '{descriptor.Id}' requires render-pack API "
                + $"{descriptor.PackApiVersion}; this client supports "
                + $"{RenderPackContract.MinimumSupported}..{RenderPackContract.Current}.");
        }

        string? nullRoster = LeadNullRoster(descriptor);
        if (nullRoster is not null)
            return Invalid($"Pack '{descriptor.Id}' has a null {nullRoster} declaration list.");

        foreach (RenderFeature needed in descriptor.RequiredCapabilities)
        {
            if (!capabilities.Available.Contains(needed))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' requires unsupported capability "
                    + $"'{needed}'.");
            }
        }

        var semanticCapabilities =
            VetSemanticCapabilities(descriptor, capabilities);
        if (!semanticCapabilities.Success)
            return semanticCapabilities;

        var idents = VetUniqueIdents(descriptor);
        if (!idents.Success)
            return idents;
        var assetList = VetAssetList(descriptor, capabilities);
        if (!assetList.Success)
            return assetList;
        var passs = VetPasss(descriptor);
        if (!passs.Success)
            return passs;
        var replays = VetReplays(descriptor);
        if (!replays.Success)
            return replays;
        var variants = VetVariants(descriptor);
        if (!variants.Success)
            return variants;
        var prefs = VetPrefs(descriptor);
        if (!prefs.Success)
            return prefs;
        var presets = VetPresets(descriptor, capabilities);
        if (!presets.Success)
            return presets;
        var semantics = VetSemanticRoles(descriptor);
        return !semantics.Success ? semantics : VetAtmosphere(descriptor);
    }

    internal static RenderPackAuditResult VetChosenHoldings(
        RenderPackCard descriptor,
        IRenderPackFiles holdings) =>
        VetChosenHoldings(descriptor, holdings, out _);

    internal static RenderPackAuditResult VetChosenHoldings(
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        out ValidatedRasterizeBundleShaderHoldings? validatedHoldings)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(holdings);
        validatedHoldings = null;

        ClientShaderValidationRequest[] reqs =
        [
            .. descriptor.Passes
                        .SelectMany(static pass => new[]
                        {
                            new ClientShaderValidationRequest(
                                pass.VertexShaderAsset, ShaderStage.Vertex, pass, null),
                            new ClientShaderValidationRequest(
                                pass.FragmentShaderAsset, ShaderStage.Fragment, pass, null),
                        })
,
            .. descriptor.PipelineVariants.SelectMany(static variant => new[]
                {
                    new ClientShaderValidationRequest(
                        variant.VertexShaderAsset, ShaderStage.Vertex, null, variant),
                    new ClientShaderValidationRequest(
                        variant.FragmentShaderAsset, ShaderStage.Fragment, null, variant),
                }),
        ];

        byte[] scanBuf = new byte[4096];
        var validated = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (IGrouping<string, ClientShaderValidationRequest> cluster in
            reqs.GroupBy(static req => req.Key, StringComparer.Ordinal))
        {
            string tag = cluster.Key;
            if (!IsSafeAssetTag(tag))
                return Invalid($"Pack '{descriptor.Id}' declares unsafe asset key '{tag}'.");

            try
            {
                using Stream flow = holdings.OpenScan(tag);
                if (flow is null || !flow.CanRead)
                    return Invalid($"Pack '{descriptor.Id}' asset '{tag}' is not readable.");
                using MemoryStream dest = new MemoryStream();
                while (true)
                {
                    int tally = flow.Read(scanBuf);
                    if (tally is 0)
                        break;
                    if (dest.Length + tally > CeilingShaderOctets)
                    {
                        return Invalid(
                            $"Pack '{descriptor.Id}' asset '{tag}' exceeds "
                            + $"the {CeilingShaderOctets}-byte shader ceiling.");
                    }
                    dest.Write(scanBuf, 0, tally);
                }

                byte[] spirv = dest.ToArray();
                if (spirv.Length < 4
                    || (spirv.Length & 3) is not 0
                    || BinaryPrimitives.ReadUInt32LittleEndian(spirv) != SpirvMagic)
                {
                    return Invalid(
                        $"Pack '{descriptor.Id}' asset '{tag}' is not valid SPIR-V.");
                }

                foreach (ClientShaderValidationRequest request in cluster)
                {
                    SpirvVerdict validation = request.Pass is not null
                        ? SpirvGate.VetPassShader(
                            spirv, request.Stage, request.Pass)
                        : SpirvGate.VetPipeVariantShader(
                            spirv, request.Stage, request.Variant!);
                    if (!validation.Success)
                    {
                        return Invalid(
                            $"Pack '{descriptor.Id}' asset '{tag}' fails render-pack shader ABI v1: "
                            + validation.Reason + ".");
                    }
                }
                validated.Add(tag, spirv);
            }
            catch (Exception problem) when (problem is not OutOfMemoryException)
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' asset '{tag}' could not be opened: "
                    + problem.GetBaseException().Message);
            }
        }

        validatedHoldings = new ValidatedRasterizeBundleShaderHoldings(validated);
        return RenderPackAuditResult.Valid();
    }

    internal static RenderPackAuditResult VetPresetCompatibility(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        RasterizeBundleHubCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(capabilities);
        foreach (RenderFeature needed in preset.RequiredCapabilities)
        {
            if (!capabilities.Available.Contains(needed))
            {
                return Invalid(
                    $"Preset '{preset.Id}' requires unsupported capability '{needed}'.");
            }
        }
        if (preset.Semantic == RenderQualityRole.Automatic
            && !capabilities.Available.Contains(RenderFeature.GpuTimestampQueries))
        {
            return Invalid(
                $"Preset '{preset.Id}' requires asynchronous GPU timestamp queries "
                + "because Auto evaluates the complete CPU/GPU pack cost; explicit "
                + "Low remains available when its resource limits fit.");
        }
        if (preset.Semantic == RenderQualityRole.Automatic)
            return RenderPackAuditResult.Valid();

        if (preset.MaxResidentGpuBytes > capabilities.MaxPackResidentBytes)
        {
            return Invalid(
                $"Preset '{preset.Id}' declares a {preset.MaxResidentGpuBytes}-byte "
                + $"resident GPU ceiling, but this host permits "
                + $"{capabilities.MaxPackResidentBytes} bytes under its "
                + $"{capabilities.MemoryPolicyDescription} policy.");
        }

        var substitutions = preset.ResourceOverrides
            .ToDictionary(val => val.ResourceId, StringComparer.OrdinalIgnoreCase);
        foreach (RenderResourceSpec asset in descriptor.Resources)
        {
            RenderExtentSpec? reach = substitutions.TryGetValue(
                    asset.Id,
                    out QualityResourceTweak? assetOverride)
                ? assetOverride.Extent ?? asset.Extent
                : asset.Extent;
            if (reach is null)
                continue;
            if (reach.Layers > capabilities.MaxImageArrayLayers)
            {
                return Invalid(
                    $"Preset '{preset.Id}' resource '{asset.Id}' needs "
                    + $"{reach.Layers} image-array layers; this device provides "
                    + $"{capabilities.MaxImageArrayLayers}.");
            }
            if (reach.Mode == ExtentRule.AbsolutePixels
                && (reach.Width > capabilities.MaxImageDimension2D
                    || reach.Height > capabilities.MaxImageDimension2D))
            {
                return Invalid(
                    $"Preset '{preset.Id}' resource '{asset.Id}' needs "
                    + $"{reach.Width:G}x{reach.Height:G}; this device's maximum "
                    + $"2-D image edge is {capabilities.MaxImageDimension2D}.");
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetSemanticRoles(
        RenderPackCard descriptor)
    {
        var unique = UniqueNonCustomSemantics(
            descriptor,
            descriptor.Resources,
            static val => val.Semantic,
            RenderResourceRole.Custom,
            "resource");
        if (!unique.Success) return unique;
        unique = UniqueNonCustomSemantics(
            descriptor,
            descriptor.Passes,
            static val => val.Semantic,
            RenderPassRole.CustomFullscreen,
            "pass");
        if (!unique.Success) return unique;
        unique = UniqueNonCustomSemantics(
            descriptor,
            descriptor.PipelineVariants,
            static val => val.Semantic,
            PipelineVariantRole.Custom,
            "pipeline variant");
        if (!unique.Success) return unique;
        unique = UniqueNonCustomSemantics(
            descriptor,
            descriptor.QualityPresets,
            static val => val.Semantic,
            RenderQualityRole.Custom,
            "quality preset");
        if (!unique.Success) return unique;
        unique = UniqueNonCustomSemantics(
            descriptor,
            descriptor.Settings,
            static val => val.Semantic,
            RenderSettingRole.Custom,
            "setting");
        if (!unique.Success) return unique;

        var automaticSetting = descriptor.Settings.FirstOrDefault(
            static val => val.Semantic == RenderSettingRole.AutomaticQuality);
        if (automaticSetting is not null && automaticSetting.Kind != SettingValueKind.Boolean)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' AutomaticQuality setting must be Boolean.");
        }
        if (automaticSetting is not null
            || descriptor.QualityPresets.Any(static val =>
                val.Semantic == RenderQualityRole.Automatic))
        {
            foreach (RenderQualityRole semantic in new[]
            {
                RenderQualityRole.Low,
                RenderQualityRole.Medium,
                RenderQualityRole.High,
            })
            {
                if (!descriptor.QualityPresets.Any(val =>
                        val.Semantic == semantic && val.AutoEligible))
                {
                    return Invalid(
                        $"Pack '{descriptor.Id}' declares Automatic quality but has no "
                        + $"AutoEligible '{semantic}' semantic preset.");
                }
            }
        }

        bool atmosphericExecutor = descriptor.Passes.Any(static val =>
            val.Semantic != RenderPassRole.CustomFullscreen);
        if (!atmosphericExecutor)
            return RenderPackAuditResult.Valid();

        bool directedShadeSole = descriptor.Passes.Any(static val =>
                val.Semantic == RenderPassRole.DirectionalShadowDepth)
            && descriptor.Passes.All(static val =>
                val.Semantic is RenderPassRole.CustomFullscreen
                    or RenderPassRole.DirectionalShadowDepth);
        if (directedShadeSole)
            return VetDirectedShadeProfile(descriptor);

        RenderPassRole[] neededPasss =
        [
            RenderPassRole.DirectionalShadowDepth,
            RenderPassRole.SunOcclusion,
            RenderPassRole.SunRays,
            RenderPassRole.VolumetricShafts,
            RenderPassRole.BloomDownsample,
            RenderPassRole.BloomBlurHorizontal,
            RenderPassRole.BloomBlurVertical,
            RenderPassRole.FilmicComposite,
        ];
        foreach (RenderPassRole semantic in neededPasss)
        {
            if (!descriptor.Passes.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' requests the atmospheric executor but "
                    + $"does not declare required pass semantic '{semantic}'.");
            }
        }

        RenderResourceRole[] neededAssetList =
        [
            RenderResourceRole.MainWorldHdr,
            RenderResourceRole.BloomPing,
            RenderResourceRole.BloomPong,
            RenderResourceRole.SunOcclusionMask,
            RenderResourceRole.SunRays,
            RenderResourceRole.DirectionalShadowDepth,
            RenderResourceRole.VolumetricShafts,
        ];
        foreach (RenderResourceRole semantic in neededAssetList)
        {
            if (!descriptor.Resources.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' requests the atmospheric executor but "
                    + $"does not declare required resource semantic '{semantic}'.");
            }
        }

        if (descriptor.SceneReplays.Count(val =>
                val.Semantic == SceneReplayRole.OutdoorDirectionalShadowCasters) is not 1)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare exactly one outdoor directional-shadow replay.");
        }

        bool usesMultiview = descriptor.QualityPresets.Any(preset =>
            (preset.ExecutionHints & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0);
        List<PipelineVariantRole> neededVariants =
        [
            PipelineVariantRole.TerrainDirectionalShadowCaster,
            PipelineVariantRole.WorldOpaqueDirectionalShadowCaster,
            PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster,
            PipelineVariantRole.TerrainDirectionalShadowReceiver,
            PipelineVariantRole.WorldDirectionalShadowReceiver,
        ];
        if (usesMultiview)
        {
            neededVariants.Add(PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster);
            neededVariants.Add(PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster);
            neededVariants.Add(PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster);
        }
        foreach (PipelineVariantRole semantic in neededVariants)
        {
            if (!descriptor.PipelineVariants.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' does not declare required pipeline-variant "
                    + $"semantic '{semantic}'.");
            }
        }

        RenderSettingRole[] neededPrefs =
        [
            RenderSettingRole.BloomStrength,
            RenderSettingRole.FilmicStrength,
            RenderSettingRole.Exposure,
            RenderSettingRole.GradeSaturation,
            RenderSettingRole.GradeContrast,
            RenderSettingRole.VignetteStrength,
            RenderSettingRole.SunRayStrength,
            RenderSettingRole.DirectionalShadowStrength,
            RenderSettingRole.DirectionalShadowReachMetres,
            RenderSettingRole.DirectionalShadowPcfTaps,
            RenderSettingRole.VolumetricStrength,
            RenderSettingRole.VolumetricRayMarchSteps,
        ];
        foreach (RenderSettingRole semantic in neededPrefs)
        {
            if (!descriptor.Settings.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' does not declare required atmospheric "
                    + $"setting semantic '{semantic}'.");
            }
        }
        if (descriptor.AtmospherePolicy is null)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare its visible sun/day-group "
                + "atmosphere policy.");
        }
        if (descriptor.AtmospherePolicy.DirectedShadeLampElevationResponse is not { Count: >= 2 })
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare a directional-shadow "
                + "light-elevation response curve.");
        }
        if (descriptor.AtmospherePolicy.VolumetricShaftSunElevationResponse is not { Count: >= 2 })
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare a volumetric-shaft "
                + "sun-elevation response curve.");
        }

        var forms = VetAtmosphericSemanticForms(descriptor);
        return !forms.Success ? forms : VetAtmosphericSemanticRims(descriptor);
    }

    private static RenderPackAuditResult VetDirectedShadeProfile(
        RenderPackCard descriptor)
    {
        if (descriptor.HighestTier < PackTier.Tier2)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' declares directional shadows below Tier2.");
        }

        RenderFeature[] neededCapabilities =
        [
            RenderFeature.MainWorldColorIntermediate,
            RenderFeature.FullscreenPasses,
            RenderFeature.AuthoredCelestialDirectionalLight,
            RenderFeature.AuthoredWeather,
            RenderFeature.DirectionalShadowMaps,
            RenderFeature.OutdoorDirectionalShadowCasterReplay,
            RenderFeature.AnimatedCasterTransforms,
            RenderFeature.AlphaCutoutShadowCasters,
        ];
        foreach (RenderFeature capability in neededCapabilities)
        {
            if (!descriptor.RequiredCapabilities.Contains(capability))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' directional shadows must explicitly require "
                    + $"capability '{capability}'.");
            }
        }

        if (descriptor.Passes.Count(static val =>
                val.Semantic == RenderPassRole.DirectionalShadowDepth) is not 1)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare exactly one directional-shadow pass.");
        }
        if (descriptor.Resources.Count(static val =>
                val.Semantic == RenderResourceRole.DirectionalShadowDepth) is not 1)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare exactly one directional-shadow resource.");
        }
        if (descriptor.SceneReplays.Count is not 1
            || descriptor.SceneReplays[0].Semantic
                != SceneReplayRole.OutdoorDirectionalShadowCasters)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare exactly one outdoor "
                + "directional-shadow replay.");
        }

        bool usesMultiview = descriptor.QualityPresets.Any(preset =>
            (preset.ExecutionHints & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0);
        List<PipelineVariantRole> neededVariants =
        [
            PipelineVariantRole.TerrainDirectionalShadowCaster,
            PipelineVariantRole.WorldOpaqueDirectionalShadowCaster,
            PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster,
            PipelineVariantRole.TerrainDirectionalShadowReceiver,
            PipelineVariantRole.WorldDirectionalShadowReceiver,
        ];
        if (usesMultiview)
        {
            neededVariants.Add(PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster);
            neededVariants.Add(PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster);
            neededVariants.Add(PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster);
        }
        if (descriptor.PipelineVariants.Count != neededVariants.Count)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' directional shadows require exactly {neededVariants.Count} "
                + "semantic pipeline variants for its execution hints.");
        }
        foreach (PipelineVariantRole semantic in neededVariants)
        {
            if (!descriptor.PipelineVariants.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' does not declare required pipeline-variant "
                    + $"semantic '{semantic}'.");
            }
        }

        foreach (RenderSettingRole semantic in new[]
        {
            RenderSettingRole.DirectionalShadowStrength,
            RenderSettingRole.DirectionalShadowReachMetres,
            RenderSettingRole.DirectionalShadowPcfTaps,
        })
        {
            if (!descriptor.Settings.Any(val => val.Semantic == semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' does not declare required directional-shadow "
                    + $"setting semantic '{semantic}'.");
            }
        }

        if (descriptor.AtmospherePolicy?.DirectedShadeLampElevationResponse
            is not { Count: >= 2 })
        {
            return Invalid(
                $"Pack '{descriptor.Id}' must declare a directional-shadow "
                + "light-elevation response curve.");
        }

        var shade = descriptor.Passes.Single(val =>
            val.Semantic == RenderPassRole.DirectionalShadowDepth);
        var zDepth = descriptor.Resources.Single(val =>
            val.Semantic == RenderResourceRole.DirectionalShadowDepth);
        if (shade.Hook != RenderPassAnchor.ShadowDepthBeforeWorld
            || shade.ResourceReads.Count is not 0
            || shade.ResourceWrites.Count is not 1
            || !string.Equals(
                shade.ResourceWrites[0], zDepth.Id, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                $"Pack '{descriptor.Id}' directional-shadow pass must run before the world, "
                + "read no declared resource, and write its directional-depth resource.");
        }
        if (zDepth.Kind != GpuResourceKind.Image2DArray
            || zDepth.Format != PixelFormatKind.DirectionalDepth
            || zDepth.Extent?.Mode != ExtentRule.AbsolutePixels
            || zDepth.Usage != (GpuResourceUsage.Sampled | GpuResourceUsage.DepthAttachment)
            || zDepth.Lifetime != GpuResourceLifetime.ActivePack)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' directional-shadow resource does not match the "
                + "host executor's array-depth contract.");
        }

        var forms = VetDirectedShadeForms(descriptor);
        if (!forms.Success)
            return forms;

        RenderPassSpec[] productPasss = [.. descriptor.Passes
            .Where(static val => val.Hook == RenderPassAnchor.ToneMap
                && val.ResourceWrites.Count is 0)];
        if (productPasss.Length is not 1
            || productPasss[0].Semantic != RenderPassRole.CustomFullscreen
            || !productPasss[0].SemanticInputs.Contains(SemanticInput.WorldColor))
        {
            return Invalid(
                $"Pack '{descriptor.Id}' directional shadows require exactly one custom "
                + "ToneMap output-copy pass sampling WorldColor.");
        }
        return descriptor.Passes.Any(val =>
                val.Semantic == RenderPassRole.CustomFullscreen
                && val.Hook is RenderPassAnchor.ShadowDepthBeforeWorld
                    or RenderPassAnchor.AfterToneMapBeforePrivateViewports)
            ? Invalid(
                $"Pack '{descriptor.Id}' uses an unsupported custom pass hook in its "
                + "directional-shadow graph.")
            : RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetDirectedShadeForms(
        RenderPackCard descriptor)
    {
        PipelineVariantRole[] semantics =
        [
            PipelineVariantRole.TerrainDirectionalShadowCaster,
            PipelineVariantRole.WorldOpaqueDirectionalShadowCaster,
            PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster,
            PipelineVariantRole.TerrainDirectionalShadowReceiver,
            PipelineVariantRole.WorldDirectionalShadowReceiver,
        ];
        PipelineBaseRole[] bases =
        [
            PipelineBaseRole.Terrain,
            PipelineBaseRole.WorldMesh,
            PipelineBaseRole.WorldMesh,
            PipelineBaseRole.Terrain,
            PipelineBaseRole.WorldMesh,
        ];
        MaterialKind[] matls =
        [
            MaterialKind.Opaque,
            MaterialKind.Opaque | MaterialKind.AnimatedOpaque,
            MaterialKind.AlphaCutout | MaterialKind.AnimatedAlphaCutout,
            MaterialKind.Opaque,
            MaterialKind.Opaque | MaterialKind.AlphaCutout
                | MaterialKind.AnimatedOpaque
                | MaterialKind.AnimatedAlphaCutout,
        ];
        SemanticInput[][] feeds =
        [
            [SemanticInput.CameraMatrices],
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms],
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms],
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight],
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight],
        ];
        for (int idx = 0; idx < semantics.Length; ++idx)
        {
            var variant = descriptor.PipelineVariants.Single(val =>
                val.Semantic == semantics[idx]);
            if (variant.BaseSemantic != bases[idx]
                || variant.CompatibleMaterials != matls[idx]
                || !variant.SemanticInputs.SequenceEqual(feeds[idx]))
            {
                return Invalid(
                    $"Pipeline variant semantic '{semantics[idx]}' does not match the fixed "
                    + "directional-shadow executor contract.");
            }
        }

        if (descriptor.QualityPresets.Any(preset =>
                (preset.ExecutionHints & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0))
        {
            PipelineVariantRole[] multiview =
            [
                PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster,
                PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster,
                PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster,
            ];
            for (int idx = 0; idx < multiview.Length; ++idx)
            {
                var variant = descriptor.PipelineVariants.Single(val =>
                    val.Semantic == multiview[idx]);
                if (variant.BaseSemantic != bases[idx]
                    || variant.CompatibleMaterials != matls[idx]
                    || !variant.SemanticInputs.SequenceEqual(feeds[idx]))
                {
                    return Invalid(
                        $"Pipeline variant semantic '{multiview[idx]}' does not match the fixed "
                        + "multiview directional-shadow executor contract.");
                }
            }
        }

        var rerun = descriptor.SceneReplays[0];
        const ShadowCasterKind neededCasters = ShadowCasterKind.Terrain
            | ShadowCasterKind.OpaqueWorld
            | ShadowCasterKind.AlphaCutoutWorld
            | ShadowCasterKind.AnimatedOpaque
            | ShadowCasterKind.AnimatedAlphaCutout;
        return rerun.CasterClasses != neededCasters || rerun.ViewCount is not 4
            ? Invalid(
                $"Pack '{descriptor.Id}' outdoor directional-shadow replay must declare "
                + "all five headline caster classes and four maximum cascade views.")
            : RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetAtmosphericSemanticForms(
        RenderPackCard descriptor)
    {
        var outcome = Asset(
            RenderResourceRole.MainWorldHdr,
            GpuResourceKind.Image2D,
            PixelFormatKind.HdrColor,
            ExtentRule.RelativeToMainWorld,
            GpuResourceUsage.Sampled | GpuResourceUsage.ColorAttachment);
        if (!outcome.Success) return outcome;
        foreach (RenderResourceRole semantic in new[]
        {
            RenderResourceRole.BloomPing,
            RenderResourceRole.BloomPong,
            RenderResourceRole.SunRays,
            RenderResourceRole.VolumetricShafts,
        })
        {
            outcome = Asset(
                semantic,
                GpuResourceKind.Image2D,
                PixelFormatKind.HdrColor,
                ExtentRule.RelativeToMainWorld,
                GpuResourceUsage.Sampled | GpuResourceUsage.ColorAttachment);
            if (!outcome.Success) return outcome;
        }
        outcome = Asset(
            RenderResourceRole.SunOcclusionMask,
            GpuResourceKind.Image2D,
            PixelFormatKind.SingleChannel,
            ExtentRule.RelativeToMainWorld,
            GpuResourceUsage.Sampled | GpuResourceUsage.ColorAttachment);
        if (!outcome.Success) return outcome;
        outcome = Asset(
            RenderResourceRole.DirectionalShadowDepth,
            GpuResourceKind.Image2DArray,
            PixelFormatKind.DirectionalDepth,
            ExtentRule.AbsolutePixels,
            GpuResourceUsage.Sampled | GpuResourceUsage.DepthAttachment);
        if (!outcome.Success) return outcome;

        outcome = Variant(
            PipelineVariantRole.TerrainDirectionalShadowCaster,
            PipelineBaseRole.Terrain,
            MaterialKind.Opaque,
            [SemanticInput.CameraMatrices]);
        if (!outcome.Success) return outcome;
        outcome = Variant(
            PipelineVariantRole.WorldOpaqueDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            MaterialKind.Opaque | MaterialKind.AnimatedOpaque,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]);
        if (!outcome.Success) return outcome;
        outcome = Variant(
            PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            MaterialKind.AlphaCutout | MaterialKind.AnimatedAlphaCutout,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]);
        if (!outcome.Success) return outcome;
        outcome = Variant(
            PipelineVariantRole.TerrainDirectionalShadowReceiver,
            PipelineBaseRole.Terrain,
            MaterialKind.Opaque,
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight]);
        if (!outcome.Success) return outcome;
        outcome = Variant(
            PipelineVariantRole.WorldDirectionalShadowReceiver,
            PipelineBaseRole.WorldMesh,
            MaterialKind.Opaque | MaterialKind.AlphaCutout
                | MaterialKind.AnimatedOpaque
                | MaterialKind.AnimatedAlphaCutout,
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight]);
        if (!outcome.Success) return outcome;

        var rerun = descriptor.SceneReplays.Single(val =>
            val.Semantic == SceneReplayRole.OutdoorDirectionalShadowCasters);
        const ShadowCasterKind neededCasters = ShadowCasterKind.Terrain
            | ShadowCasterKind.OpaqueWorld
            | ShadowCasterKind.AlphaCutoutWorld
            | ShadowCasterKind.AnimatedOpaque
            | ShadowCasterKind.AnimatedAlphaCutout;
        return rerun.CasterClasses != neededCasters || rerun.ViewCount is not 4
            ? Invalid(
                $"Pack '{descriptor.Id}' outdoor directional-shadow replay must declare "
                + "all five headline caster classes and four maximum cascade views.")
            : RenderPackAuditResult.Valid();
        RenderPackAuditResult Asset(
            RenderResourceRole semantic,
            GpuResourceKind sort,
            PixelFormatKind fmt,
            ExtentRule reachManner,
            GpuResourceUsage usage)
        {
            var asset = descriptor.Resources.Single(val =>
                val.Semantic == semantic);
            return asset.Kind != sort
                || asset.Format != fmt
                || asset.Extent?.Mode != reachManner
                || asset.Usage != usage
                || asset.Lifetime != GpuResourceLifetime.ActivePack
                ? Invalid(
                    $"Resource semantic '{semantic}' does not match the fixed atmospheric "
                    + "executor's kind, format, extent, usage, and lifetime contract.")
                : RenderPackAuditResult.Valid();
        }

        RenderPackAuditResult Variant(
            PipelineVariantRole semantic,
            PipelineBaseRole baseSemantic,
            MaterialKind matls,
            IReadOnlyList<SemanticInput> feeds)
        {
            var variant = descriptor.PipelineVariants.Single(val =>
                val.Semantic == semantic);
            return variant.BaseSemantic != baseSemantic
                || variant.CompatibleMaterials != matls
                || !variant.SemanticInputs.SequenceEqual(feeds)
                ? Invalid(
                    $"Pipeline variant semantic '{semantic}' does not match the fixed "
                    + "atmospheric executor's base, material, and input contract.")
                : RenderPackAuditResult.Valid();
        }
    }

    private static RenderPackAuditResult VetAtmosphericSemanticRims(
        RenderPackCard descriptor)
    {
        if (descriptor.Passes.Count is not 8
            || descriptor.Resources.Count is not 7
            || descriptor.PipelineVariants.Count != (descriptor.QualityPresets.Any(preset =>
                    (preset.ExecutionHints & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0)
                ? 8 : 5)
            || descriptor.SceneReplays.Count is not 1)
        {
            return Invalid(
                $"Pack '{descriptor.Id}' requests the fixed atmospheric executor; API v1 "
                + "requires exactly 8 semantic passes, 7 semantic resources, and the declared semantic "
                + "pipeline variants, and 1 semantic scene replay.");
        }

        RenderPackAuditResult Tap(RenderPassRole semantic, RenderPassAnchor tap)
        {
            var pass = descriptor.Passes.Single(val =>
                val.Semantic == semantic);
            return pass.Hook == tap
                ? RenderPackAuditResult.Valid()
                : Invalid(
                    $"Pass semantic '{semantic}' must run at hook '{tap}', not '{pass.Hook}'.");
        }

        var outcome = Tap(
            RenderPassRole.DirectionalShadowDepth,
            RenderPassAnchor.ShadowDepthBeforeWorld);
        if (!outcome.Success) return outcome;
        foreach (RenderPassRole semantic in new[]
        {
            RenderPassRole.SunOcclusion,
            RenderPassRole.SunRays,
            RenderPassRole.VolumetricShafts,
            RenderPassRole.BloomDownsample,
            RenderPassRole.BloomBlurHorizontal,
            RenderPassRole.BloomBlurVertical,
        })
        {
            outcome = Tap(semantic, RenderPassAnchor.AtmosphereBeforeToneMap);
            if (!outcome.Success) return outcome;
        }
        outcome = Tap(RenderPassRole.FilmicComposite, RenderPassAnchor.ToneMap);
        if (!outcome.Success) return outcome;

        RenderPassRole[] declaredOrdering = [.. descriptor.Passes
            .Where(static pass => pass.Hook is RenderPassAnchor.AtmosphereBeforeToneMap
                or RenderPassAnchor.ToneMap)
            .Select(static pass => pass.Semantic)];
        RenderPassRole[] neededOrdering =
        [
            RenderPassRole.SunOcclusion,
            RenderPassRole.SunRays,
            RenderPassRole.VolumetricShafts,
            RenderPassRole.BloomDownsample,
            RenderPassRole.BloomBlurHorizontal,
            RenderPassRole.BloomBlurVertical,
            RenderPassRole.FilmicComposite,
        ];
        if (!declaredOrdering.SequenceEqual(neededOrdering))
        {
            return Invalid(
                $"Pack '{descriptor.Id}' atmospheric pass order does not match the "
                + "renderer-owned semantic execution order.");
        }

        outcome = Rim(RenderPassRole.DirectionalShadowDepth, [],
            RenderResourceRole.DirectionalShadowDepth);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.SunOcclusion, [],
            RenderResourceRole.SunOcclusionMask);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.SunRays,
            [RenderResourceRole.SunOcclusionMask], RenderResourceRole.SunRays);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.VolumetricShafts,
            [RenderResourceRole.DirectionalShadowDepth],
            RenderResourceRole.VolumetricShafts);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.BloomDownsample,
            [RenderResourceRole.SunRays, RenderResourceRole.VolumetricShafts],
            RenderResourceRole.BloomPing);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.BloomBlurHorizontal,
            [RenderResourceRole.BloomPing], RenderResourceRole.BloomPong);
        if (!outcome.Success) return outcome;
        outcome = Rim(RenderPassRole.BloomBlurVertical,
            [RenderResourceRole.BloomPong], RenderResourceRole.BloomPing);
        return !outcome.Success
            ? outcome
            : Rim(RenderPassRole.FilmicComposite,
            [RenderResourceRole.BloomPing, RenderResourceRole.SunRays,
                RenderResourceRole.VolumetricShafts],
            product: null);
        RenderPackAuditResult Rim(
            RenderPassRole passSemantic,
            IReadOnlyList<RenderResourceRole> reads,
            RenderResourceRole? product)
        {
            var pass = descriptor.Passes.Single(val =>
                val.Semantic == passSemantic);
            RenderResourceRole[] actualReads = [.. pass.ResourceReads
                .Select(ident => descriptor.Resources.Single(asset => string.Equals(
                    asset.Id,
                    ident,
                    StringComparison.OrdinalIgnoreCase)).Semantic)];
            if (!actualReads.SequenceEqual(reads))
            {
                return Invalid(
                    $"Pass semantic '{passSemantic}' declares resource reads that do not "
                    + "match its renderer-owned execution edges.");
            }
            RenderResourceRole[] actualWrites = [.. pass.ResourceWrites
                .Select(ident => descriptor.Resources.Single(asset => string.Equals(
                    asset.Id,
                    ident,
                    StringComparison.OrdinalIgnoreCase)).Semantic)];
            RenderResourceRole[] anticipatedWrites = product is { } semantic
                ? [semantic]
                : [];
            return actualWrites.SequenceEqual(anticipatedWrites)
                ? RenderPackAuditResult.Valid()
                : Invalid(
                    $"Pass semantic '{passSemantic}' declares a resource output that does "
                    + "not match its renderer-owned execution edge.");
        }
    }

    private static RenderPackAuditResult VetUniqueIdents(RenderPackCard descriptor)
    {
        HashSet<string> idents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<(string Kind, string? Id)> declarations =
            descriptor.Resources.Select(static val => ("resource", val?.Id))
                .Concat(descriptor.Passes.Select(static val => ("pass", val?.Id)))
                .Concat(descriptor.SceneReplays.Select(static val => ("scene replay", val?.Id)))
                .Concat(descriptor.PipelineVariants.Select(static val => ("pipeline variant", val?.Id)))
                .Concat(descriptor.QualityPresets.Select(static val => ("quality preset", val?.Id)))
                .Concat(descriptor.Settings.Select(static val => ("setting", val?.Id)));

        foreach ((string sort, string? ident) in declarations)
        {
            if (!IsStableIdent(ident))
                return Invalid($"Pack '{descriptor.Id}' has an invalid {sort} id.");
            if (!idents.Add($"{sort}:{ident}"))
                return Invalid($"Pack '{descriptor.Id}' declares duplicate {sort} id '{ident}'.");
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetAssetList(
        RenderPackCard descriptor,
        RasterizeBundleHubCapabilities capabilities)
    {
        long declaredOctets = 0;
        foreach (RenderResourceSpec? asset in descriptor.Resources)
        {
            if (asset is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null resource declaration.");
            if (asset.Kind == GpuResourceKind.Buffer
                || asset.Format == PixelFormatKind.StructuredData
                || asset.Usage.HasFlag(GpuResourceUsage.Storage))
            {
                return Invalid(
                    $"Resource '{asset.Id}' uses a buffer/storage declaration reserved "
                    + "for a future render-pack API; API v1 binds image resources only.");
            }
            if (asset.Kind == GpuResourceKind.Image2DArray
                && asset.Format != PixelFormatKind.DirectionalDepth)
            {
                return Invalid(
                    $"Resource '{asset.Id}' uses a color image array; render-pack API "
                    + "v1 reserves image arrays for directional depth maps.");
            }
            if (asset.EstimatedResidentBytes < 0 || asset.SizeBytes < 0)
                return Invalid($"Resource '{asset.Id}' declares negative bytes.");
            if (asset.Usage == GpuResourceUsage.None)
                return Invalid($"Resource '{asset.Id}' declares no usage.");
            if (asset.Kind == GpuResourceKind.Buffer && asset.Extent is not null)
                return Invalid($"Buffer resource '{asset.Id}' must not declare an image extent.");
            if (asset.Kind != GpuResourceKind.Buffer)
            {
                if (asset.Extent is null)
                    return Invalid($"Image resource '{asset.Id}' has no extent.");
                var reach = asset.Extent;
                if (!IsFinitePositive(reach.Width)
                    || !IsFinitePositive(reach.Height)
                    || reach.Layers <= 0
                    || reach.Layers > AbsoluteImageArrStratumCeiling)
                {
                    return Invalid($"Image resource '{asset.Id}' has an invalid extent.");
                }
                if (reach.Mode == ExtentRule.AbsolutePixels
                    && (reach.Width > AbsoluteImageDimension2DCeiling
                        || reach.Height > AbsoluteImageDimension2DCeiling))
                {
                    return Invalid($"Image resource '{asset.Id}' exceeds the device image limit.");
                }
                if (reach.Mode != ExtentRule.AbsolutePixels
                    && (reach.Width > 1.0 || reach.Height > 1.0))
                {
                    return Invalid($"Relative resource '{asset.Id}' must use a scale in (0, 1].");
                }
            }

            if (!TryAppend(ref declaredOctets, asset.EstimatedResidentBytes))
                return Invalid($"Pack '{descriptor.Id}' resource byte total overflows.");
        }

        return declaredOctets > AbsoluteBundleByteCeiling
            ? Invalid(
                $"Pack '{descriptor.Id}' declares {declaredOctets} resident bytes; "
                + $"the render-pack API ceiling is {AbsoluteBundleByteCeiling}.")
            : RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetPasss(RenderPackCard descriptor)
    {
        var assetList = descriptor.Resources
            .ToDictionary(static val => val.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (RenderPassSpec? pass in descriptor.Passes)
        {
            if (pass is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null pass declaration.");
            if (!IsSafeAssetTag(pass.VertexShaderAsset)
                || !IsSafeAssetTag(pass.FragmentShaderAsset))
                return Invalid($"Pass '{pass.Id}' declares an unsafe shader asset key.");
            if (pass.SemanticInputs is null || pass.ResourceReads is null || pass.ResourceWrites is null)
                return Invalid($"Pass '{pass.Id}' contains a null binding list.");
            if (pass.ResourceWrites.Count > 1)
            {
                return Invalid(
                    $"Pass '{pass.Id}' writes {pass.ResourceWrites.Count} resources; "
                    + "render-pack API v1 supports one attachment per declared pass.");
            }
            if (pass.ResourceWrites.Count is 0
                && pass.Hook is not RenderPassAnchor.ToneMap
                    and not RenderPassAnchor.AfterToneMapBeforePrivateViewports)
            {
                return Invalid(
                    $"Pass '{pass.Id}' has no declared output at hook '{pass.Hook}'.");
            }
            int sampledFeeds = pass.SemanticInputs.Count(static semantic =>
                semantic is SemanticInput.WorldColor
                    or SemanticInput.SceneDepth
                    or SemanticInput.SceneNormals);
            foreach (string scan in pass.ResourceReads)
            {
                if (!assetList.TryGetValue(scan, out RenderResourceSpec? asset))
                    return Invalid($"Pass '{pass.Id}' reads unknown resource '{scan}'.");
                if (!written.Contains(scan))
                    return Invalid($"Pass '{pass.Id}' reads resource '{scan}' before it is written.");
                if (!asset.Usage.HasFlag(GpuResourceUsage.Sampled))
                    return Invalid($"Pass '{pass.Id}' samples non-sampled resource '{scan}'.");
                bool dedicatedDirectedSocket =
                    asset.Format == PixelFormatKind.DirectionalDepth
                    && pass.SemanticInputs.Contains(SemanticInput.DirectionalShadowMaps);
                if (!dedicatedDirectedSocket)
                    ++sampledFeeds;
            }
            if (sampledFeeds > 4)
            {
                return Invalid(
                    $"Pass '{pass.Id}' needs {sampledFeeds} ordinary sampled images; "
                    + "render-pack API v1 provides four ordered texture slots (A..D).");
            }
            foreach (string emit in pass.ResourceWrites)
            {
                if (!assetList.TryGetValue(emit, out RenderResourceSpec? asset))
                    return Invalid($"Pass '{pass.Id}' writes unknown resource '{emit}'.");
                GpuResourceUsage affix = asset.Format == PixelFormatKind.DirectionalDepth
                    ? GpuResourceUsage.DepthAttachment
                    : GpuResourceUsage.ColorAttachment;
                if (!asset.Usage.HasFlag(affix))
                    return Invalid($"Pass '{pass.Id}' writes non-attachment resource '{emit}'.");
                written.Add(emit);
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetSemanticCapabilities(
        RenderPackCard descriptor,
        RasterizeBundleHubCapabilities capabilities)
    {
        foreach (RenderPassSpec shadePass in descriptor.Passes.Where(static pass =>
                     pass.Semantic == RenderPassRole.DirectionalShadowDepth))
        {
            if (!shadePass.SemanticInputs.Contains(
                    SemanticInput.SelectedCelestialDirectionalLight)
                || shadePass.SemanticInputs.Contains(SemanticInput.SunDirection))
            {
                return Invalid(
                    $"Directional-shadow pass '{shadePass.Id}' must declare "
                    + $"'{SemanticInput.SelectedCelestialDirectionalLight}' and must not "
                    + "alias the sun-specific atmospheric direction.");
            }
        }

        var feeds = descriptor.Passes
            .SelectMany(static pass => pass?.SemanticInputs ?? [])
            .Concat(descriptor.PipelineVariants.SelectMany(
                static variant => variant?.SemanticInputs ?? []));
        foreach (SemanticInput feed in feeds.Distinct())
        {
            RenderFeature? needed = feed switch
            {
                SemanticInput.WorldColor =>
                    RenderFeature.MainWorldColorIntermediate,
                SemanticInput.SceneDepth =>
                    RenderFeature.SceneDepthSampling,
                SemanticInput.SceneNormals =>
                    RenderFeature.SceneNormalSampling,
                SemanticInput.SunDirection =>
                    RenderFeature.AuthoredSunDirection,
                SemanticInput.SelectedCelestialDirectionalLight =>
                    RenderFeature.AuthoredCelestialDirectionalLight,
                SemanticInput.SunScreenPosition =>
                    RenderFeature.AuthoredSunScreenPosition,
                SemanticInput.ActiveDayGroup or SemanticInput.Weather =>
                    RenderFeature.AuthoredWeather,
                SemanticInput.CameraMatrices or SemanticInput.FrameTime =>
                    RenderFeature.FullscreenPasses,
                SemanticInput.ShadowCasterTransforms =>
                    RenderFeature.AnimatedCasterTransforms,
                SemanticInput.DirectionalShadowMaps =>
                    RenderFeature.DirectionalShadowMaps,
                _ => null,
            };
            if (feed is SemanticInput.SelectedCelestialDirectionalLight
                && needed is { } declaredCapability
                && !descriptor.RequiredCapabilities.Contains(declaredCapability))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' declares semantic '{feed}' but does not "
                    + $"require capability '{declaredCapability}'.");
            }
            if (needed is { } capability
                && !capabilities.Available.Contains(capability))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' declares semantic '{feed}' but the host "
                    + $"does not provide capability '{capability}'.");
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetReplays(RenderPackCard descriptor)
    {
        foreach (SceneReplaySpec? rerun in descriptor.SceneReplays)
        {
            if (rerun is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null scene replay.");
            if (rerun.ViewCount is <= 0 or > 4)
                return Invalid($"Scene replay '{rerun.Id}' must request 1..4 views.");
            if (rerun.CasterClasses == ShadowCasterKind.None)
                return Invalid($"Scene replay '{rerun.Id}' declares no caster classes.");
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetVariants(RenderPackCard descriptor)
    {
        foreach (PipelineVariantSpec? variant in descriptor.PipelineVariants)
        {
            if (variant is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null pipeline variant.");
            if (!IsSafeAssetTag(variant.VertexShaderAsset)
                || !IsSafeAssetTag(variant.FragmentShaderAsset))
                return Invalid($"Pipeline variant '{variant.Id}' declares an unsafe shader asset key.");
            if (variant.CompatibleMaterials == MaterialKind.None)
                return Invalid($"Pipeline variant '{variant.Id}' declares no compatible materials.");
            if (variant.SemanticInputs is null)
                return Invalid($"Pipeline variant '{variant.Id}' has a null semantic-input list.");
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetPrefs(RenderPackCard descriptor)
    {
        if (descriptor.Settings.Count > ShaderAbi.BundleSettingScalarCap)
            return Invalid(
                $"Pack '{descriptor.Id}' exceeds the "
                + $"{ShaderAbi.BundleSettingScalarCap}-setting API-v1 ceiling.");
        foreach (RenderSettingSpec? setting in descriptor.Settings)
        {
            if (setting is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null setting.");
            if (!Enum.IsDefined(setting.Kind))
                return Invalid($"Setting '{setting.Id}' declares an unknown kind.");
            if (string.IsNullOrWhiteSpace(setting.DisplayName))
                return Invalid($"Setting '{setting.Id}' has no display name.");
            if (setting.DefaultValue is null || setting.Choices is null)
                return Invalid($"Setting '{setting.Id}' contains a null value list.");
            if (setting.Minimum is { } lower && !double.IsFinite(lower)
                || setting.Maximum is { } upper && !double.IsFinite(upper)
                || setting.Step is { } hop && (!double.IsFinite(hop) || hop <= 0))
                return Invalid($"Setting '{setting.Id}' has invalid bounds.");
            if (setting.Minimum is { } floor
                && setting.Maximum is { } ceiling
                && floor > ceiling)
                return Invalid($"Setting '{setting.Id}' has an inverted range.");
            if (setting.Kind == SettingValueKind.Choice
                && (setting.Choices is null || setting.Choices.Count is 0))
                return Invalid($"Choice setting '{setting.Id}' declares no choices.");
            if (!SettingValueCodec.TryPack(
                    setting,
                    setting.DefaultValue,
                    out _))
            {
                return Invalid($"Setting '{setting.Id}' has an invalid default value.");
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetPresets(
        RenderPackCard descriptor,
        RasterizeBundleHubCapabilities capabilities)
    {
        if (descriptor.QualityPresets.Count is 0)
            return Invalid($"Pack '{descriptor.Id}' declares no quality presets.");
        HashSet<string> assetList = descriptor.Resources
            .Select(static val => val.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> prefs = descriptor.Settings
            .Select(static val => val.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settingDeclarations =
            descriptor.Settings.ToDictionary(
                val => val.Id,
                StringComparer.OrdinalIgnoreCase);
        const long ceiling = AbsoluteBundleByteCeiling;

        foreach (QualityLadderStep? preset in descriptor.QualityPresets)
        {
            if (preset is null)
                return Invalid($"Pack '{descriptor.Id}' contains a null quality preset.");
            if (string.IsNullOrWhiteSpace(preset.DisplayName))
                return Invalid($"Quality preset '{preset.Id}' has no display name.");
            if (preset.MaxResidentGpuBytes is < 0 or > ceiling)
                return Invalid($"Quality preset '{preset.Id}' exceeds the pack memory ceiling.");
            const QualityExecutionHints supportedExecutionHints =
                QualityExecutionHints.FusedAtmosphericPostProcess
                | QualityExecutionHints.MultiviewDirectionalShadowCascades;
            if ((preset.ExecutionHints & ~supportedExecutionHints) != 0)
                return Invalid($"Quality preset '{preset.Id}' declares an unknown execution hint.");
            if ((preset.ExecutionHints
                    & QualityExecutionHints.FusedAtmosphericPostProcess) != 0
                && preset.Semantic != RenderQualityRole.Low)
            {
                return Invalid(
                    $"Quality preset '{preset.Id}' may only use fused atmospheric post-processing "
                    + "with the Low quality semantic.");
            }
            if ((preset.ExecutionHints
                    & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0
                && preset.Semantic != RenderQualityRole.Low)
            {
                return Invalid(
                    $"Quality preset '{preset.Id}' may only use multiview directional-shadow "
                    + "cascades with the Low quality semantic.");
            }
            if ((preset.ExecutionHints
                    & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0
                && descriptor.Passes.Count(pass =>
                    pass.Semantic == RenderPassRole.DirectionalShadowDepth) is not 1)
            {
                return Invalid(
                    $"Quality preset '{preset.Id}' requests multiview directional-shadow "
                    + "cascades without the directional-shadow graph.");
            }
            if ((preset.ExecutionHints
                    & QualityExecutionHints.MultiviewDirectionalShadowCascades) != 0
                && !preset.RequiredCapabilities.Contains(
                    RenderFeature.MultiviewDirectionalShadowCascades))
            {
                return Invalid(
                    $"Quality preset '{preset.Id}' must require multiview directional-shadow capability.");
            }
            if ((preset.ExecutionHints
                    & QualityExecutionHints.FusedAtmosphericPostProcess) != 0)
            {
                RenderPassRole[] neededFusedPasss =
                [
                    RenderPassRole.SunOcclusion,
                    RenderPassRole.SunRays,
                    RenderPassRole.BloomDownsample,
                    RenderPassRole.BloomBlurHorizontal,
                    RenderPassRole.BloomBlurVertical,
                    RenderPassRole.FilmicComposite,
                ];
                if (neededFusedPasss.Any(semantic =>
                        descriptor.Passes.Count(pass => pass.Semantic == semantic) != 1))
                {
                    return Invalid(
                        $"Quality preset '{preset.Id}' requests fused atmospheric "
                        + "post-processing without the complete standard pass graph.");
                }
            }
            if (!IsFiniteNonNegative(preset.MaxIncrementalGpuMillisecondsP50)
                || !IsFiniteNonNegative(preset.MaxIncrementalGpuMillisecondsP99)
                || !IsFiniteNonNegative(preset.MaxIncrementalCpuMillisecondsP50)
                || !IsFiniteNonNegative(preset.MaxIncrementalCpuMillisecondsP99)
                || preset.MaxIncrementalGpuMillisecondsP50 > preset.MaxIncrementalGpuMillisecondsP99
                || preset.MaxIncrementalCpuMillisecondsP50 > preset.MaxIncrementalCpuMillisecondsP99)
                return Invalid($"Quality preset '{preset.Id}' has invalid performance budgets.");
            foreach (QualityResourceTweak value in preset.ResourceOverrides)
            {
                if (!assetList.Contains(value.ResourceId))
                    return Invalid($"Quality preset '{preset.Id}' overrides unknown resource '{value.ResourceId}'.");
                if (value.SizeBytes < 0 || value.EstimatedResidentBytes < 0)
                    return Invalid($"Quality preset '{preset.Id}' declares negative resource bytes.");
            }
            HashSet<string> overriddenPrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QualitySettingTweak value in preset.SettingOverrides)
            {
                if (!prefs.Contains(value.SettingId))
                    return Invalid($"Quality preset '{preset.Id}' overrides unknown setting '{value.SettingId}'.");
                if (!overriddenPrefs.Add(value.SettingId))
                    return Invalid($"Quality preset '{preset.Id}' overrides setting '{value.SettingId}' more than once.");
                if (!SettingValueCodec.TryPack(
                        settingDeclarations[value.SettingId],
                        value.Value,
                        out _))
                {
                    return Invalid(
                        $"Quality preset '{preset.Id}' supplies an invalid value "
                        + $"for setting '{value.SettingId}'.");
                }
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static RenderPackAuditResult VetAtmosphere(RenderPackCard descriptor)
    {
        var rule = descriptor.AtmospherePolicy;
        if (rule is null)
            return RenderPackAuditResult.Valid();
        if (rule.SunElevationResponse is null
            || rule.ActiveDayGroupMultipliers is null
            || rule.DirectedShadeLampElevationResponse is null
            || rule.VolumetricShaftSunElevationResponse is null)
            return Invalid($"Pack '{descriptor.Id}' has a null atmosphere-policy list.");

        var curve = VetCurve(
            rule.SunElevationResponse,
            "sun-elevation");
        if (!curve.Success)
            return curve;
        curve = VetCurve(
            rule.DirectedShadeLampElevationResponse,
            "directional-shadow light-elevation",
            unitInterval: true);
        if (!curve.Success)
            return curve;
        curve = VetDirectedShadeHorizon(
            rule.DirectedShadeLampElevationResponse);
        if (!curve.Success)
            return curve;
        curve = VetCurve(
            rule.VolumetricShaftSunElevationResponse,
            "volumetric-shaft sun-elevation",
            unitInterval: true);
        if (!curve.Success)
            return curve;

        HashSet<int> clusters = new HashSet<int>();
        foreach (DayGroupWeight val in rule.ActiveDayGroupMultipliers)
        {
            if (!clusters.Add(val.ActiveDayGroup)
                || !IsFiniteNonNegative(val.Multiplier))
                return Invalid($"Pack '{descriptor.Id}' has an invalid active-day-group mapping.");
        }

        if (rule.FoliageWindByWeather is null)
            return Invalid($"Pack '{descriptor.Id}' has a null foliage-wind weather-point list.");
        HashSet<string> weatherSorts = new HashSet<string>(StringComparer.Ordinal);
        foreach (WindWeatherKnot pt in rule.FoliageWindByWeather)
        {
            if (!Enum.TryParse(
                    pt.WeatherKind,
                    ignoreCase: false,
                    out MacAC.Mechanics.Realm.WeatherKind decodedSort)
                || !Enum.IsDefined(decodedSort))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' declares an unknown foliage-wind weather "
                    + $"kind '{pt.WeatherKind}'.");
            }
            if (!weatherSorts.Add(pt.WeatherKind))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' declares the foliage-wind weather kind "
                    + $"'{pt.WeatherKind}' more than once.");
            }
            if (!IsFiniteNonNegative(pt.Mean) || !IsFiniteNonNegative(pt.Gust))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' has an invalid foliage-wind mean/gust value "
                    + $"for weather kind '{pt.WeatherKind}'.");
            }
        }
        return RenderPackAuditResult.Valid();

        RenderPackAuditResult VetCurve(
            IReadOnlyList<SunElevationKnot> pts,
            string label,
            bool unitInterval = false)
        {
            double precedingElevation = double.NegativeInfinity;
            foreach (SunElevationKnot pt in pts)
            {
                if (pt is null
                    || !double.IsFinite(pt.ElevationDegrees)
                    || pt.ElevationDegrees < -90
                    || pt.ElevationDegrees > 90
                    || !IsFiniteNonNegative(pt.Multiplier)
                    || (unitInterval && pt.Multiplier > 1)
                    || pt.ElevationDegrees <= precedingElevation)
                {
                    return Invalid(
                        $"Pack '{descriptor.Id}' has an invalid {label} response curve.");
                }
                precedingElevation = pt.ElevationDegrees;
            }
            return RenderPackAuditResult.Valid();
        }

        RenderPackAuditResult VetDirectedShadeHorizon(
            IReadOnlyList<SunElevationKnot> pts)
        {
            bool hasPreciseHorizonPt = false;
            SunElevationKnot? leadAboveHorizon = null;
            foreach (SunElevationKnot pt in pts)
            {
                if (pt.ElevationDegrees <= 0d)
                {
                    if (pt.Multiplier != 0d)
                        return InvalidHorizon();
                    hasPreciseHorizonPt |= pt.ElevationDegrees == 0d;
                    continue;
                }

                leadAboveHorizon = pt;
                break;
            }

            return !hasPreciseHorizonPt
                && leadAboveHorizon is { Multiplier: not 0d }
                    ? InvalidHorizon()
                    : RenderPackAuditResult.Valid();

            RenderPackAuditResult InvalidHorizon() => Invalid(
                $"Pack '{descriptor.Id}' directional-shadow light-elevation "
                + "curve must resolve to zero at and below the 0-degree "
                + "authored horizon.");
        }
    }
}

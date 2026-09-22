using MacAC.Client.Graphics.Gpu.Vulkan;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal sealed partial class RenderPackDriver
{
    internal bool TryRestartPerformanceEvidence(out string problem)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (Capture.State != RenderPackActivationPhase.Active
            || ActiveRuntime is not IRenderPackEnginePerformanceSource src)
        {
            problem = "an active render pack with performance diagnostics is required";
            return false;
        }
        if (_autoFidelity is not null)
        {
            problem = "performance evidence reset requires an explicit quality preset";
            return false;
        }

        var metrics =
            src.GrabPerformanceMetrics();
        Validate(in metrics);
        _performance.Reset();
        _observedPerformanceCore = ActiveRuntime;
        _observedAssetGen = metrics.ResourceGeneration;
        problem = string.Empty;
        return true;
    }

    private static bool TryLocateAutomaticSetting(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSubstitutions,
        out bool automatic)
    {
        var setting = descriptor.Settings.FirstOrDefault(val =>
            val.Semantic == RenderSettingRole.AutomaticQuality);
        if (setting is null)
        {
            automatic = false;
            return false;
        }

        automatic = bool.Parse(RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSubstitutions));
        return true;
    }

    private static bool TryLocateAutomaticSpan(
        RenderPackRegistryEntry listing,
        out QualityLadderStep preset,
        out AtmosphericFidelityTier starting,
        out AtmosphericFidelityTier ceiling,
        out string? miss)
    {
        preset = null!;
        starting = AtmosphericFidelityTier.Low;
        ceiling = AtmosphericFidelityTier.Low;
        miss = null;

        var lo = SeekNetPreset(
            listing,
            AtmosphericFidelityTier.Low);
        if (lo is null)
        {
            miss = "Automatic quality requires an AutoEligible Low semantic preset as its safe fallback.";
            return false;
        }
        if (listing.PresetIncompatibilityReasons.TryGetValue(
                lo.Id,
                out string? loMiss)
            && loMiss is not null)
        {
            miss = "Automatic quality cannot support Low on this host: " + loMiss;
            return false;
        }

        preset = lo;
        var medium = SeekNetPreset(
            listing,
            AtmosphericFidelityTier.Medium);
        if (medium is null
            || (listing.PresetIncompatibilityReasons.TryGetValue(
                    medium.Id,
                    out string? mediumMiss)
                && mediumMiss is not null))

            return true;

        preset = medium;
        starting = AtmosphericFidelityTier.Medium;
        ceiling = AtmosphericFidelityTier.Medium;
        var hi = SeekNetPreset(
            listing,
            AtmosphericFidelityTier.High);
        if (hi is not null
            && (!listing.PresetIncompatibilityReasons.TryGetValue(
                    hi.Id,
                    out string? hiMiss)
                || hiMiss is null))

            ceiling = AtmosphericFidelityTier.High;
        return true;
    }

    private static bool TryFidelityTier(
        RenderQualityRole semantic,
        out AtmosphericFidelityTier fidelity)
    {
        fidelity = semantic switch
        {
            RenderQualityRole.Low => AtmosphericFidelityTier.Low,
            RenderQualityRole.Medium => AtmosphericFidelityTier.Medium,
            RenderQualityRole.High => AtmosphericFidelityTier.High,
            _ => default,
        };
        return semantic is RenderQualityRole.Low
            or RenderQualityRole.Medium
            or RenderQualityRole.High;
    }

    private static string? TryTeardown(IRenderPackEngine? core)
    {
        if (core is null)
            return null;
        try
        {
            core.Dispose();
            return null;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            return problem.GetBaseException().Message;
        }
    }

    private static string? TryTeardownRecipientContender(
        IRasterizeBundleRecipientPipeContender? contender)
    {
        if (contender is null)
            return null;
        try
        {
            contender.Dispose();
            return null;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            return problem.GetBaseException().Message;
        }
    }

    private string? TryWipeRecipientPipes()
    {
        if (_recipientPipes is null)
            return null;
        try
        {
            _recipientPipes.Clear();
            return null;
        }
        catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
        {
            return problem.GetBaseException().Message;
        }
    }
}

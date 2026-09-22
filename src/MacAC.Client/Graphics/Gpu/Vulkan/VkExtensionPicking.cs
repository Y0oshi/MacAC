namespace MacAC.Client.Graphics.Gpu.Vulkan;

// The extensions actually asked for, plus the optional ones that were not available
internal sealed record VkExtensionPlan(
    IReadOnlyList<string> Enabled,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> UnavailableOptional)
{
    internal bool IsSatisfied => MissingRequired.Count is 0;
}

internal static class VkExtensionPicking
{
    // Object naming for RenderDoc and validation output
    internal const string DiagUtilsExtension = "VK_EXT_debug_utils";

    // Real allocator headroom for GpuMemoryLedger
    internal const string MemoryAllowanceExtension = "VK_EXT_memory_budget";

    internal const string PresentPauseExtension = "VK_KHR_present_wait";

    // Presenting to a surface
    internal const string SwapchainExtension = "VK_KHR_swapchain";

    // Lets the loader report non-conformant implementations
    internal const string PortabilityEnumerationExtension =
        "VK_KHR_portability_enumeration";

    // The spec requires this be enabled whenever the physical device advertises it, which MoltenVK
    // always does
    internal const string PortabilitySubsetExtension =
        "VK_KHR_portability_subset";

    internal static VkExtensionPlan Resolve(
        IReadOnlyList<string> onHand,
        IReadOnlyList<string> needed,
        IReadOnlyList<string> optional)
    {
        ArgumentNullException.ThrowIfNull(onHand);
        ArgumentNullException.ThrowIfNull(needed);
        ArgumentNullException.ThrowIfNull(optional);

        HashSet<string> advertised = new HashSet<string>(onHand, StringComparer.Ordinal);
        List<string> turnedOn = new List<string>();
        List<string> absent = new List<string>();
        List<string> unavailable = new List<string>();

        foreach (string label in needed)
        {
            if (advertised.Contains(label))
            {
                if (!turnedOn.Contains(label, StringComparer.Ordinal))
                    turnedOn.Add(label);
            }
            else if (!absent.Contains(label, StringComparer.Ordinal))
            {
                absent.Add(label);
            }
        }

        foreach (string label in optional)
        {
            if (advertised.Contains(label))
            {
                if (!turnedOn.Contains(label, StringComparer.Ordinal))
                    turnedOn.Add(label);
            }
            else if (!unavailable.Contains(label, StringComparer.Ordinal))
            {
                unavailable.Add(label);
            }
        }

        return new VkExtensionPlan(turnedOn, absent, unavailable);
    }

    internal static IReadOnlyList<string> OptionalInstExtensions { get; } =
        [DiagUtilsExtension];

    internal static IReadOnlyList<string> OptionalDevExtensions { get; } =
        [MemoryAllowanceExtension, PresentPauseExtension, PortabilitySubsetExtension];

    internal static IReadOnlyList<string> LocateOptionalInstExtensions(
        bool activateOptionalExtensions)
    {
        return activateOptionalExtensions
            ? [.. OptionalInstExtensions, PortabilityEnumerationExtension]
            : [PortabilityEnumerationExtension];
    }
}

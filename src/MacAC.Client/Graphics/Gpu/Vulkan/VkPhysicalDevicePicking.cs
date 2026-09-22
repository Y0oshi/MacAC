using System.Globalization;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed record VkPhysicalDeviceChoice(
    VkPhysicalDeviceCandidate Device,
    string Reason);

internal static class VkPhysicalDevicePicking
{
    internal static int PreferenceGrade(PhysicalDeviceType kind)
    {
        return kind switch
        {
            PhysicalDeviceType.DiscreteGpu => 0,
            PhysicalDeviceType.IntegratedGpu => 1,
            PhysicalDeviceType.VirtualGpu => 2,
            PhysicalDeviceType.Cpu => 3,
            _ => 4,
        };
    }

    internal static VkPhysicalDeviceChoice? Choose(
        IReadOnlyList<VkPhysicalDeviceCandidate> contenders,
        string? devOverride)
    {
        ArgumentNullException.ThrowIfNull(contenders);
        if (contenders.Count is 0)
            return null;

        if (!string.IsNullOrWhiteSpace(devOverride))
        {
            string trimmed = devOverride.Trim();
            if (IsDecimalOrdinal(trimmed))
            {
                int ordinal = int.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
                var byOrdinal =
                    contenders.FirstOrDefault(contender => contender.Index == ordinal);
                if (byOrdinal is not null)
                {
                    return new VkPhysicalDeviceChoice(
                        byOrdinal,
                        $"MACAC_VULKAN_DEVICE={trimmed} selected device index {ordinal}.");
                }
            }
            else
            {
                var byLabel = contenders.FirstOrDefault(
                    contender => contender.DeviceName.Contains(
                        trimmed,
                        StringComparison.OrdinalIgnoreCase));
                if (byLabel is not null)
                {
                    return new VkPhysicalDeviceChoice(
                        byLabel,
                        $"MACAC_VULKAN_DEVICE={trimmed} matched device name '{byLabel.DeviceName}'.");
                }
            }

            var automatic = Rank(contenders);
            return new VkPhysicalDeviceChoice(
                automatic,
                $"MACAC_VULKAN_DEVICE={trimmed} matched no enumerated device; " +
                $"fell back to the automatic choice '{automatic.DeviceName}' " +
                $"({automatic.DeviceType}, {Gib(automatic.DeviceLocalHeapBytes)} device-local).");
        }

        var chosen = Rank(contenders);
        return new VkPhysicalDeviceChoice(
            chosen,
            $"automatic: '{chosen.DeviceName}' ({chosen.DeviceType}, " +
            $"{Gib(chosen.DeviceLocalHeapBytes)} device-local) ranked first of " +
            $"{contenders.Count} enumerated device(s).");
    }

    internal static VkPhysicalDeviceCandidate Rank(
        IReadOnlyList<VkPhysicalDeviceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count is 0)
            throw new ArgumentException("At least one candidate is needed", nameof(candidates));

        VkPhysicalDeviceCandidate finest = candidates[0];
        for (int idx = 1; idx < candidates.Count; ++idx)
        {
            if (Contrast(candidates[idx], finest) < 0)
                finest = candidates[idx];
        }

        return finest;
    }

    // Negative when left is the better device
    internal static int Contrast(
        VkPhysicalDeviceCandidate left,
        VkPhysicalDeviceCandidate right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int byKind = PreferenceGrade(left.DeviceType).CompareTo(PreferenceGrade(right.DeviceType));
        if (byKind is not 0)
            return byKind;

        int byHeap = right.DeviceLocalHeapBytes.CompareTo(left.DeviceLocalHeapBytes);
        return byHeap is not 0 ? byHeap : left.Index.CompareTo(right.Index);
    }

    // An index override is decimal digits and nothing else
    internal static bool IsDecimalOrdinal(string val)
    {
        if (string.IsNullOrEmpty(val))
            return false;
        foreach (char toon in val)
        {
            if (toon is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static string Gib(ulong octets)
    {
        return (octets / (1024d * 1024d * 1024d)).ToString("0.##", CultureInfo.InvariantCulture) + " GiB";
    }
}

internal sealed record VkQueueFamilyChoice(
    uint GraphicsFamily,
    uint PresentFamily)
{
    internal bool IsUnified => GraphicsFamily == PresentFamily;
}

// One enumerated queue family, reduced to the two facts the selector needs
internal readonly record struct VkQueueFamilyCandidate(
    uint Index,
    bool SupportsGraphics,
    bool SupportsPresent);

internal static class VkQueueFamilyPicking
{
    internal static VkQueueFamilyChoice? Choose(
        IReadOnlyList<VkQueueFamilyCandidate> clans)
    {
        ArgumentNullException.ThrowIfNull(clans);

        foreach (VkQueueFamilyCandidate clan in clans)
        {
            if (clan.SupportsGraphics && clan.SupportsPresent)
                return new VkQueueFamilyChoice(clan.Index, clan.Index);
        }

        uint? visuals = null;
        uint? present = null;
        foreach (VkQueueFamilyCandidate clan in clans)
        {
            if (visuals is null && clan.SupportsGraphics)
                visuals = clan.Index;
            if (present is null && clan.SupportsPresent)
                present = clan.Index;
        }

        return visuals is { } g && present is { } p
            ? new VkQueueFamilyChoice(g, p)
            : null;
    }

    internal static uint? SelectVisualsSole(
        IReadOnlyList<VkQueueFamilyCandidate> clans)
    {
        ArgumentNullException.ThrowIfNull(clans);
        foreach (VkQueueFamilyCandidate clan in clans)
        {
            if (clan.SupportsGraphics)
                return clan.Index;
        }

        return null;
    }
}

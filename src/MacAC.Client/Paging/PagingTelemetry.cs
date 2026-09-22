using System.Globalization;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

internal static class PagingTelemetry
{
    internal const int DefaultTunnelFreezeCycle = 72;

    public static int? UnveilRadiusOverride { get; } =
        DecodeRadius(
            Environment.GetEnvironmentVariable("MACAC_PROBE_REVEAL_RADIUS"));

    public static PagingRevealWindow ApplyRevealRadiusOverride(
        PagingRevealWindow pane) =>
        ApplyRevealRadiusOverride(pane, UnveilRadiusOverride);

    public static PagingRevealWindow ApplyRevealRadiusOverride(
        PagingRevealWindow pane,
        int? overrideRadius)
    {
        if (overrideRadius is not { } radius)
            return pane;

        int faraway = Math.Max(0, radius);
        return new PagingRevealWindow(
            Math.Clamp(pane.NearRadius, 0, faraway),
            faraway);
    }

    public static bool SensorUnveilTiming { get; } =
        Environment.GetEnvironmentVariable("MACAC_PROBE_REVEAL_TIMING") == "1";

    public static int? TunnelFreezeCycle { get; } = DecodeTunnelFreezeCycle(
        Environment.GetEnvironmentVariable("MACAC_PROBE_TUNNEL_FREEZE"));

    internal static int? DecodeRadius(string? raw)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)
        && val >= 1
            ? val
            : null;
    }

    internal static int? DecodeTunnelFreezeCycle(string? raw)
    {
        return string.Equals(raw, "1", StringComparison.Ordinal)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            ? DefaultTunnelFreezeCycle
            : int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int cycle)
            && cycle >= 2
            && cycle <= WarpAnimScheduler.TunnelFinishCycle
                ? cycle
                : null;
    }
}

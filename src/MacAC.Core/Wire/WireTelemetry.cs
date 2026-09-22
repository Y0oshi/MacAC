using System.Globalization;

namespace MacAC.Wire;

/// <summary>Which way the loss shim (<c>MACAC_NET_DROP_DIR</c>) eats datagrams.</summary>
public enum DropSide
{
    Out,
    In,
    Both,
}

public static class WireTelemetry
{

    internal static int DecodeDiscardPct(string? val)
    {
        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct) && pct is >= 0 and <= 100 ? pct : 0;
    }

    internal static DropSide DecodeDiscardDir(string? val)
    {
        return val?.ToLowerInvariant() switch
        {
            "out" => DropSide.Out,
            "in" => DropSide.In,
            _ => DropSide.Both,
        };
    }

    public static bool SensorNet { get; set; } = Mark("MACAC_PROBE_NET");

    public static bool SensorUnveil { get; set; } = Mark("MACAC_PROBE_REVEAL");

    /// <summary><c>MACAC_NET_DROP_PCT</c>: 0 (off) to 100.</summary>
    public static int NetDiscardPct { get; set; } = DecodeDiscardPct(Env("MACAC_NET_DROP_PCT"));

    public static int NetDiscardSeed { get; set; } =
        int.TryParse(Env("MACAC_NET_DROP_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed) ? seed : 1;

    /// <summary><c>MACAC_NET_DROP_DIR</c>: <c>out</c>, <c>in</c> or <c>both</c> (default).</summary>
    public static DropSide NetDiscardDirection { get; set; } = DecodeDiscardDir(Env("MACAC_NET_DROP_DIR"));
    private static string? Env(string label) => Environment.GetEnvironmentVariable(label);

    private static bool Mark(string label) => Env(label) == "1";
}

using System.Text.Json;

namespace MacAC.Forge;

/// <summary>Machine-readable progress channel (the launcher reads it as JSON lines).</summary>
public interface IForgeProgressTap
{
    void Begun(uint bakeToolVer, string productTrail);

    void Progress(
        string stage,
        long finished,
        long sum,
        int misses,
        double passedSecs,
        double etaSecs,
        long privateOctets,
        long managedOctets);

    void Completed(uint bakeToolVer, long productOctets, int misses);

    void Error(string msg);
}

public sealed class ForgeProgressJsonScribe(TextWriter output) : IForgeProgressTap
{
    public const int LatestVer = 1;

    private readonly TextWriter _product = output ?? throw new ArgumentNullException(nameof(output));
    private readonly Lock _latch = new();

    public void Begun(uint bakeToolVer, string productTrail)
    {
        Emit(new { v = LatestVer, e = "started", t = Instant, bakeToolVersion = bakeToolVer, outputPath = productTrail });
    }

    public void Progress(
        string stage,
        long finished,
        long sum,
        int misses,
        double passedSecs,
        double etaSecs,
        long privateOctets,
        long managedOctets)
    {
        Emit(new
        {
            v = LatestVer,
            e = "progress",
            t = Instant,
            phase = stage,
            completed = finished,
            total = sum,
            failures = misses,
            elapsedSeconds = passedSecs,
            etaSeconds = etaSecs,
            privateBytes = privateOctets,
            managedBytes = managedOctets,
        });
    }

    public void Completed(uint bakeToolVer, long productOctets, int misses)
    {
        Emit(new { v = LatestVer, e = "completed", t = Instant, bakeToolVersion = bakeToolVer, outputBytes = productOctets, failures = misses });
    }

    public void Error(string msg)
    {
        Emit(new { v = LatestVer, e = "error", t = Instant, message = msg });
    }

    private static DateTimeOffset Instant => DateTimeOffset.UtcNow;

    private void Emit<T>(T capture)
    {
        string stroke = JsonSerializer.Serialize(capture);
        lock (_latch)
        {
            _product.WriteLine(stroke);
            _product.Flush();
        }
    }
}

// The human progress line, plus a mirrored record on the tap when one is attached
internal static class ForgeProgressLine
{
    public static void Write(
        TextWriter humanProduct,
        IForgeProgressTap? machineProduct,
        string stage,
        long finished,
        int sum,
        int misses,
        TimeSpan passed,
        double etaSecs,
        long privateOctets,
        long managedOctets)
    {
        ArgumentNullException.ThrowIfNull(humanProduct);
        humanProduct.WriteLine(
            $"[{passed:hh\\:mm\\:ss}] extracted {finished:N0}/{sum:N0}, "
            + $"failures={misses:N0}, elapsed={passed.TotalSeconds:F0}s, "
            + $"ETA={etaSecs:F0}s, "
            + $"private={Megabytes(privateOctets):F0}MB, "
            + $"managed={Megabytes(managedOctets):F0}MB");
        machineProduct?.Progress(
            stage,
            finished,
            sum,
            misses,
            passed.TotalSeconds,
            etaSecs,
            privateOctets,
            managedOctets);
    }

    private static double Megabytes(long octets) => octets / 1024.0 / 1024.0;
}

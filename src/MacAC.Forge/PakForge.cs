using System.Diagnostics;
using MacAC.Assets.Pak;

namespace MacAC.Forge;

public static class PakForge
{
    public static int Run(ForgeJob job)
    {
        ExecuteDetailed(job);
        return 0;
    }

    public static ForgeTally ExecuteDetailed(ForgeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Threads <= 0)
            throw new ArgumentOutOfRangeException(nameof(job), "thread count has to be positive");

        job.AbortTicket.ThrowIfCancellationRequested();
        job.Progress?.Begun(PakFmt.LatestBakeToolVer, job.OutTrail);
        Stopwatch timer = Stopwatch.StartNew();
        ForgeTally count = PakCommit.Run(
            job.OutTrail,
            lined => new ForgeRun(job with { OutTrail = lined }, job.OutTrail).Execute(),
            (lined, outcome) => PakProof.Check(lined, outcome.Header, outcome.SumTags, outcome.KindCounts),
            job.AbortTicket);
        timer.Stop();

        using Process self = Process.GetCurrentProcess();
        count = count with
        {
            Passed = timer.Elapsed,
            ProductOctets = new FileInfo(job.OutTrail).Length,
            PeakWorkingSetOctets = self.PeakWorkingSet64,
            PeakPrivateOctets = self.PeakPagedMemorySize64,
        };

        ForgeSummary.Dump(count, job.OutTrail);
        job.Progress?.Completed(count.Header.BakeToolVer, count.ProductOctets, count.Failures);
        return count;
    }
}

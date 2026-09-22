using MacAC.Sim;

namespace MacAC.Client.Rigging;

internal sealed record SessionBeginDeps(
    Action<string> Log);

internal sealed class SessionStartAssemblyPhase(SessionBeginDeps dependencies)
        : ISessionStartAssemblyPhase<CycleTrunkOutcome>
{
    private readonly SessionBeginDeps _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));

    public void Start(CycleTrunkOutcome cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        var outcome =
            cycle.SimCore.Session.Start(cycle.SimCore.Generation);
        Report(outcome, _deps.Log);
    }

    internal static void Report(
        SimSessionStartResult outcome,
        Action<string> trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        switch (outcome.Status)
        {
            case SimSessionStartStatus.MissingCredentials:
                trace(
                    "live: MACAC_LIVE set but TEST_USER/TEST_PASS missing; skipping");
                break;
            case SimSessionStartStatus.Failed:
                trace($"live: session failed: {outcome.Error}");
                break;
        }
    }
}

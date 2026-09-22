using System.Globalization;

namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger
{
    private const string Tag = "[world-reveal] event=";

    private void FailInvariant(string cause, string? specifics)
    {
        _capture = _capture with
        {
            InvariantFailureCount = checked(_capture.InvariantFailureCount + 1),
        };
        SafeTrace($"{Tag}invariant-failure reason={cause}{Suffix(specifics)} {Depict(_capture)}");
    }

    private void TraceRejected(string cause, string? specifics)
    {
        SafeTrace($"{Tag}rejected reason={cause}{Suffix(specifics)} {Depict(_capture)}");
    }

    private void Trace(string signalLabel, SimPortalCapture capture) =>
        SafeTrace($"{Tag}{signalLabel} {Depict(capture)}");

    private static string Suffix(string? specifics) =>
        string.IsNullOrWhiteSpace(specifics) ? string.Empty : $" {specifics}";

    private void SafeTrace(string msg)
    {
        try
        {
            _trace(msg);
        }
        catch (Exception problem)
        {
            ProbeMissTally = checked(ProbeMissTally + 1);
            PreviousProbeMiss = problem;
        }
    }

    private static string Depict(SimPortalCapture capture)
    {
        var primed = capture.Readiness;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"generation={capture.Generation} kind={capture.Kind} "
            + $"cell=0x{primed.DestinationCell:X8} "
            + $"indoor={primed.IsIndoor} "
            + $"radius={primed.RequiredRenderRadius} "
            + $"unhydratable={primed.IsUnhydratable} "
            + $"render={primed.IsRenderNeighborhoodReady} "
            + $"composites={primed.AreCompositeTexturesReady} "
            + $"collision={primed.IsCollisionReady} ready={primed.IsReady} "
            + $"materialized={capture.Materialized} "
            + $"completed={capture.Completed} "
            + $"cancelled={capture.Cancelled} "
            + $"visible={capture.WorldViewportObserved} "
            + $"simulation={capture.WorldSimulationAvailable} "
            + $"failures={capture.InvariantFailureCount}");
    }
}

using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

internal enum ContactWalkMode
{
    Graph,
    Flat,
}

// The seam between the graph (dat-object) and flat (packed) collision paths
internal static partial class ContactSweep
{
    // One dual-path query: its two implementations and how to report on them
    private interface IDualQuery<TResult>
    {
        string Kind { get; }
        uint SourceId { get; }

        // The graph form
        TResult Graph(Changeover? changeover);

        // The flat form; throws AbsentPlanar when the packed asset is absent
        TResult Flat(Changeover? changeover);

        // Rendered inputs for the artifact; only built when a sample is taken
        string Input();

        void Record(ProxyShadowVerifier referee, long specimen, TResult graph, TResult planar, Changeover? graphChangeover, Changeover? planarChangeover, string feed);
    }

    private static bool EmployPlanar(KineticAssetCache stash)
    {
        var shade = stash.ImpactShade;
        if (shade?.IsGraphPass == true)
            return false;
        return shade?.IsPlanarPass == true ? true : stash.LinkStrollManner == ContactWalkMode.Flat;
    }

    private static InvalidOperationException AbsentPlanar(string sort)
    {
        return new($"Flat collision traversal requires a prepared {sort} asset. Gameplay must not fall back silently.");
    }

    private static TResult Rule<TQuery, TResult>(KineticAssetCache stash, in TQuery ask, Changeover? changeover)
        where TQuery : struct, IDualQuery<TResult>
    {
        var referee = stash.ImpactShade;
        long specimen = 0;
        bool sampled = referee is not null && referee.TrySpecimen(out specimen);
        Changeover? shade = sampled && changeover is not null ? referee!.ReadyShade(changeover) : null;

        if (EmployPlanar(stash))
        {
            // Flat is authority; graph referees afterwards on the untouched copy
            TResult planar = ask.Flat(changeover);
            if (!sampled)
                return planar;

            referee!.CommenceGraphPass();
            TResult graph = default!;
            Exception? flaw = null;
            try
            {
                graph = ask.Graph(shade ?? changeover);
            }
            catch (Exception problem)
            {
                flaw = problem;
            }
            finally
            {
                referee.FinishGraphPass();
            }

            string feed = ask.Input();
            if (flaw is null)
                ask.Record(referee, specimen, graph, planar, shade, changeover, feed);
            else
                referee.CaptureFlaw(specimen, ask.Kind, ask.SourceId, flaw, feed, "flat");
            return planar;
        }
        else
        {
            if (!sampled)
                return ask.Graph(changeover);

            // Flat referees first on the copy, then graph runs as authority
            referee!.CommencePlanarPass();
            TResult planar = default!;
            Exception? flaw = null;
            try
            {
                planar = ask.Flat(shade ?? changeover);
            }
            catch (Exception problem)
            {
                flaw = problem;
            }
            finally
            {
                referee.FinishPlanarPass();
            }

            TResult graph = ask.Graph(changeover);
            string feed = ask.Input();
            if (flaw is null)
                ask.Record(referee, specimen, graph, planar, changeover, shade, feed);
            else
                referee.CaptureFlaw(specimen, ask.Kind, ask.SourceId, flaw, feed);
            return graph;
        }
    }

    // Boolean queries all report the same way
    private static void CaptureBool<TQuery>(in TQuery ask, ProxyShadowVerifier referee, long specimen, bool graph, bool planar, string feed)
        where TQuery : struct, IDualQuery<bool>
    {
        referee.CaptureBoolean(specimen, ask.Kind, ask.SourceId, graph, planar, feed);
    }
}

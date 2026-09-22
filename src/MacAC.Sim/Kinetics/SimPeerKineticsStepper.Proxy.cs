using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// Keeping the collision proxy in step with the body pose
internal sealed partial class SimPeerKineticsStepper
{
    internal void SynchronizeDistantShadeToCorpus(
        uint actorIdent,
        MacAC.Sim.Kinetics.ISimPeerPlacement placement,
        int onlineMiddleX,
        int onlineMiddleY,
        uint? authoritativeChamberIdent = null)
    {
        SynchronizeDistantShadeToCorpus(
            actorIdent,
            placement.Body,
            onlineMiddleX,
            onlineMiddleY,
            authoritativeChamberIdent ?? placement.CellId);
        placement.LastShadowSyncPosition = placement.Body.Position;
        placement.LastShadowSyncOrientation = placement.Body.Orientation;
    }

    internal void SynchronizeDistantShadeToCorpus(
        uint actorIdent,
        KineticBody corpus,
        int onlineMiddleX,
        int onlineMiddleY,
        uint authoritativeChamberIdent)
    {
        ProxyPositionSynchronizer.Sync(
            _register.Engine.ShadeObjects,
            actorIdent,
            corpus.Position,
            corpus.Orientation,
            authoritativeChamberIdent,
            onlineMiddleX,
            onlineMiddleY);
    }

    internal static bool ShouldSynchronizeShadePosture(
        Vector3 latestLocus,
        Quaternion latestFacing,
        Vector3 previousLocus,
        Quaternion previousFacing)
    {
        if (Vector3.DistanceSquared(
                latestLocus,
                previousLocus) > 1e-4f)

            return true;

        float latestLenSquared = latestFacing.LengthSquared();
        float previousLenSquared = previousFacing.LengthSquared();
        if (!float.IsFinite(latestLenSquared)
            || !float.IsFinite(previousLenSquared)
            || latestLenSquared < 1e-12f
            || previousLenSquared < 1e-12f)

            return true;

        float normalizedDot = MathF.Abs(
            Quaternion.Dot(
                latestFacing,
                previousFacing)
            / MathF.Sqrt(latestLenSquared * previousLenSquared));
        return !float.IsFinite(normalizedDot) || normalizedDot < 0.99999f;
    }

    internal static bool ShouldSynchronizeShade(
        bool chamberAltered,
        Vector3 latestLocus,
        Quaternion latestFacing,
        Vector3 previousLocus,
        Quaternion previousFacing)
    {
        return chamberAltered
        || ShouldSynchronizeShadePosture(
            latestLocus,
            latestFacing,
            previousLocus,
            previousFacing);
    }
}

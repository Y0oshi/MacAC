using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Kinetics;

internal static class SimPeerSettledStatePosition
{
    private const float CorpusSnapThreshold = 4f;
    private const uint InsideChamberFloor = 0x100u;

    internal const float ProbeCorpusSnapThreshold = CorpusSnapThreshold;

    internal enum Act : byte
    {
        Snapped,

        Enqueued,
    }

    internal static bool OwnsSteadyPhase(SimSovereignPositionRoute? course) => IsAirborneNoOp(course) || IsNearbyLerp(course);

    internal static bool IsAirborneNoOp(SimSovereignPositionRoute? course)
    {
        return course is { Disposition: SimSovereignPositionVerdict.NoPositionOperation };
    }

    internal static bool IsNearbyLerp(SimSovereignPositionRoute? course) =>
        course is { Disposition: SimSovereignPositionVerdict.Interpolate };

    internal static bool WouldSnap(PeerMotion distant, Vector3 realmLocus, bool willBeDrTicked)
    {
        ArgumentNullException.ThrowIfNull(distant);
        bool lead = distant.PreviousSrvSpotMoment <= 0.0;
        return lead || !willBeDrTicked || Vector3.Distance(distant.Body.Position, realmLocus) > CorpusSnapThreshold;
    }

    internal static Act ImposeLerp(PeerMotion distant, Vector3 realmLocus, Quaternion facing, bool isMovingTo, bool willBeDrTicked, uint markChamberIdent = 0u)
    {
        ArgumentNullException.ThrowIfNull(distant);

        bool lead = distant.PreviousSrvSpotMoment <= 0.0;
        float gap = Vector3.Distance(distant.Body.Position, realmLocus);
        bool telemetry = KineticTelemetry.ShouldTraceDistantSlide(KineticTelemetry.DistantSlideAttributionOid);

        if (WouldSnap(distant, realmLocus, willBeDrTicked))
        {
            if (telemetry)
            {
                (int zDepth, int misses) = distant.Lerp.ProbeInterpolationPhase;
                KineticTelemetry.TraceDistantSlideCorpusSnap(
                    oid: KineticTelemetry.DistantSlideAttributionOid,
                    leadUp: lead,
                    willBeDrTicked: willBeDrTicked,
                    corpusToMark: gap,
                    threshold: CorpusSnapThreshold,
                    corpusLocus: distant.Body.Position,
                    markLocus: realmLocus,
                    lerpFifoZDepth: zDepth,
                    lerpFailTally: misses);
            }
            distant.Lerp.Clear();
            distant.Body.Position = realmLocus;
            distant.Body.Orientation = facing;
            return Act.Snapped;
        }

        // Indoor cells use a tighter reach than the landscape
        float reach = (distant.CellId & 0xFFFFu) >= InsideChamberFloor ? 20f : 100f;
        Quaternion? immediate = distant.Lerp.Enqueue(realmLocus, facing, isMovingTo, distant.Body.Position, distant.Body.Orientation, markChamberIdent, reach);
        if (immediate is { } pivot)
            distant.Body.Orientation = pivot;
        if (telemetry)
        {
            (int zDepth, int misses) = distant.Lerp.ProbeInterpolationPhase;
            KineticTelemetry.TraceDistantSlideQueue(
                oid: KineticTelemetry.DistantSlideAttributionOid,
                corpusToMark: gap,
                markLocus: realmLocus,
                lerpFifoZDepth: zDepth,
                lerpFailTally: misses);
        }
        return Act.Enqueued;
    }

    // Every arm except airborne no-op re-tethers the body to where it landed
    internal static bool TryArmConstraintFollowingOp(SimPeerGrantedPositionArm arm, PeerMotion distant)
    {
        ArgumentNullException.ThrowIfNull(distant);
        bool arms = arm is SimPeerGrantedPositionArm.TeleportPlacement
            or SimPeerGrantedPositionArm.FarSnapPlacement
            or SimPeerGrantedPositionArm.NearInterpolate
            or SimPeerGrantedPositionArm.UnroutedCatchUp;
        if (!arms || distant.Host is not { } hub)
            return false;
        ArmConstraintFollowingOp(hub);
        return true;
    }

    internal static void ArmConstraintFollowingOp(ActorKineticsHarbor hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        Locus mooring = hub.Position;
        hub.LocusKeeper.ConstrainTo(mooring, TetherDistance.FetchBeginConstraintGap(mooring.ObjCellId), TetherDistance.FetchUpperConstraintGap(mooring.ObjCellId));
    }
}

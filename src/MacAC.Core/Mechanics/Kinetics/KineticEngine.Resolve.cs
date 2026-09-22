using System.Collections.Immutable;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticEngine
{
    private const TransientPhaseFlagSet StationaryBitset =
        TransientPhaseFlagSet.StationaryFall | TransientPhaseFlagSet.StationaryStop | TransientPhaseFlagSet.StationaryStuck;

    public ResolveVerdict ResolveWithTransition(
        Vector3 latestSpot, Vector3 markSpot, uint chamberIdent,
        float orbRadius, float orbHeight,
        float hopUpHeight, float hopDownHeight,
        bool isOnTerrain,
        KineticBody? corpus = null,
        MoverState carrierFlagSet = MoverState.None,
        uint movingActorIdent = 0,
        Vector3? ownOrbOrigin = null,
        Quaternion? commenceFacing = null,
        Quaternion? finishFacing = null,
        uint designatedMarkIdent = 0,
        ImmutableArray<PackedContactSphere> orbRoster = default,
        float orbScaling = 1f)
    {
        bool grab = KineticResolveCapture.IsTurnedOn && (carrierFlagSet & MoverState.IsPlayer) != 0;
        KineticBodyFrame? corpusPrior = grab && corpus is not null ? KineticResolveCapture.Snapshot(corpus) : null;

        KineticTelemetry.CommencePassageFailTrace();

        Changeover changeover = RentChangeover();
        try
        {
            changeover.MoverFacts.StepUpHeight = hopUpHeight;
            changeover.MoverFacts.StepDownHeight = hopDownHeight;
            PrimeFromCorpus(changeover, corpus, isOnTerrain, carrierFlagSet, movingActorIdent, designatedMarkIdent);

            SweepPath trail = changeover.SweepPath;
            if (!orbRoster.IsDefaultOrEmpty)
                trail.InitPath(latestSpot, markSpot, chamberIdent, orbRoster, orbScaling, commenceFacing, finishFacing);
            else
                trail.InitPath(latestSpot, markSpot, chamberIdent, orbRadius, orbHeight, ownOrbOrigin, commenceFacing, finishFacing);

            // Outdoors within the body's own landblock the sweep can carry the
            // block origin instead of re-deriving it from the cell.
            bool sameExteriorChunk = corpus is not null
                && IsLandChamber(chamberIdent)
                && IsLandChamber(corpus.CellPosition.ObjCellId)
                && (chamberIdent >> 16) == (corpus.CellPosition.ObjCellId >> 16);
            trail.CarriedBlockOrigin = sameExteriorChunk ? corpus!.Position - corpus.CellPosition.Frame.Origin : null;

            if (isOnTerrain && corpus is not null && corpus.WalkablePolygonValid && corpus.WalkableVertices is { Length: >= 3 })
                trail.ApplyPassable(corpus.WalkablePlane, corpus.WalkableVertices, corpus.WalkableUp);

            if (corpus is not null)
                changeover.ContactLedger.FramesStationaryFall = CyclesStationaryFallOf(corpus);

            bool ok = changeover.SeekTransitionalLocus(this);

            var touch = changeover.ContactLedger;
            if (corpus is not null)
            {
                if (ok)
                    AbsorbIntoCorpus(corpus, trail, touch, isOnTerrain);

                if (changeover.MoverFacts.VelocityKilled)
                {
                    if (KineticTelemetry.DumpSteepRoofEnabled)
                        Console.WriteLine($"[steep-roof] KILL-VELOCITY-APPLIED Vbefore=({corpus.Velocity.X:F2},{corpus.Velocity.Y:F2},{corpus.Velocity.Z:F2}) → 0,0,0");
                    corpus.Velocity = Vector3.Zero;
                }
            }

            bool impactNormValid = touch.CollisionNormalValid;
            Vector3 impactNorm = touch.CollisionNormal;

            if (KineticTelemetry.ProbeResolveEnabled)
                InspectLocate(movingActorIdent, latestSpot, markSpot, chamberIdent, isOnTerrain, ok, trail, touch, impactNormValid, impactNorm);
            if (KineticTelemetry.ProbeSweptEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[cell-swept] ent=0x{movingActorIdent:X8} ok={ok} inCell=0x{chamberIdent:X8} curCell=0x{trail.CurCellId:X8} checkCell=0x{trail.CheckCellId:X8} curPos=({trail.CurPos.X:F3},{trail.CurPos.Y:F3},{trail.CurPos.Z:F3}) checkPos=({trail.CheckPos.X:F3},{trail.CheckPos.Y:F3},{trail.CheckPos.Z:F3})"));
            }

            bool carrierOnPassable = (changeover.MoverFacts.State & MoverState.OnWalkable) != 0;
            ResolveVerdict verdict;
            if (ok)
            {
                bool inLink = touch.ContactPlaneValid;
                verdict = new ResolveVerdict(
                    trail.CheckPos,
                    trail.CurCellId,
                    inLink || carrierOnPassable,
                    impactNormValid,
                    impactNorm,
                    Orientation: trail.CurOrientation,
                    InContact: inLink,
                    OnWalkable: KineticObjUpdate.IsPassableLink(inLink, touch.ContactPlane.Normal));
            }
            else
            {
                // Render Residual A - the sweep failed (find_valid_position == 0)
                uint partialChamberIdent = trail.CheckCellId is not 0 ? trail.CheckCellId : chamberIdent;
                verdict = new ResolveVerdict(
                    trail.CheckPos,
                    trail.CurCellId is not 0 ? trail.CurCellId : partialChamberIdent,
                    touch.ContactPlaneValid || carrierOnPassable || isOnTerrain,
                    impactNormValid,
                    impactNorm,
                    Ok: false,
                    Orientation: trail.CurOrientation);
            }

            KineticTelemetry.WritePassageFailIfStuck(movingActorIdent, latestSpot, markSpot, verdict.Position);

            if (grab)
            {
                KineticResolveCapture.TraceCall(
                    new ResolveCallArgs(
                        CurrentPos: latestSpot,
                        TargetPos: markSpot,
                        CellId: chamberIdent,
                        SphereRadius: orbRadius,
                        SphereHeight: orbHeight,
                        StepUpHeight: hopUpHeight,
                        StepDownHeight: hopDownHeight,
                        IsOnGround: isOnTerrain,
                        MoverFlags: (uint)carrierFlagSet,
                        MovingEntityId: movingActorIdent),
                    corpusPrior,
                    new ResolveOutcome(
                        Position: verdict.Position,
                        CellId: verdict.CellId,
                        IsOnGround: verdict.IsOnGround,
                        CollisionNormalValid: verdict.CollisionNormalValid,
                        CollisionNormal: verdict.CollisionNormal),
                    corpus is not null ? KineticResolveCapture.Snapshot(corpus) : null);
            }

            return verdict with
            {
                PreviousCollidedObjectIdent = touch.LastCollidedObjectGuid ?? 0u,
                CollidedWithEnvironment = touch.CollidedWithEnvironment,
            };
        }
        finally
        {
            YieldChangeover(changeover);
        }
    }

    private static int CyclesStationaryFallOf(KineticBody corpus)
    {
        var flags = corpus.TransientState;
        if ((flags & TransientPhaseFlagSet.StationaryStuck) != 0) return 3;
        if ((flags & TransientPhaseFlagSet.StationaryStop) != 0) return 2;
        return (flags & TransientPhaseFlagSet.StationaryFall) != 0 ? 1 : 0;
    }

    private static TransientPhaseFlagSet StationaryBitFor(int cyclesStationaryFall)
    {
        return cyclesStationaryFall switch
        {
            1 => TransientPhaseFlagSet.StationaryFall,
            2 => TransientPhaseFlagSet.StationaryStop,
            3 => TransientPhaseFlagSet.StationaryStuck,
            _ => TransientPhaseFlagSet.None,
        };
    }

    // Seeds the transition's mover and contact records from the body's current state
    private static void PrimeFromCorpus(Changeover changeover, KineticBody? corpus, bool isOnTerrain, MoverState carrierFlagSet, uint movingActorIdent, uint designatedMarkIdent)
    {
        MoverFacts carrier = changeover.MoverFacts;
        var touch = changeover.ContactLedger;

        carrier.StepDown = true;
        carrier.SelfEntityId = movingActorIdent;
        PrimeFromCorpusRest(carrier, touch, corpus, designatedMarkIdent, carrierFlagSet, isOnTerrain);
    }

    private static void PrimeFromCorpusRest(MoverFacts carrier, ContactLedger touch, KineticBody? corpus, uint designatedMarkIdent, MoverState carrierFlagSet, bool isOnTerrain)
    {
        carrier.MoverPhysicsState = corpus?.State ?? KineticStateFlags.None;
        carrier.TargetId = designatedMarkIdent;
        PrimeFromCorpusTail(carrier, touch, corpus, carrierFlagSet, isOnTerrain);
    }

    private static void PrimeFromCorpusTail(MoverFacts carrier, ContactLedger touch, KineticBody? corpus, MoverState carrierFlagSet, bool isOnTerrain)
    {
        carrier.State |= carrierFlagSet;
        if ((carrier.MoverPhysicsState & KineticStateFlags.Missile) != 0)
            carrier.State |= MoverState.PathClipped;
        carrier.MoverHasGravity = corpus?.HasGravity ?? false;
        if (corpus is not null && corpus.InContact && corpus.ContactPlaneValid)
        {
            // Still pressed into the plane: carry it as live contact. Pulling
            // away from it: remember it as the last known plane only.
            float awayRate = Vector3.Dot(corpus.Velocity, corpus.ContactPlane.Normal);
            if (awayRate <= KineticConstants.EPSILON)
            {
                carrier.State |= MoverState.Contact;
                if (corpus.OnWalkable)
                    carrier.State |= MoverState.OnWalkable;
                touch.PrimeLinkPlane(corpus.ContactPlane, corpus.ContactPlaneCellId, corpus.ContactPlaneIsWater);
            }
            else
            {
                touch.LastKnownContactPlaneValid = true;
                touch.LastKnownContactPlane = corpus.ContactPlane;
                touch.LastKnownContactPlaneCellId = corpus.ContactPlaneCellId;
                touch.LastKnownContactPlaneIsWater = corpus.ContactPlaneIsWater;
            }
        }
        else if (corpus is null && isOnTerrain)
        {
            carrier.State |= MoverState.Contact | MoverState.OnWalkable;
        }
        if (corpus is not null
                    && (corpus.TransientState & TransientPhaseFlagSet.Sliding) != 0
                    && corpus.SlidingNormal.LengthSquared() > KineticConstants.EpsilonSq)

            touch.AssignSlidingNorm(corpus.SlidingNormal);
    }

    // Writes the transition's contact, stationary, walkable and sliding results back onto the body
    private static void AbsorbIntoCorpus(KineticBody corpus, SweepPath trail, ContactLedger touch, bool isOnTerrain)
    {
        if (touch.ContactPlaneValid)
        {
            corpus.ContactPlaneValid = true;
            corpus.ContactPlane = touch.ContactPlane;
            corpus.ContactPlaneCellId = touch.ContactPlaneCellId;
            corpus.ContactPlaneIsWater = touch.ContactPlaneIsWater;
            corpus.GroundNormal = touch.ContactPlane.Normal;
        }
        else if (touch.LastKnownContactPlaneValid)
        {
            corpus.ContactPlaneValid = true;
            corpus.ContactPlane = touch.LastKnownContactPlane;
            corpus.ContactPlaneCellId = touch.LastKnownContactPlaneCellId;
            corpus.ContactPlaneIsWater = touch.LastKnownContactPlaneIsWater;
            corpus.GroundNormal = touch.LastKnownContactPlane.Normal;
        }
        else
        {
            corpus.ContactPlaneValid = false;
        }

        if (corpus.ContactPlaneIsWater)
            corpus.TransientState |= TransientPhaseFlagSet.WaterContact;
        else
            corpus.TransientState &= ~TransientPhaseFlagSet.WaterContact;

        corpus.FramesStationaryFall = touch.FramesStationaryFall;
        AbsorbIntoCorpusRest(touch, corpus, trail, isOnTerrain);
    }

    private static void AbsorbIntoCorpusRest(ContactLedger touch, KineticBody corpus, SweepPath trail, bool isOnTerrain)
    {
        corpus.TransientState = (corpus.TransientState & ~StationaryBitset) | StationaryBitFor(touch.FramesStationaryFall);
        if (trail.HasPreviousPassablePolyg && trail.LastWalkableVertices is not null)
        {
            corpus.WalkablePolygonValid = true;
            corpus.WalkablePlane = trail.LastWalkablePlane;
            corpus.AssignPassableVertsPrecise(trail.LastWalkableVertices);
            corpus.WalkableUp = trail.LastWalkableUp;
        }
        else if (!isOnTerrain && !touch.ContactPlaneValid && !touch.LastKnownContactPlaneValid)
        {
            corpus.WalkablePolygonValid = false;
            corpus.WalkableVertices = null;
        }
        if (touch.SlidingNormalValid && touch.SlidingNormal.LengthSquared() > KineticConstants.EpsilonSq)
        {
            corpus.SlidingNormal = touch.SlidingNormal;
            corpus.TransientState |= TransientPhaseFlagSet.Sliding;
        }
        else
        {
            corpus.SlidingNormal = Vector3.Zero;
            corpus.TransientState &= ~TransientPhaseFlagSet.Sliding;
        }
    }

    private static void InspectLocate(
        uint movingActorIdent, Vector3 latestSpot, Vector3 markSpot, uint chamberIdent, bool isOnTerrain, bool ok,
        SweepPath trail, ContactLedger touch, bool impactNormValid, Vector3 impactNorm)
    {
        Vector3 post = trail.CheckPos;
        string cp = touch.ContactPlaneValid ? "valid" : (touch.LastKnownContactPlaneValid ? "lastKnown" : "none");
        string strike;
        if (impactNormValid)
        {
            string objRefPiece = touch.LastCollidedObjectGuid.HasValue ? FormattableString.Invariant($" obj=0x{touch.LastCollidedObjectGuid.Value:X8}") : "";
            string environPiece = touch.CollidedWithEnvironment ? " env" : "";
            int objRefTally = touch.CollideObjectGuids.Count;
            string objRefTallyPiece = objRefTally > 1 ? FormattableString.Invariant($" nObj={objRefTally}") : "";
            strike = FormattableString.Invariant($"yes n=({impactNorm.X:F2},{impactNorm.Y:F2},{impactNorm.Z:F2}){objRefPiece}{environPiece}{objRefTallyPiece}");
        }
        else
        {
            strike = "no";
        }
        Console.WriteLine(FormattableString.Invariant(
            $"[resolve] ent=0x{movingActorIdent:X8} in=({latestSpot.X:F3},{latestSpot.Y:F3},{latestSpot.Z:F3}) cell=0x{chamberIdent:X8} tgt=({markSpot.X:F3},{markSpot.Y:F3},{markSpot.Z:F3}) out=({post.X:F3},{post.Y:F3},{post.Z:F3}) cell=0x{trail.CheckCellId:X8} ok={ok} groundedIn={isOnTerrain} cp={cp} hit={strike} walkable={trail.HasPreviousPassablePolyg}"));
    }
}

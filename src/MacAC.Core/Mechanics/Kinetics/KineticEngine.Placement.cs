using System.Collections.Immutable;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticEngine
{
    private const float NoSlideTolerance = 0.0500000007f;

    private readonly record struct AdjustedGroupLocus(uint CellId, Vector3 CellLocalPosition, bool Resident);

    internal PlaceOutcome SetPosition(in KineticSetPositionRequest req, Func<PlaceContactReport, bool>? hndImpacts = null)
    {
        if (_temp?.EngagedZDepth >= ShiftScratch.Capacity)
            return Refuse(req, PlaceError.GeneralFailure);

        Changeover changeover = RentChangeover();
        ChamberArray footprint = changeover.SweepPath.SetLocusAskFootprint;
        try
        {
            footprint.Clear();
            changeover.SweepPath.CellCandidates.UnionMark = footprint;
            PrimeCarrier(changeover, req);

            PlaceOutcome outcome;
            if (Has(req.Flags, KineticSetPositionFlags.RandomScatter))
            {
                outcome = Scatter(changeover, req, hndImpacts, footprint);
            }
            else
            {
                outcome = Place(changeover, req, hndImpacts, footprint);
                if (outcome.Error != PlaceError.Ok && Has(req.Flags, KineticSetPositionFlags.Scatter))
                    outcome = Scatter(changeover, req, hndImpacts, footprint);
            }

            return outcome with { QueriedCellIds = footprint.SequencedIdents.ToImmutableArray() };
        }
        finally
        {
            changeover.SweepPath.CellCandidates.UnionMark = null;
            YieldChangeover(changeover);
        }
    }

    // Without slide permission a placement may only settle within 5 cm of where it was asked, in the
    // same cell
    internal static bool AdmitNoSlideStance(Vector3 settledLocus, Vector3 askedLocus, uint settledChamberIdent, uint adjustedChamberIdent)
    {
        Vector3 drift = settledLocus - askedLocus;
        return drift.X <= NoSlideTolerance && drift.Y <= NoSlideTolerance && settledChamberIdent == adjustedChamberIdent;
    }

    private static bool Has(KineticSetPositionFlags flagSet, KineticSetPositionFlags bit) => (flagSet & bit) != 0;

    private static bool IsLandChamber(uint chamberIdent) => (chamberIdent & LoBitmask) is >= 1u and <= 0x40u;

    // Retail adjust_position for placement: interiors resolve to the child cell holding the sphere,
    // else fall outside
    private AdjustedGroupLocus TuneSetLocus(uint seedChamberIdent, Vector3 chamberOwnLocus, Vector3 leadRealmOrbMiddle, ChamberArray askFootprint)
    {
        askFootprint.Add(seedChamberIdent);
        uint lo = seedChamberIdent & LoBitmask;
        bool loInSpan = lo is (>= 1u and <= 0x40u) or (>= 0x0100u and <= 0xFFFDu) or 0xFFFFu;
        if (!loInSpan)
            return new AdjustedGroupLocus(seedChamberIdent, chamberOwnLocus, Resident: false);

        if (lo >= 0x0100u)
        {
            var stash = DataCache;
            if (stash is null || stash.FetchChamberStruct(seedChamberIdent) is null)
                return new AdjustedGroupLocus(seedChamberIdent, chamberOwnLocus, Resident: false);

            uint descendant = CellHop.FindVisibleChildCell(stash, seedChamberIdent, leadRealmOrbMiddle, useStabRoster: true, askFootprint);
            if (descendant is not 0u)
                return new AdjustedGroupLocus(descendant, chamberOwnLocus, Resident: stash.FetchChamberStruct(descendant) is not null);

            var claimed = stash.FetchChamberStruct(seedChamberIdent);
            if (claimed is null || !claimed.SeenOutside)
                return new AdjustedGroupLocus(seedChamberIdent, chamberOwnLocus, Resident: false);
        }

        uint chamber = seedChamberIdent;
        Vector3 own = chamberOwnLocus;
        bool adjusted = MechLandDefs.TuneToBeyond(ref chamber, ref own);
        askFootprint.Add(chamber);
        return new AdjustedGroupLocus(chamber, own, adjusted && IsLbLandHoused(chamber));
    }

    private static void PrimeCarrier(Changeover changeover, in KineticSetPositionRequest req)
    {
        MoverFacts carrier = changeover.MoverFacts;
        carrier.StepUpHeight = req.StepUpHeight;
        carrier.StepDownHeight = req.StepDownHeight;
        carrier.StepDown = (req.MoverPhysicsState & KineticStateFlags.Missile) == 0;
        carrier.MoverPhysicsState = req.MoverPhysicsState;
        carrier.SelfEntityId = req.MovingEntityId;
        carrier.State = req.MoverFlags;
        carrier.Ethereal = (req.MoverPhysicsState & KineticStateFlags.Ethereal) != 0;
        changeover.SweepPath.PlacementAllowsSliding = Has(req.Flags, KineticSetPositionFlags.Slide);
    }

    // Retries the placement at random offsets within the scatter radii until one lands
    private PlaceOutcome Scatter(Changeover changeover, in KineticSetPositionRequest req, Func<PlaceContactReport, bool>? hndImpacts, ChamberArray footprint)
    {
        var outcome = Refuse(req, PlaceError.GeneralFailure);
        for (uint attempt = 0u; attempt < req.ScatterAttempts; ++attempt)
        {
            float dx = ((float)((SetLocusRandomUnit() * 2d) - 1d)) * req.ScatterRadiusX;
            float dy = ((float)((SetLocusRandomUnit() * 2d) - 1d)) * req.ScatterRadiusY;
            Vector3 nudge = new Vector3(dx, dy, 0f);
            var scattered = req with
            {
                Position = req.Position + nudge,
                CellLocalPosition = req.CellLocalPosition + nudge,
            };
            outcome = Place(changeover, scattered, hndImpacts, footprint);
            if (outcome.Error == PlaceError.Ok)
                break;
        }
        return outcome;
    }

    private PlaceOutcome Place(Changeover changeover, in KineticSetPositionRequest req, Func<PlaceContactReport, bool>? hndImpacts, ChamberArray footprint)
    {
        SweepPath trail = changeover.SweepPath;
        trail.CellCandidates.Clear();
        trail.WipePassable();

        var orbs = req.Spheres;
        float orbScaling = orbs.IsDefaultOrEmpty ? 1f : req.Scale;
        Vector3 leadOwnMiddle = orbs.IsDefaultOrEmpty
            ? new Vector3(0f, 0f, KineticConstants.DummyOrbRadius)
            : orbs[0].Origin * orbScaling;
        Vector3 leadRealmMiddle = Vector3.Transform(leadOwnMiddle, req.Orientation) + req.Position;

        var adjusted = TuneSetLocus(req.CellId, req.CellLocalPosition, leadRealmMiddle, footprint);
        if (!adjusted.Resident)
        {
            return new PlaceOutcome(
                PlaceError.Ok,
                KineticResidenceVerdict.DeferredCell,
                req.Position,
                req.Orientation,
                adjusted.CellId,
                adjusted.CellLocalPosition,
                CrossCellIds: [],
                CollidedObjectIds: []);
        }

        // Hooks, storage and corpses are pinned straight into their cell without a sweep.
        bool pinned = req.PlacementClass is KineticPlacementClass.Hook or KineticPlacementClass.Storage or KineticPlacementClass.Corpse;
        if (pinned)
        {
            if (adjusted.CellId is 0u)
                return Refuse(req, PlaceError.NoCell);

            bool chamberAltered = req.CurrentCellId != adjusted.CellId;
            return new PlaceOutcome(
                PlaceError.Ok,
                KineticResidenceVerdict.Committed,
                req.Position,
                req.Orientation,
                adjusted.CellId,
                adjusted.CellLocalPosition,
                CellChanged: chamberAltered,
                ShadowAction: chamberAltered ? ProxyCommitAction.Recalculate : ProxyCommitAction.None,
                CrossCellIds: [],
                CollidedObjectIds: []);
        }

        bool allowSlide = Has(req.Flags, KineticSetPositionFlags.Slide);
        trail.InitPath(req.Position, req.Position, adjusted.CellId, orbs, orbScaling, req.Orientation, req.Orientation);
        trail.InsertType = SlotKind.Placement;
        trail.PlacementAllowsSliding = allowSlide;

        bool valid = changeover.SeekValidLocus(this);
        if (valid && !allowSlide)
            valid = AdmitNoSlideStance(trail.CurPos, req.Position, trail.CurCellId, adjusted.CellId);

        var touch = changeover.ContactLedger;
        PlaceContactReport dossier = new PlaceContactReport(
            touch.ContactPlaneValid,
            touch.ContactPlane,
            touch.ContactPlaneCellId,
            touch.ContactPlaneIsWater,
            touch.LastKnownContactPlaneValid,
            touch.LastKnownContactPlane,
            touch.LastKnownContactPlaneCellId,
            touch.LastKnownContactPlaneIsWater,
            touch.SlidingNormalValid,
            touch.SlidingNormal,
            touch.CollisionNormalValid,
            touch.CollisionNormal,
            touch.CollidedWithEnvironment,
            touch.FramesStationaryFall,
            touch.AdjustOffset,
            touch.LastCollidedObjectGuid,
            touch.CollideObjectGuids.ToImmutableArray());
        bool handlerOutcome = !valid && hndImpacts?.Invoke(dossier) == true;

        bool inLink = touch.ContactPlaneValid;
        bool onPassable = KineticObjUpdate.IsPassableLink(inLink, touch.ContactPlane.Normal);

        if (!valid)
        {
            return new PlaceOutcome(
                handlerOutcome ? PlaceError.Collided : PlaceError.NoValidPosition,
                KineticResidenceVerdict.Unchanged,
                req.Position,
                req.Orientation,
                req.CellId,
                req.CellLocalPosition,
                InContact: inLink,
                OnWalkable: onPassable,
                ContactPlane: touch.ContactPlane,
                ContactPlaneCellId: touch.ContactPlaneCellId,
                ContactPlaneIsWater: touch.ContactPlaneIsWater,
                SlidingNormalValid: touch.SlidingNormalValid,
                SlidingNormal: touch.SlidingNormal,
                CollisionNormalValid: touch.CollisionNormalValid,
                CollisionNormal: touch.CollisionNormal,
                FramesStationaryFall: touch.FramesStationaryFall,
                CollisionHandlerResult: handlerOutcome,
                CollidedWithEnvironment: touch.CollidedWithEnvironment,
                CrossCellIds: [],
                CollidedObjectIds: dossier.CollidedObjectIds);
        }
        if (trail.CurCellId is 0u)
            return Refuse(req, PlaceError.NoCell);

        Vector3 outcomeOwn = adjusted.CellLocalPosition + (trail.CurPos - req.Position) - MechLandDefs.FetchChunkShift(adjusted.CellId, trail.CurCellId);
        var crossed = trail.CellCandidates.SequencedIdents.ToImmutableArray();
        bool hasBsp = (req.MoverPhysicsState & KineticStateFlags.HasPhysicsBsp) != 0;
        ProxyCommitAction shadeAct = hasBsp
            ? ProxyCommitAction.Recalculate
            : crossed.Length is not 0 ? ProxyCommitAction.Replace : ProxyCommitAction.Preserve;

        return new PlaceOutcome(
            PlaceError.Ok,
            KineticResidenceVerdict.Committed,
            trail.CurPos,
            req.Orientation,
            trail.CurCellId,
            outcomeOwn,
            inLink,
            onPassable,
            touch.ContactPlane,
            touch.ContactPlaneCellId,
            touch.ContactPlaneIsWater,
            touch.SlidingNormalValid,
            touch.SlidingNormal,
            touch.CollisionNormalValid,
            touch.CollisionNormal,
            touch.FramesStationaryFall,
            touch.CollidedWithEnvironment,
            handlerOutcome,
            CellChanged: req.CurrentCellId != trail.CurCellId,
            ShadowAction: shadeAct,
            CrossCellIds: shadeAct == ProxyCommitAction.Replace ? crossed : [],
            CollidedObjectIds: touch.CollideObjectGuids.ToImmutableArray());
    }

    private static PlaceOutcome Refuse(in KineticSetPositionRequest req, PlaceError problem)
    {
        return new(
        problem,
        KineticResidenceVerdict.Unchanged,
        req.Position,
        req.Orientation,
        req.CellId,
        req.CellLocalPosition,
        CrossCellIds: [],
        CollidedObjectIds: []);
    }
}

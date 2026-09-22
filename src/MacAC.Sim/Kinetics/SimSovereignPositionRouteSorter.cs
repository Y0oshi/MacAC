using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Kinetics;

internal static class SimSovereignPositionRouteSorter
{
    private const float UpperKineticsGap = 96f;
    private const KineticSetPositionFlags StartingBuildFlagSet = KineticSetPositionFlags.Placement | KineticSetPositionFlags.Slide;
    private const KineticSetPositionFlags AuthoritativeWarpFlagSet =
        KineticSetPositionFlags.Teleport | KineticSetPositionFlags.Slide | KineticSetPositionFlags.SendPositionEvent;

    internal static bool IsValidBuildWireLocus(in ObjectCreation.RemotePosition locus) => ValidLocus(locus);

    // What happens before routing: an applied position unparents first and, without animations,
    // applies the placement frame
    internal static SimGrantedPositionPrePlacementFlags DerivePreStanceFlagSet(PoseStampVerdict disposition, bool hasAnims)
    {
        return disposition switch
        {
            PoseStampVerdict.Apply => new(UnparentBeforeRouting: true, ApplyPlacementFrameBeforeRouting: !hasAnims),
            PoseStampVerdict.ForcePosition => default,
            PoseStampVerdict.Rejected => default,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null),
        };
    }

    internal static SimSovereignPositionRoute ClassifyBuild(in SimCreatePositionRouteRequest req)
    {
        var arbiter = req.Authority;
        if (!ValidBuildArbiter(arbiter) || !ValidActorSort(req.EntityKind) || req.Residence is SimCreateTenancyKind.Unknown)
            return RejectedArbiter(arbiter);

        var op = OpSort(req.EntityKind, startingBuild: true);
        bool reporting = req.PlacementFacts.ImpactLotEligible;

        // A child or carried object has no cell of its own until a position arrives.
        if (req.Residence is SimCreateTenancyKind.Parented or SimCreateTenancyKind.PickedUp)
            return Bare(arbiter, SimSovereignPositionVerdict.AwaitFreshPosition, op, reporting);

        if (req.AcceptedWirePosition is not { } locus || !ValidLocus(locus))
            return RejectedBlob(arbiter, op, reporting);

        return Bare(arbiter, SimSovereignPositionVerdict.SetPosition, op, reporting) with
        {
            SetPositionFlags = StartingBuildFlagSet,
            TeleportHookPhase = req.EntityKind is SimPositionActorKind.LocalPlayer ? SimWarpHookPhase.AfterEnterWorld : SimWarpHookPhase.None,
        };
    }

    internal static SimSovereignPositionRoute ToCellessBuildCourse(in SimSovereignPositionRoute course)
    {
        return Bare(course.Authority, SimSovereignPositionVerdict.AwaitFreshPosition, course.OperationKind, course.CollisionBatchEligible);
    }

    internal static SimSovereignPositionRoute ClassifyApprovedLocus(in SimGrantedPositionRouteRequest req)
    {
        var arbiter = req.Authority;
        var op = OpSort(req.EntityKind, startingBuild: false);
        bool reporting = req.PlacementFacts.ImpactLotEligible;
        if (!ValidApprovedArbiter(arbiter, req.EntityKind) || req.Source is SimGrantedPositionSource.Unknown || !ValidActorSort(req.EntityKind))
            return RejectedArbiter(arbiter, op, reporting);
        if (!ValidLocus(req.AcceptedWirePosition))
            return RejectedBlob(arbiter, op, reporting);

        var pre = DerivePreStanceFlagSet(arbiter.TimestampDisposition, req.HasAnimations);
        uint stance = req.PlacementFrame ?? 0u;
        SimSovereignPositionRoute Course(SimSovereignPositionVerdict verdict) => Bare(arbiter, verdict, op, reporting) with
        {
            PlacementFrame = stance,
            UnparentBeforeRouting = pre.UnparentBeforeRouting,
            ApplyPlacementFrameBeforeRouting = pre.ApplyPlacementFrameBeforeRouting,
        };

        if (arbiter.TimestampDisposition is PoseStampVerdict.ForcePosition)
        {
            return Course(SimSovereignPositionVerdict.SetPositionSimple) with
            {
                SetPositionFlags = AuthoritativeWarpFlagSet,
                PreserveHeading = true,
                SendPositionImmediately = true,
            };
        }

        if (req.EntityKind is SimPositionActorKind.LocalPlayer)
        {
            if (arbiter.WarpAdvanced)
            {
                return Course(SimSovereignPositionVerdict.SetPositionSimple) with
                {
                    SetPositionFlags = AuthoritativeWarpFlagSet,
                    TeleportHookPhase = SimWarpHookPhase.AfterPositionOperation,
                    ConstrainPhase = SimPositionConstrainPhase.AfterPositionOperation,
                    ZeroVelocity = true,
                };
            }

            bool lerp = req.UsePositionFromServer && req.HasContact;
            return Course(lerp ? SimSovereignPositionVerdict.Interpolate : SimSovereignPositionVerdict.NoPositionOperation) with
            {
                ConstrainPhase = SimPositionConstrainPhase.BeforePositionOperation,
            };
        }

        bool cellless = req.CommittedCellId is null or 0u;
        if (arbiter.WarpAdvanced || cellless)
        {
            return Course(SimSovereignPositionVerdict.SetPosition) with
            {
                SetPositionFlags = AuthoritativeWarpFlagSet,
                TeleportHookPhase = SimWarpHookPhase.BeforePositionOperation,
                ConstrainPhase = SimPositionConstrainPhase.AfterPositionOperation,
            };
        }

        bool link = req.Source is SimGrantedPositionSource.SameIncarnationCreate || req.HasContact;
        if (!link)
            return Course(SimSovereignPositionVerdict.NoPositionOperation);

        if (!float.IsFinite(req.PlayerDistance) || req.PlayerDistance < 0f)
            return RejectedBlob(arbiter, op, reporting);

        // Close enough to simulate: interpolate; otherwise snap there
        bool nearby = req.PlayerDistance < UpperKineticsGap;
        return Course(nearby ? SimSovereignPositionVerdict.Interpolate : SimSovereignPositionVerdict.SetPositionSimple) with
        {
            SetPositionFlags = nearby ? KineticSetPositionFlags.None : AuthoritativeWarpFlagSet,
            StopInterpolating = !nearby,
            ConstrainPhase = SimPositionConstrainPhase.AfterPositionOperation,
        };
    }

    // A route with every hook off; the cases below switch on what they need
    private static SimSovereignPositionRoute Bare(
        in SimSovereignPositionAuthority arbiter,
        SimSovereignPositionVerdict verdict,
        SimSetPositionOperationKind op,
        bool reporting)
    {
        return new(
            arbiter,
            verdict,
            op,
            KineticSetPositionFlags.None,
            0u,
            UnparentBeforeRouting: false,
            ApplyPlacementFrameBeforeRouting: false,
            LeaveWorld: false,
            TeleportHookPhase: SimWarpHookPhase.None,
            StopInterpolating: false,
            ConstrainPhase: SimPositionConstrainPhase.None,
            PreserveHeading: false,
            ZeroVelocity: false,
            SendPositionImmediately: false,
            reporting);
    }

    private static SimSovereignPositionRoute RejectedArbiter(
        in SimSovereignPositionAuthority arbiter,
        SimSetPositionOperationKind op = SimSetPositionOperationKind.RemoteAuthoritative,
        bool reporting = false)
    {
        return Bare(arbiter, SimSovereignPositionVerdict.RejectedAuthority, op, reporting);
    }

    private static SimSovereignPositionRoute RejectedBlob(in SimSovereignPositionAuthority arbiter, SimSetPositionOperationKind op, bool reporting) =>
        Bare(arbiter, SimSovereignPositionVerdict.RejectedData, op, reporting);

    private static bool ValidBuildArbiter(in SimSovereignPositionAuthority arbiter)
    {
        return arbiter.IsStructurallyValid
        && arbiter.TimestampDisposition is PoseStampVerdict.Apply
        && arbiter.PreviousTeleportSequence == arbiter.AcceptedTeleportSequence;
    }

    private static bool ValidApprovedArbiter(in SimSovereignPositionAuthority arbiter, SimPositionActorKind sort)
    {
        if (!arbiter.IsStructurallyValid || arbiter.WarpRegressed)
            return false;
        return arbiter.TimestampDisposition switch
        {
            PoseStampVerdict.Apply => true,
            PoseStampVerdict.ForcePosition => sort is SimPositionActorKind.LocalPlayer,
            _ => false,
        };
    }

    private static bool ValidActorSort(SimPositionActorKind sort)
    {
        return sort is SimPositionActorKind.LocalPlayer or SimPositionActorKind.Remote or SimPositionActorKind.Projectile;
    }

    private static bool ValidLocus(in ObjectCreation.RemotePosition position)
    {
        Vector3 origin = new Vector3(position.PositionX, position.PositionY, position.PositionZ);
        Quaternion facing = new Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW);
        return float.IsFinite(origin.X) && float.IsFinite(origin.Y) && float.IsFinite(origin.Z)
            && float.IsFinite(facing.X) && float.IsFinite(facing.Y) && float.IsFinite(facing.Z) && float.IsFinite(facing.W)
            && PoseValidation.IsValid(position.LandblockId, origin, facing);
    }

    private static SimSetPositionOperationKind OpSort(SimPositionActorKind sort, bool startingBuild)
    {
        return sort switch
        {
            SimPositionActorKind.LocalPlayer when startingBuild => SimSetPositionOperationKind.InitialLogin,
            SimPositionActorKind.LocalPlayer => SimSetPositionOperationKind.LocalAuthoritative,
            SimPositionActorKind.Projectile => SimSetPositionOperationKind.ProjectileAuthoritative,
            _ => SimSetPositionOperationKind.RemoteAuthoritative,
        };
    }
}

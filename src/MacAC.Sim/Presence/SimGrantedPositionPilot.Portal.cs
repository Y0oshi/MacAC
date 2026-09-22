using System.Numerics;
using MacAC.Assets;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Presence;

public sealed partial class SimGrantedPositionPilot
{
    private (long RevealGeneration, ushort TeleportSequence)? _sealedGateway;

    internal bool TryAbsorbGatewaySeal(
        long unveilGen,
        ushort warpSeries)
    {
        if (_sealedGateway is not { } committed
            || committed.RevealGeneration != unveilGen
            || committed.TeleportSequence != warpSeries)

            return false;
        _sealedGateway = null;
        return true;
    }

    internal SimGrantedPositionExecutionStatus TryPerformApprovedGatewayArrival(
        in SimWarpDestination dest,
        in SimPortalPlacementAuthority gateway)
    {
        if (!gateway.IsValid
            || !_entityObjects.Entities.TryFetchEngaged(
                _selfOid(), out SimActorRecord capture)
            || capture.KineticBody is null
            || capture.Key is not { } tag
            || _entityObjects.TryFetchStartingBuildResidence(capture, out _))
        {
            RecordGatewayAttempt(
                SimGrantedPositionExecutionStatus.NotApplicable,
                gateway,
                settledChamber: 0u);
            return SimGrantedPositionExecutionStatus.NotApplicable;
        }

        var course = GatewayArrivalCourse(
            capture, tag, dest, _epoch());
        if (!course.Accepted)
        {
            RecordGatewayAttempt(
                SimGrantedPositionExecutionStatus.Rejected,
                gateway,
                capture.WholeChamberTag);
            return SimGrantedPositionExecutionStatus.Rejected;
        }

        var setLocus = _entityObjects.Physics.SetPosition;
        ulong approvedVer = capture.PositionAuthorityVersion;
        var ticket =
            setLocus.TryCommenceExclusiveAuthoredStance(
                capture,
                approvedVer,
                course.OperationKind,
                gateway);
        if (!ticket.IsValid)
        {
            RecordGatewayAttempt(
                SimGrantedPositionExecutionStatus.Contention,
                gateway,
                capture.WholeChamberTag);
            return SimGrantedPositionExecutionStatus.Contention;
        }

        return SubmitGateway(capture, ticket, course, gateway);
    }

    private static SimSovereignPositionRoute GatewayArrivalCourse(
        SimActorRecord capture,
        SimActorKey tag,
        in SimWarpDestination dest,
        SimEpochTicket gen)
    {
        ushort approvedWarp = dest.TeleportSequence;
        ushort precedingWarp = unchecked((ushort)(approvedWarp - 1));
        var arbiter = new SimSovereignPositionAuthority(
            gen,
            tag,
            capture.PositionAuthorityVersion,
            dest.PositionSequence,
            precedingWarp,
            approvedWarp,
            PoseStampVerdict.Apply);

        bool hasAnims = (capture.Snapshot.MotionTableId
                ?? capture.Snapshot.Physics?.MotionTableId) is { } locomotionChartIdent
            && locomotionChartIdent is not 0u;

        var wireLocus = new ObjectCreation.RemotePosition(
            dest.CellId,
            dest.Position.Frame.Origin.X,
            dest.Position.Frame.Origin.Y,
            dest.Position.Frame.Origin.Z,
            dest.Position.Frame.Orientation.W,
            dest.Position.Frame.Orientation.X,
            dest.Position.Frame.Orientation.Y,
            dest.Position.Frame.Orientation.Z);

        var req = new SimGrantedPositionRouteRequest(
            arbiter,
            SimPositionActorKind.LocalPlayer,
            SimGrantedPositionSource.PositionEvent,
            wireLocus,
            PlacementFrame: null,
            PositionPackVelocity: null,
            CommittedCellId: capture.WholeChamberTag,
            HasContact: false,
            PlayerDistance: 0f,
            UsePositionFromServer: false,
            hasAnims,
            new SimPositionPlacementFacts(
                capture.FinalKineticsCondition,
                capture.Snapshot.SetupTableId is not null));

        return SimSovereignPositionRouteSorter
            .ClassifyApprovedLocus(req);
    }

    private SimGrantedPositionExecutionStatus SubmitGateway(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket,
        in SimSovereignPositionRoute course,
        in SimPortalPlacementAuthority gateway)
    {
        var setLocus = _entityObjects.Physics.SetPosition;
        var condition =
            setLocus.TryReadyAndSubmitAuthoredStance(
                capture,
                ticket,
                course.OperationKind,
                course.SetPositionFlags,
                _link,
                _clock.SimulationMomentSecs,
                out SimSetPositionUpshot verdict,
                gateway: gateway,
                locateRealmShiftFromCoreCycle: true);

        if (condition != SimSetPositionMoverStagingStatus.Prepared)
        {
            if (condition.IsRetryable())
            {
                RetainQueued(setLocus, new PendingUnit
                {
                    Record = capture,
                    Token = ticket,
                    Course = course,
                    ExpectingSealWake = false,
                    LocusSignalOwed = false,
                    Portal = gateway,
                });
                RecordGatewayAttempt(
                    SimGrantedPositionExecutionStatus.Contention,
                    gateway,
                    capture.WholeChamberTag);
                return SimGrantedPositionExecutionStatus.Contention;
            }

            CancelTicket(setLocus, ticket);
            RecordGatewayAttempt(
                SimGrantedPositionExecutionStatus.Rejected,
                gateway,
                capture.WholeChamberTag);
            return SimGrantedPositionExecutionStatus.Rejected;
        }

        switch (verdict.Status)
        {
            case SimSetPositionStatus.CommittedHostAcknowledgementPending:
                SettleGateway(capture, course, gateway);
                return SimGrantedPositionExecutionStatus.Committed;

            case SimSetPositionStatus.DeferredCell:
                while (setLocus.TryGlimpseProj(
                        out SimPlacementMirrorCapture shelved)
                    && shelved.Token.Entity == ticket.Entity
                    && shelved.Kind is SimPlacementMirrorKind.Withdraw)
                {
                    if (!setLocus.AcknowledgeProj(shelved.Token))
                        break;
                }
                if (!setLocus.MonitorStanceWrapUp(ticket))
                {
                    CancelTicket(setLocus, ticket);
                    RecordGatewayAttempt(
                        SimGrantedPositionExecutionStatus.Rejected,
                        gateway,
                        capture.WholeChamberTag);
                    return SimGrantedPositionExecutionStatus.Rejected;
                }
                RetainQueued(setLocus, new PendingUnit
                {
                    Record = capture,
                    Token = ticket,
                    Course = course,
                    ExpectingSealWake = true,
                    LocusSignalOwed = false,
                    Portal = gateway,
                });
                RecordGatewayAttempt(
                    SimGrantedPositionExecutionStatus.DeferredCell,
                    gateway,
                    capture.WholeChamberTag);
                return SimGrantedPositionExecutionStatus.DeferredCell;

            default:
                CancelTicket(setLocus, ticket);
                RecordGatewayAttempt(
                    SimGrantedPositionExecutionStatus.Rejected,
                    gateway,
                    capture.WholeChamberTag);
                return SimGrantedPositionExecutionStatus.Rejected;
        }
    }

    private static void RecordGatewayAttempt(
        SimGrantedPositionExecutionStatus condition,
        in SimPortalPlacementAuthority gateway,
        uint settledChamber)
    {
        KineticTelemetry.TraceOwnWarpArrival(
            cause: "portal",
            stanceCondition: condition.ToString(),
            gatewayGen: gateway.RevealGeneration,
            warpSeries: gateway.TeleportSequence,
            destChamber: gateway.Projection.DestinationCell,
            settledChamber: settledChamber,
            tapRearRan: false,
            leashLoaded: false,
            autorunCancelled: false);
    }

    private bool GatewayArbiterHolds(
        in SimPortalPlacementAuthority gateway) =>
        _gatewayArbiterHolds is null || _gatewayArbiterHolds(gateway);

    private void SettleGateway(
        SimActorRecord capture,
        in SimSovereignPositionRoute course,
        in SimPortalPlacementAuthority gateway)
    {
        _sealedGateway = (gateway.RevealGeneration, gateway.TeleportSequence);
        if (capture.ServerGuid != _selfOid())
            return;
        if (_avatar() is not { } driver)
            return;
        bool tapRearRan = course.ExecutionsWarpTap;
        driver.SealCanonWarpCycle(
            zeroVel: course.ZeroVelocity,
            rearmConstraintLeash: course.ConstrainFollowingRouting,
            execWarpTapRear: tapRearRan);
        bool autorunCancelled = _locomotion()?.AbortAutoExec() ?? false;
        if (!_srvLocusRule())
        {
            _outgoing.TryTransmitTravel(
                _session(),
                driver,
                driver.GrabExhibitOutcome());
        }

        KineticTelemetry.TraceOwnWarpArrival(
            cause: "portal",
            stanceCondition: "Committed",
            gatewayGen: gateway.RevealGeneration,
            warpSeries: gateway.TeleportSequence,
            destChamber: gateway.Projection.DestinationCell,
            settledChamber: capture.WholeChamberTag,
            tapRearRan: tapRearRan,
            leashLoaded: driver.PlaceKeeper?.Constraint?.IsConstrained
                ?? false,
            autorunCancelled: autorunCancelled);
    }
}

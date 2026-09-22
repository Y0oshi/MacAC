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
    private sealed class PendingUnit
    {
        internal required SimActorRecord Record { get; init; }
        internal required SimActorPlacementTicket Token { get; init; }
        internal required SimSovereignPositionRoute Course { get; init; }
        internal required bool ExpectingSealWake { get; init; }

        internal required bool LocusSignalOwed { get; init; }

        internal SimPortalPlacementAuthority Portal { get; init; }
    }

    private readonly record struct ForceSighting(
        SimActorKey Entity,
        ulong PositionAuthorityVersion,
        SimSovereignPositionRoute Route);

    private readonly SimActorObjectLifetime _entityObjects;

    private readonly ISimCoreClock _clock;

    private readonly IBakedContactSource _link;

    private readonly AvatarOutboundDriver _outgoing;

    private readonly Func<SimEpochTicket> _epoch;

    private readonly Func<uint> _selfOid;

    private readonly Func<AvatarLocomotionDriver?> _avatar;

    private readonly Func<bool> _srvLocusRule;

    private readonly Func<RealmSession?> _session;

    private readonly Func<SimAvatarLocomotionLedger?> _locomotion;

    private readonly Func<SimPortalPlacementAuthority, bool>? _gatewayArbiterHolds;

    private PendingUnit? _queued;

    private ForceSighting? _currentForce;

    private object? _course;

    internal SimGrantedPositionPilot(
        SimActorObjectLifetime entityObjects,
        ISimCoreClock clock,
        IBakedContactSource collisionSource,
        AvatarOutboundDriver localPlayerOutbound,
        Func<SimEpochTicket> generation,
        Func<uint> localPlayerServerGuid,
        Func<AvatarLocomotionDriver?> localController,
        Func<bool> usePositionFromServer,
        Func<RealmSession?> session,
        Func<SimAvatarLocomotionLedger?>? ownTravelPhase = null,
        Func<SimPortalPlacementAuthority, bool>? isGatewayArbiterLatest = null)
    {
        _entityObjects = entityObjects
            ?? throw new ArgumentNullException(nameof(entityObjects));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _link = collisionSource
            ?? throw new ArgumentNullException(nameof(collisionSource));
        _outgoing = localPlayerOutbound
            ?? throw new ArgumentNullException(nameof(localPlayerOutbound));
        _epoch = generation
            ?? throw new ArgumentNullException(nameof(generation));
        _selfOid = localPlayerServerGuid
            ?? throw new ArgumentNullException(nameof(localPlayerServerGuid));
        _avatar = localController
            ?? throw new ArgumentNullException(nameof(localController));
        _srvLocusRule = usePositionFromServer
            ?? throw new ArgumentNullException(nameof(usePositionFromServer));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _locomotion = ownTravelPhase ?? (static () => null);
        _gatewayArbiterHolds = isGatewayArbiterLatest;
        _entityObjects.EnrollApprovedLocusSteerOwnership(
            () => _queued is null ? 0 : 1);
    }

    internal int QueuedTally => _queued is null ? 0 : 1;

    internal void FastenCourse(object course)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (_course is not null && !ReferenceEquals(_course, course))
        {
            throw new InvalidOperationException(
                "An accepted-position drive controller serves one session "
                + "route at a time; the prior route has to be disposed "
                + "(session reset precedes a new route) prior to a "
                + "replacement attaches");
        }
        _course = course;
    }

    // Route-scoped teardown: abandons any pending operation, but ONLY when course is the attached
    // owner
    internal void UnfastenCourse(object course)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (!ReferenceEquals(_course, course))
            return;
        _course = null;
        DiscardQueued();
    }

    internal SimGrantedPositionExecutionStatus TryExecuteAcceptedLocalPosition(
        SimActorRecord capture,
        in RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        in GrantedKineticsTimestamps timestamps,
        ushort earlierWarpSeries)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (disposition is not PoseStampVerdict.ForcePosition
            || capture.ServerGuid != _selfOid()
            || capture.KineticBody is null
            || capture.Key is not { } tag
            || _entityObjects.TryFetchStartingBuildResidence(capture, out _))

            return SimGrantedPositionExecutionStatus.NotApplicable;

        var course = ForceLocusCourse(
            capture, tag, refresh, disposition, timestamps, earlierWarpSeries);
        if (!course.Accepted)

            return SimGrantedPositionExecutionStatus.Rejected;

        ulong approvedVer = capture.PositionAuthorityVersion;
        _currentForce = new ForceSighting(tag, approvedVer, course);

        var setLocus = _entityObjects.Physics.SetPosition;
        var ticket =
            setLocus.TryCommenceExclusiveAuthoredStance(
                capture,
                approvedVer,
                course.OperationKind);
        if (!ticket.IsValid)

            return SimGrantedPositionExecutionStatus.Contention;

        return Submit(capture, ticket, course);
    }

    internal void Advance()
    {
        if (_queued is not { } queued)
            return;

        var setLocus = _entityObjects.Physics.SetPosition;
        if (queued.ExpectingSealWake)
        {
            if (setLocus.TryGlimpseAcknowledgedStance(
                    queued.Token,
                    out SimPlacementMirrorTicket proj))
            {
                if (!setLocus.AbsorbAcknowledgedStance(
                        queued.Token, proj))

                    return;
                if (queued.Portal.Present)
                {
                    _queued = null;
                    if (!GatewayArbiterHolds(queued.Portal))
                    {
                        KineticTelemetry.TraceOwnWarpArrival(
                            cause: "portal",
                            stanceCondition: "AbandonedAtWake",
                            gatewayGen: queued.Portal.RevealGeneration,
                            warpSeries: queued.Portal.TeleportSequence,
                            destChamber:
                                queued.Portal.Projection.DestinationCell,
                            settledChamber: queued.Record.WholeChamberTag,
                            tapRearRan: false,
                            leashLoaded: false,
                            autorunCancelled: false);
                        return;
                    }
                    SettleGateway(
                        queued.Record, queued.Course, queued.Portal);
                    return;
                }
                SettleAndAck(queued.Record, queued.Course);
                SettleFollowingWake(
                    queued.Record,
                    queued.Token,
                    queued.Course,
                    locusSignalOwed: false);
                return;
            }

            if (setLocus.IsStanceWrapUpFollowed(queued.Token))
            {
                return;
            }

            if (queued.Portal.Present)
            {
                _queued = null;
                KineticTelemetry.TraceOwnWarpArrival(
                    cause: "portal",
                    stanceCondition: "WatchDied",
                    gatewayGen: queued.Portal.RevealGeneration,
                    warpSeries: queued.Portal.TeleportSequence,
                    destChamber: queued.Portal.Projection.DestinationCell,
                    settledChamber: queued.Record.WholeChamberTag,
                    tapRearRan: false,
                    leashLoaded: false,
                    autorunCancelled: false);
                return;
            }
            SettleFollowingWake(
                queued.Record,
                queued.Token,
                queued.Course,
                queued.LocusSignalOwed);
            return;
        }

        if (setLocus.IsStanceLatest(queued.Token))
        {
            if (queued.Portal.Present
                && !GatewayArbiterHolds(queued.Portal))
            {
                _queued = null;
                CancelTicket(setLocus, queued.Token);
                KineticTelemetry.TraceOwnWarpArrival(
                    cause: "portal",
                    stanceCondition: "AbandonedAtWake",
                    gatewayGen: queued.Portal.RevealGeneration,
                    warpSeries: queued.Portal.TeleportSequence,
                    destChamber: queued.Portal.Projection.DestinationCell,
                    settledChamber: queued.Record.WholeChamberTag,
                    tapRearRan: false,
                    leashLoaded: false,
                    autorunCancelled: false);
                return;
            }
            _ = queued.Portal.Present
                ? SubmitGateway(
                    queued.Record, queued.Token, queued.Course, queued.Portal)
                : Submit(queued.Record, queued.Token, queued.Course);
            return;
        }

        if (queued.Portal.Present)
        {
            _queued = null;
            KineticTelemetry.TraceOwnWarpArrival(
                cause: "portal",
                stanceCondition: "PrepareRetryLost",
                gatewayGen: queued.Portal.RevealGeneration,
                warpSeries: queued.Portal.TeleportSequence,
                destChamber: queued.Portal.Projection.DestinationCell,
                settledChamber: queued.Record.WholeChamberTag,
                tapRearRan: false,
                leashLoaded: false,
                autorunCancelled: false);
            return;
        }
        SettleFollowingWake(
            queued.Record,
            queued.Token,
            queued.Course,
            queued.LocusSignalOwed);
    }

    private void DiscardQueued()
    {
        _currentForce = null;
        _sealedGateway = null;
        if (_queued is not { } queued)
            return;
        _queued = null;
        var setLocus = _entityObjects.Physics.SetPosition;
        DiscardQueuedRest(queued, setLocus);
    }

    private void DiscardQueuedRest(PendingUnit queued, SimSetPositionLedger setLocus)
    {
        setLocus.DropStanceWrapUp(queued.Token);
        var abort =
                    setLocus.DropPreciseStance(
                        queued.Token,
                        revertCancelledPark: true);
        if (abort.IsValid)
            setLocus.BroadcastAbort(abort);
    }

    private void SettleFollowingWake(
        SimActorRecord terminalCapture,
        in SimActorPlacementTicket terminalTicket,
        in SimSovereignPositionRoute terminalCourse,
        bool locusSignalOwed)
    {
        _queued = null;

        if (!_entityObjects.Entities.TryFetchEngaged(
                terminalCapture.ServerGuid, out SimActorRecord capture)
            || capture.ServerGuid != _selfOid()
            || capture.KineticBody is null
            || capture.Key is not { } tag
            || tag != terminalTicket.Entity)

            return;

        if (locusSignalOwed && _avatar() is { } terminalDriver)
            TransmitLocusInstant(terminalDriver, terminalCourse);

        ulong latest = capture.PositionAuthorityVersion;
        if (terminalTicket.PositionAuthorityVersion == latest)
            return;

        if (_currentForce is not { } newest
            || newest.Entity != tag
            || newest.PositionAuthorityVersion != latest)

            return;

        var setLocus = _entityObjects.Physics.SetPosition;
        var ticket =
            setLocus.TryCommenceExclusiveAuthoredStance(
                capture,
                latest,
                newest.Route.OperationKind);
        if (!ticket.IsValid)
        {
            _queued = new PendingUnit
            {
                Record = terminalCapture,
                Token = terminalTicket,
                Course = terminalCourse,
                ExpectingSealWake = false,
                LocusSignalOwed = false,
            };
            return;
        }

        _ = Submit(capture, ticket, newest.Route);
    }

    private SimGrantedPositionExecutionStatus Submit(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket,
        in SimSovereignPositionRoute course)
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
                    LocusSignalOwed = true,
                });
                return SimGrantedPositionExecutionStatus.Contention;
            }

            CancelTicket(setLocus, ticket);
            SettleFollowingWake(capture, ticket, course, locusSignalOwed: true);
            return SimGrantedPositionExecutionStatus.Rejected;
        }

        switch (verdict.Status)
        {
            case SimSetPositionStatus.CommittedHostAcknowledgementPending:
                SettleAndAck(capture, course);
                SettleFollowingWake(capture, ticket, course, locusSignalOwed: false);
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
                    SettleFollowingWake(capture, ticket, course, locusSignalOwed: true);
                    return SimGrantedPositionExecutionStatus.Rejected;
                }
                RetainQueued(setLocus, new PendingUnit
                {
                    Record = capture,
                    Token = ticket,
                    Course = course,
                    ExpectingSealWake = true,
                    LocusSignalOwed = true,
                });
                return SimGrantedPositionExecutionStatus.DeferredCell;

            default:
                CancelTicket(setLocus, ticket);
                SettleFollowingWake(capture, ticket, course, locusSignalOwed: true);
                return SimGrantedPositionExecutionStatus.Rejected;
        }
    }

    private void RetainQueued(
        SimSetPositionLedger setLocus,
        PendingUnit upcoming)
    {
        if (_queued is { } extant
            && extant.Token != upcoming.Token
            && setLocus.IsStanceLatest(extant.Token))
        {
            throw new InvalidOperationException(
                "SimGrantedPositionPilot tracks no more than one "
                + "pending accepted-position operation (the local player); "
                + "a still-live pending operation must never be silently "
                + "overwritten by a new one");
        }
        _queued = upcoming;
    }

    private static void CancelTicket(
        SimSetPositionLedger setLocus,
        in SimActorPlacementTicket ticket)
    {
        var abort =
            setLocus.DropPreciseStance(
                ticket,
                revertCancelledPark: true);
        if (abort.IsValid)
            setLocus.BroadcastAbort(abort);
    }

    private void SettleAndAck(
        SimActorRecord capture,
        in SimSovereignPositionRoute course)
    {
        if (capture.ServerGuid != _selfOid())
            return;
        if (_avatar() is not { } driver)
            return;
        driver.SealCanonForceLocusCycle();
        TransmitLocusInstant(driver, course);
    }

    private void TransmitLocusInstant(
        AvatarLocomotionDriver driver,
        in SimSovereignPositionRoute course)
    {
        if (!course.SendPositionImmediately)
            return;
        _outgoing.TransmitImmediateLocus(_session(), driver);
    }

    private SimSovereignPositionRoute ForceLocusCourse(
        SimActorRecord capture,
        SimActorKey tag,
        in RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        in GrantedKineticsTimestamps timestamps,
        ushort earlierWarpSeries)
    {
        var arbiter = new SimSovereignPositionAuthority(
            _epoch(),
            tag,
            capture.PositionAuthorityVersion,
            refresh.PositionSequence,
            earlierWarpSeries,
            timestamps.Teleport,
            disposition);

        bool hasLink = refresh.IsGrounded;
        bool hasAnims = (capture.Snapshot.MotionTableId
                ?? capture.Snapshot.Physics?.MotionTableId) is { } locomotionChartIdent
            && locomotionChartIdent is not 0u;

        bool useLocusFromSrv = _srvLocusRule();
        float avatarGap = 0f;
        if (_avatar() is { } driverForGap
            && (capture.Snapshot.Physics?.Position
                ?? capture.Snapshot.Position) is { } approvedForGap)
        {
            Vector3 mark = new Vector3(
                approvedForGap.PositionX,
                approvedForGap.PositionY,
                approvedForGap.PositionZ);
            avatarGap = Vector3.Distance(
                mark, driverForGap.Position);
        }

        var req = new SimGrantedPositionRouteRequest(
            arbiter,
            SimPositionActorKind.LocalPlayer,
            SimGrantedPositionSource.PositionEvent,
            refresh.Position,
            refresh.PlacementId,
            refresh.Velocity,
            capture.WholeChamberTag,
            hasLink,
            avatarGap,
            useLocusFromSrv,
            hasAnims,
            new SimPositionPlacementFacts(
                capture.FinalKineticsCondition,
                capture.Snapshot.SetupTableId is not null));

        return SimSovereignPositionRouteSorter
            .ClassifyApprovedLocus(req);
    }
}

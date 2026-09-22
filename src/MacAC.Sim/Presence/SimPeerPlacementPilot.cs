using System.Numerics;
using MacAC.Assets;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Presence;

public interface ISimPeerPlacementServiceWindow
{
    bool IsWithinServiceWindow(uint lbIdent);
}

internal enum SimPeerPlacementExecutionStatus : byte
{
    NotApplicable,

    Refused,

    Contention,

    RejectedPreparation,

    Committed,

    Deferred,

    RejectedByPlacement,
}

internal static class SimPeerPlacementExecutionStatusExtensions
{
    // When the placement did not go through the physics world, the body is parked at the accepted pose
    // instead
    internal static bool StoresApprovedDest(this SimPeerPlacementExecutionStatus status)
    {
        return status switch
        {
            SimPeerPlacementExecutionStatus.NotApplicable => true,
            SimPeerPlacementExecutionStatus.Refused => true,
            SimPeerPlacementExecutionStatus.Contention => true,
            SimPeerPlacementExecutionStatus.RejectedPreparation => true,
            SimPeerPlacementExecutionStatus.Committed => false,
            SimPeerPlacementExecutionStatus.Deferred => false,
            SimPeerPlacementExecutionStatus.RejectedByPlacement => false,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }
}

internal sealed class SimPeerPlacementPilot
{
    private sealed record Pending(SimActorRecord Record, SimActorPlacementTicket Token, SimSovereignPositionRoute Route);

    private readonly SimActorObjectLifetime _entityObjects;
    private readonly ISimCoreClock _clock;
    private readonly IBakedContactSource _link;
    private readonly ISimPeerPlacementServiceWindow _window;

    private readonly Dictionary<SimActorKey, Pending> _queued = [];
    private readonly Dictionary<SimActorKey, SimActorPlacementTicket> _expectingAck = [];
    private readonly List<SimActorKey> _steerTemp = [];
    private readonly List<SimActorKey> _ackTemp = [];
    private readonly List<SimActorKey> _queuedTemp = [];
    private bool _driving;
    private object? _course;

    internal SimPeerPlacementPilot(SimActorObjectLifetime entityObjects, ISimCoreClock clock, IBakedContactSource collisionSource, ISimPeerPlacementServiceWindow serviceWindow)
    {
        _entityObjects = entityObjects ?? throw new ArgumentNullException(nameof(entityObjects));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _link = collisionSource ?? throw new ArgumentNullException(nameof(collisionSource));
        _window = serviceWindow ?? throw new ArgumentNullException(nameof(serviceWindow));
        _entityObjects.EnrollDistantStanceSteerOwnership(OnlineQueuedTally);
        _entityObjects.EnrollDistantStanceSteerOwnership(OnlineExpectingAckTally);
    }

    private SimSetPositionLedger Placements => _entityObjects.Physics.SetPosition;

    internal int QueuedTally => _queued.Count;

    internal static bool OwnsStance(SimSovereignPositionRoute course)
    {
        return course.OperationKind is SimSetPositionOperationKind.RemoteAuthoritative or SimSetPositionOperationKind.ProjectileAuthoritative
        && course.Disposition is SimSovereignPositionVerdict.SetPosition or SimSovereignPositionVerdict.SetPositionSimple
        && (course.SetPositionFlags & KineticSetPositionFlags.Teleport) != 0;
    }

    internal void FastenCourse(object course)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (_course is not null && !ReferenceEquals(_course, course))
        {
            throw new InvalidOperationException(
                "A remote placement drive controller serves one session "
                + "route at a time; the prior route has to be disposed "
                + "(session reset precedes a new route) prior to a "
                + "replacement attaches");
        }
        _course = course;
    }

    internal void UnfastenCourse(object course)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (!ReferenceEquals(_course, course))
            return;
        _course = null;

        var stances = Placements;
        if (_queued.Count is not 0)
        {
            SimActorPlacementTicket[] abandoned = [.. _queued.Values.Select(static pending => pending.Token)];
            _queued.Clear();
            Abandon(stances, abandoned);
        }
        if (_expectingAck.Count is not 0)
        {
            SimActorPlacementTicket[] abandoned = [.. _expectingAck.Values];
            _expectingAck.Clear();
            Abandon(stances, abandoned);
        }
    }

    internal SimPeerPlacementExecutionStatus TryPerformApprovedDistantLocus(SimActorRecord capture, in SimSovereignPositionRoute course)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!OwnsStance(course) || capture.KineticBody is null || capture.Key is not { } tag)
            return SimPeerPlacementExecutionStatus.NotApplicable;

        var stances = Placements;
        if (_queued.TryGetValue(tag, out Pending? stale) && !stances.IsStanceLatest(stale.Token))
            _queued.Remove(tag);

        if (ApprovedPosture(capture) is not { } approved || !MayAttempt(stances, approved.LandblockId))
            return SimPeerPlacementExecutionStatus.Refused;

        var ticket = stances.TryCommenceExclusiveAuthoredStance(capture, capture.PositionAuthorityVersion, course.OperationKind);
        return ticket.IsValid ? Submit(capture, ticket, course) : SimPeerPlacementExecutionStatus.Contention;
    }

    internal SimPeerPlacementExecutionStatus ImposeApprovedDistantFarawaySnap(SimActorRecord capture, PeerMotion distant, in SimSovereignPositionRoute route)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(distant);
        if (!SimPeerFarSnapPosition.OwnsFarawaySnap(route))
        {
            throw new ArgumentException(
                "Only a remote far-snap classification (SetPositionSimple, "
                + "RemoteAuthoritative, Teleport-flagged) may be applied "
                + "through the far-snap arm; the caller must select the arm "
                + "with SimPeerFarSnapPosition.ResolveArm",
                nameof(route));
        }

        if (route.StopInterpolating)
            distant.Lerp.Clear();
        return PerformThenPark(capture, route);
    }

    internal SimPeerPlacementExecutionStatus ImposeApprovedDistantWarp(SimActorRecord capture, PeerMotion distant, in SimSovereignPositionRoute route)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(distant);
        if (!SimPeerWarpPosition.OwnsTeleportPlacement(route))
        {
            throw new ArgumentException(
                "Only a remote teleport/cell-less classification (SetPosition, "
                + "RemoteAuthoritative, Teleport-flagged) may be applied "
                + "through the teleport arm; the caller must select the arm "
                + "with SimPeerWarpPosition.OwnsTeleportPlacement",
                nameof(route));
        }
        return PerformThenPark(capture, route);
    }

    internal SimPeerPlacementExecutionStatus? ImposeApprovedMissileLocus(SimActorRecord capture, in SimSovereignPositionRoute course)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (course.OperationKind is not SimSetPositionOperationKind.ProjectileAuthoritative
            || capture.Projectile is not SimMissile missile
            || capture.KineticBody is not { } corpus
            || !ReferenceEquals(corpus, missile.Body))

            return null;

        bool wasInRealm = corpus.InWorld;
        switch (course.Disposition)
        {
            case SimSovereignPositionVerdict.SetPosition:
                _entityObjects.Physics.ImpactDossiers.ExitRealm(capture);
                break;
            case SimSovereignPositionVerdict.SetPositionSimple:
                if (course.StopInterpolating && capture.PeerMotion is PeerMotion carrier)
                    carrier.Lerp.Clear();
                break;
            default:
                return null;
        }

        missile.DirtyPrediction();
        var condition = PerformThenPark(capture, course);
        if (condition is not SimPeerPlacementExecutionStatus.Deferred and not SimPeerPlacementExecutionStatus.RejectedByPlacement)
            SynchronizeMissileExhibit(capture, missile, corpus, wasInRealm);
        return condition;
    }

    internal void Advance()
    {
        if (_driving || _queued.Count is 0)
            return;
        _driving = true;
        try
        {
            _steerTemp.Clear();
            _steerTemp.AddRange(_queued.Keys);
            var stances = Placements;
            foreach (SimActorKey tag in _steerTemp)
            {
                if (!_queued.TryGetValue(tag, out Pending? queued))
                    continue;
                _queued.Remove(tag);
                if (stances.IsStanceLatest(queued.Token))
                    Reattempt(stances, queued);
            }
        }
        finally
        {
            _driving = false;
        }
    }

    private static void Abandon(SimSetPositionLedger stances, SimActorPlacementTicket[] tickets)
    {
        foreach (SimActorPlacementTicket ticket in tickets)
        {
            stances.DropStanceWrapUp(ticket);
            AbortTicket(stances, ticket);
        }
    }

    // Executes the placement and, when it did not reach the world, parks the body at the accepted pose
    private SimPeerPlacementExecutionStatus PerformThenPark(SimActorRecord capture, in SimSovereignPositionRoute course)
    {
        var condition = TryPerformApprovedDistantLocus(capture, course);
        if (condition.StoresApprovedDest())
            ParkAtApprovedPosture(capture);
        return condition;
    }

    // Puts a projectile's body in or out of the world and the proxy registry to match its spatial
    // status
    private void SynchronizeMissileExhibit(SimActorRecord capture, SimMissile missile, KineticBody corpus, bool wasInRealm)
    {
        if (!_entityObjects.Entities.IsCurrent(capture) || !ReferenceEquals(capture.Projectile, missile) || !ReferenceEquals(capture.KineticBody, corpus))
            return;

        var kinetics = _entityObjects.Physics;
        bool spatial = kinetics.IsSpatialMissile(capture, missile);
        bool concealed = (capture.FinalKineticsCondition & KineticStateFlags.Hidden) != 0;
        uint ownIdent = capture.OwnActorTag ?? 0u;

        if (spatial && !concealed)
        {
            if (!wasInRealm)
            {
                corpus.PreviousRefreshMoment = _clock.SimulationMomentSecs;
                if ((corpus.State & KineticStateFlags.Static) == 0)
                    corpus.TransientState |= TransientPhaseFlagSet.Active;
            }
            corpus.InWorld = true;
            if (kinetics.TryFetchRealmCycleShift(capture.WholeChamberTag, out float shiftX, out float shiftY))
                kinetics.Engine.ShadeObjects.RefreshLocus(ownIdent, corpus.Position, corpus.Orientation, shiftX, shiftY, capture.WholeChamberTag, seedChamberIdent: capture.WholeChamberTag);
            else
                kinetics.HurlIfRealmCycleUnreachable(capture.WholeChamberTag);
        }
        else if (spatial)
        {
            corpus.InWorld = true;
            corpus.PreviousRefreshMoment = _clock.SimulationMomentSecs;
            kinetics.Engine.ShadeObjects.Suspend(ownIdent);
        }
        else
        {
            SynchronizeMissileExhibitBranch(corpus, kinetics, ownIdent);
        }
    }

    private void SynchronizeMissileExhibitBranch(KineticBody corpus, SimKineticsLedger kinetics, uint ownIdent)
    {
        corpus.InWorld = false;
        corpus.TransientState &= ~TransientPhaseFlagSet.Active;
        kinetics.Engine.ShadeObjects.Suspend(ownIdent);
    }

    private static ObjectCreation.RemotePosition? ApprovedPosture(SimActorRecord capture) => capture.Snapshot.Physics?.Position ?? capture.Snapshot.Position;

    private bool ParkAtApprovedPosture(SimActorRecord capture)
    {
        if (!_entityObjects.Entities.IsCurrent(capture) || capture.KineticBody is not { } corpus || ApprovedPosture(capture) is not { } position)
            return false;

        if (!_entityObjects.Physics.TryFetchRealmCycleShift(position.LandblockId, out float shiftX, out float shiftY))
        {
            _entityObjects.Physics.HurlIfRealmCycleUnreachable(position.LandblockId);
            return false;
        }

        corpus.Position = new Vector3(position.PositionX + shiftX, position.PositionY + shiftY, position.PositionZ);
        corpus.Orientation = new Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW);
        return true;
    }

    private void Reattempt(SimSetPositionLedger stances, Pending queued)
    {
        var capture = queued.Record;
        SimMissile? missile = null;
        KineticBody? missileCorpus = null;
        if (queued.Route.OperationKind is SimSetPositionOperationKind.ProjectileAuthoritative
            && capture.Projectile is SimMissile contender
            && capture.KineticBody is { } corpus
            && ReferenceEquals(corpus, contender.Body))
        {
            missile = contender;
            missileCorpus = corpus;
        }
        bool wasInRealm = missileCorpus?.InWorld ?? false;

        SimPeerPlacementExecutionStatus condition;
        if (ApprovedPosture(capture) is not { } approved || !MayAttempt(stances, approved.LandblockId))
        {
            AbortTicket(stances, queued.Token);
            missile?.DirtyPrediction();
            ParkAtApprovedPosture(capture);
            condition = SimPeerPlacementExecutionStatus.Refused;
        }
        else
        {
            condition = Submit(capture, queued.Token, queued.Route);
            if (condition is not SimPeerPlacementExecutionStatus.Contention)
                missile?.DirtyPrediction();
        }

        if (missile is not null && missileCorpus is not null
            && condition is not SimPeerPlacementExecutionStatus.Deferred and not SimPeerPlacementExecutionStatus.RejectedByPlacement)
        {
            SynchronizeMissileExhibit(capture, missile, missileCorpus, wasInRealm);
        }
    }

    private SimPeerPlacementExecutionStatus Submit(SimActorRecord capture, in SimActorPlacementTicket ticket, in SimSovereignPositionRoute course)
    {
        var stances = Placements;
        var lined = stances.TryReadyAndSubmitAuthoredStance(
            capture, ticket, course.OperationKind, course.SetPositionFlags, _link, _clock.SimulationMomentSecs,
            out SimSetPositionUpshot verdict, locateRealmShiftFromCoreCycle: true);

        if (lined != SimSetPositionMoverStagingStatus.Prepared)
        {
            if (lined.IsRetryable())
            {
                _queued[ticket.Entity] = new Pending(capture, ticket, course);
                return SimPeerPlacementExecutionStatus.Contention;
            }
            AbortTicket(stances, ticket);
            return SimPeerPlacementExecutionStatus.RejectedPreparation;
        }

        switch (verdict.Status)
        {
            case SimSetPositionStatus.CommittedHostAcknowledgementPending:
                if (stances.IsStanceLatest(ticket))
                    _expectingAck[ticket.Entity] = ticket;
                return SimPeerPlacementExecutionStatus.Committed;
            case SimSetPositionStatus.DeferredCell:
                AbortTicket(stances, ticket);
                return SimPeerPlacementExecutionStatus.Deferred;
            default:
                AbortTicket(stances, ticket);
                return SimPeerPlacementExecutionStatus.RejectedByPlacement;
        }
    }

    private int OnlineQueuedTally()
    {
        if (_queued.Count is 0)
            return 0;
        if (_entityObjects.Physics.IsDestroyed)
            return _queued.Count;

        var stances = Placements;
        _queuedTemp.Clear();
        foreach ((SimActorKey tag, Pending listing) in _queued)
        {
            if (!stances.IsStanceLatest(listing.Token))
                _queuedTemp.Add(tag);
        }
        foreach (SimActorKey tag in _queuedTemp)
            _queued.Remove(tag);
        return _queued.Count;
    }

    private int OnlineExpectingAckTally()
    {
        if (_expectingAck.Count is 0)
            return 0;
        if (_entityObjects.Physics.IsDestroyed)
            return _expectingAck.Count;

        var stances = Placements;
        _ackTemp.Clear();
        foreach ((SimActorKey tag, SimActorPlacementTicket ticket) in _expectingAck)
        {
            if (!stances.IsStanceLatest(ticket))
                _ackTemp.Add(tag);
        }
        foreach (SimActorKey tag in _ackTemp)
            _expectingAck.Remove(tag);
        return _expectingAck.Count;
    }

    private bool MayAttempt(SimSetPositionLedger stances, uint lbIdent)
    {
        return _window.IsWithinServiceWindow(lbIdent) && !stances.IsImpactStemQuiescing(lbIdent);
    }

    private static void AbortTicket(SimSetPositionLedger stances, in SimActorPlacementTicket ticket)
    {
        var cancel = stances.DropPreciseStance(ticket, revertCancelledPark: true);
        if (cancel.IsValid)
            stances.BroadcastAbort(cancel);
    }
}

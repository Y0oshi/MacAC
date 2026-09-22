using MacAC.Assets;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Presence;

internal sealed class SimDebutPilot
{
    private const int UpperSynchronousHopsPerActor = 16;

    private sealed record PendingDef(SimActorRecord Record, SimSpawnTenancyTicket Token, bool IsLocalPlayer);

    // What one debut step reported, in the terms the pump cares about
    private readonly record struct Step(bool Terminal, bool Completed, bool AwaitingContinuationPlacement);

    private readonly SimActorObjectLifetime _entityObjects;
    private readonly ISimCoreClock _clock;
    private readonly IBakedContactSource _link;
    private readonly Func<AvatarLocomotionAssemblyOptions> _avatarKnobs;
    private readonly Func<SimActorRecord, SimAvatarKineticsArmingStaging> _avatarArming;
    private readonly Dictionary<SimActorKey, PendingDef> _queued = [];
    private readonly List<SimActorKey> _temp = [];
    private bool _pumping;
    private object? _course;
    private Action<SimActorRecord>? _avatarDebuted;
    private long _pumpCalls;

    internal SimDebutPilot(
        SimActorObjectLifetime entityObjects,
        ISimCoreClock clock,
        IBakedContactSource collisionSource,
        Func<AvatarLocomotionAssemblyOptions> localOptions,
        Func<SimActorRecord, SimAvatarKineticsArmingStaging> localActivation)
    {
        _entityObjects = entityObjects ?? throw new ArgumentNullException(nameof(entityObjects));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _link = collisionSource ?? throw new ArgumentNullException(nameof(collisionSource));
        _avatarKnobs = localOptions ?? throw new ArgumentNullException(nameof(localOptions));
        _avatarArming = localActivation ?? throw new ArgumentNullException(nameof(localActivation));
        _entityObjects.AttachStartingResidenceCommenceNotification(OnTenancyBegan);
        _entityObjects.EnrollLeadListingSteerOwnership(() => _queued.Count);
    }

    internal int QueuedTally => _queued.Count;

    internal void FastenCourse(object course, Action<SimActorRecord>? ownAvatarFinished = null)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (_course is not null && !ReferenceEquals(_course, course))
        {
            throw new InvalidOperationException(
                "A first-entry drive controller serves one session route at "
                + "a time; the prior route has to be disposed (session reset "
                + "precedes a new route) prior to a replacement attaches");
        }
        _course = course;
        _avatarDebuted = ownAvatarFinished;
    }

    internal void UnfastenCourse(object course)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (!ReferenceEquals(_course, course))
            return;
        _course = null;
        _avatarDebuted = null;
        _queued.Clear();
    }

    internal void DriveAll()
    {
        if (KineticTelemetry.ProbeParkEnabled && (++_pumpCalls <= 5 || _pumpCalls % 300 is 0))
            Console.WriteLine(FormattableString.Invariant($"[pump] DriveAll #{_pumpCalls} pending={_queued.Count}"));
        if (_pumping || _queued.Count is 0)
            return;

        _pumping = true;
        try
        {
            _temp.Clear();
            _temp.AddRange(_queued.Keys);
            foreach (SimActorKey tag in _temp)
            {
                if (_queued.TryGetValue(tag, out PendingDef? queued))
                    Drive(tag, queued);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private void OnTenancyBegan(SimActorRecord capture)
    {
        if (capture.Key is { } tag && _entityObjects.TryFetchStartingBuildResidence(capture, out SimSpawnTenancyLease tenancy))
            _queued[tag] = new PendingDef(capture, tenancy.Token, tenancy.Route.OperationKind is SimSetPositionOperationKind.InitialLogin);
    }

    private Step Advance(PendingDef queued)
    {
        if (queued.IsLocalPlayer)
        {
            var condition = _entityObjects.OwnAvatarLeadListing.Advance(
                queued.Record, queued.Token, _avatarKnobs(), _avatarArming(queued.Record), _link, _clock.SimulationMomentSecs, feeds: default, out _);
            return new Step(
                condition is SimAvatarDebutStatus.Completed or SimAvatarDebutStatus.RejectedToken or SimAvatarDebutStatus.RejectedAuthority,
                condition is SimAvatarDebutStatus.Completed,
                condition is SimAvatarDebutStatus.AwaitingContinuationPlacement);
        }

        var counterpart = _entityObjects.DistantLeadListing.Advance(
            queued.Record, queued.Token, _link, _clock.SimulationMomentSecs, feeds: default, out _, out _);
        return new Step(
            counterpart is SimPeerDebutStatus.Completed or SimPeerDebutStatus.RejectedToken or SimPeerDebutStatus.RejectedAuthority,
            Completed: false,
            counterpart is SimPeerDebutStatus.AwaitingContinuationPlacement);
    }

    private void Drive(SimActorKey tag, PendingDef queued)
    {
        for (int hop = 0; hop < UpperSynchronousHopsPerActor; ++hop)
        {
            if (queued.Record.Key != tag)
            {
                _queued.Remove(tag);
                return;
            }

            Step outcome = Advance(queued);
            if (outcome.Terminal)
            {
                _queued.Remove(tag);
                if (outcome.Completed)
                    _avatarDebuted?.Invoke(queued.Record);
                return;
            }
            if (!outcome.AwaitingContinuationPlacement)
                return;
            if (!TryDoneContinuationStance(tag, queued.Record, locateRealmShiftFromCoreCycle: !queued.IsLocalPlayer))
                return;
        }
    }

    // Acknowledges this entity's own projections at the FIFO head while they match kinds
    private static bool EmptyOwnFront(SimSetPositionLedger setLocus, SimActorKey tag, bool withdrawalsSole)
    {
        bool any = false;
        while (setLocus.TryGlimpseProj(out SimPlacementMirrorCapture front)
            && front.Token.Entity == tag
            && (front.Kind is SimPlacementMirrorKind.Withdraw || (!withdrawalsSole && front.Kind is SimPlacementMirrorKind.Place)))
        {
            if (!setLocus.AcknowledgeProj(front.Token))
                break;
            any = true;
        }
        return any;
    }

    private bool TryDoneContinuationStance(SimActorKey tag, SimActorRecord capture, bool locateRealmShiftFromCoreCycle)
    {
        var setLocus = _entityObjects.Physics.SetPosition;
        bool acked = EmptyOwnFront(setLocus, tag, withdrawalsSole: false);

        var spawns = _entityObjects.StartingBuildExecution;
        if (!spawns.TryFetchQueuedContinuationStance(tag, out SimActorPlacementTicket stance)
            || !spawns.TryFetchQueuedContinuationCourse(tag, out SimSovereignPositionRoute course))

            return acked;

        var lined = setLocus.TryReadyAndSubmitAuthoredStance(
            capture, stance, course.OperationKind, course.SetPositionFlags, _link, _clock.SimulationMomentSecs,
            out SimSetPositionUpshot verdict, locateRealmShiftFromCoreCycle: locateRealmShiftFromCoreCycle);
        if (lined != SimSetPositionMoverStagingStatus.Prepared)
        {
            return acked;
        }

        switch (verdict.Status)
        {
            case SimSetPositionStatus.CommittedHostAcknowledgementPending:
                _ = setLocus.AcknowledgeProj(verdict.Projection);
                return true;
            case SimSetPositionStatus.DeferredCell:
                return EmptyOwnFront(setLocus, tag, withdrawalsSole: true) || acked;
            default:
                return true;
        }
    }
}

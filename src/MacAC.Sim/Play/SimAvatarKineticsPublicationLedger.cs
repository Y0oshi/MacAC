using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Play;

internal sealed partial class SimAvatarKineticsPublicationLedger : IDisposable
{
    private sealed class Staged
    {
        internal required SimAvatarKineticsPublicationTicket Token
        { get; init; }
        internal required SimActorRecord Record { get; init; }
        internal required SimSetPositionDirective StanceDirective
        { get; init; }
        internal required AvatarLocomotionDriver Driver { get; init; }
        internal required KineticBody Body { get; init; }
        internal required ActorKineticsHarbor PhysicsHost { get; init; }
        internal required LocomotionKeeper Movement { get; init; }
        internal required MotionUnpacker Locomotion { get; init; }
        internal required SimAvatarKineticsArmingStaging
            ActivationPrep
        { get; init; }
        internal required ArmingRun ReadiedActivation { get; init; }
    }

    private sealed class ArmingRun
    {
        internal required SimAvatarKineticsArmingTicket Token
        { get; init; }
        internal required SimActorRecord Record { get; init; }
        internal required SimSetPositionDirective StanceDirective
        { get; init; }
        internal required AvatarLocomotionDriver Driver { get; init; }
        internal required KineticBody Body { get; init; }
        internal required ActorKineticsHarbor PhysicsHost { get; init; }
        internal required LocomotionKeeper Movement { get; init; }
        internal required MotionUnpacker Locomotion { get; init; }
        internal required SimAvatarKineticsArmingStaging
            ActivationPrep
        { get; init; }
        internal SimAvatarKineticsArmingStub Receipt
        { get; set; }
        internal SimIdleSetPositionCommitStub QueuedFinalSeal
        { get; set; }
    }

    private readonly SimActorIndex _actors;

    private readonly SimKineticsLedger _physics;

    private readonly SimAvatarLocomotionLedger _movement;

    private readonly SimAvatarIdentityLedger _identity;

    private Staged? _lined;

    private ArmingRun? _arming;

    private ulong _bulletinIdents;

    private ulong _activationIds;

    private ulong _evaluationIdents;

    private long _armingMisses;

    private bool _destroyed;

    internal SimAvatarKineticsPublicationLedger(
        SimActorIndex entities,
        SimKineticsLedger physics,
        SimAvatarLocomotionLedger movement,
        SimAvatarIdentityLedger identity)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        DiscardLined();
        TossActivation();
        _destroyed = true;
    }

    internal SimAvatarKineticsPublicationStatus Prepare(
        SimActorRecord capture,
        in SimActorPlacementTicket stance,
        in SimSetPositionDirective directive,
        AvatarLocomotionAssemblyOptions knobs,
        in SimAvatarKineticsArmingStaging activationPrep,
        out SimAvatarKineticsPublicationTicket ticket)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(capture);
        ticket = default;
        if (!activationPrep.IsValid
            || !MayJuncture(capture, stance, directive))
            return SimAvatarKineticsPublicationStatus.RejectedAuthority;

        ulong bulletinIdent = checked(_bulletinIdents + 1UL);
        ulong activationIdent = checked(_activationIds + 1UL);

        var readiedActivation =
            activationPrep;

        AvatarLocomotionDriver driver = AvatarLocomotionDriver.BuildBulletinContender(
            _physics.Engine,
            knobs);
        driver.OwnEntityId = capture.Key!.Value.LocalEntityId;
        driver.StepUpHeight = directive.Physics.StepUpHeight;
        driver.StepDownHeight = directive.Physics.StepDownHeight;

        driver.OrbRoster = directive.Physics.Spheres;
        driver.ObjectScaling = directive.Physics.Scale;
        driver.ReadyLocusForSeal(
            directive.Physics.Position,
            directive.Physics.CellId,
            directive.Physics.CellLocalPosition);
        driver.AssignCorpusFacing(directive.Physics.Orientation);
        driver.ImposeKineticsPhase(capture.FinalKineticsCondition);
        KineticBody corpus = driver.KineticBody;
        var kinetics = capture.Snapshot.Physics;
        corpus.Friction = FrictionOrDefault(
            kinetics?.Friction ?? capture.Snapshot.Friction);
        corpus.Elasticity = LimitElasticity(
            kinetics?.Elasticity
                ?? capture.Snapshot.Elasticity
                ?? corpus.Elasticity);
        if (kinetics?.Velocity is { } startingVel)
            corpus.set_velocity(startingVel);
        if (kinetics?.AngularVelocity is { } startingOmega)
            corpus.Omega = startingOmega;
        var travel = driver.Movement;
        var locomotion = driver.Locomotion;
        ActorKineticsHarbor kineticsHub = null!;
        travel.RelocateToMaker = () =>
        {
            MoveToKeeper relocateTo = new MoveToKeeper(
                locomotion,
                stopCompletely: () =>
                    _ = driver.HaltCompletelyAtKineticsObjectBoundary(),
                getPosition: () => new Locus(
                    corpus.CellPosition.ObjCellId,
                    corpus.Position,
                    corpus.Orientation),
                getHeading: () => ApproachMath.FetchBearing(corpus.Orientation),
                setHeading: (bearing, _) => corpus.Orientation =
                    ApproachMath.ApplyBearing(corpus.Orientation, bearing),
                getOwnRadius: () => readiedActivation.Radius,
                getOwnHeight: () => readiedActivation.Height,
                contact: () => corpus.InContact,
                isInterpolating: static () => false,
                getVelocity: () => corpus.Velocity,
                getSelfId: () => capture.ServerGuid,
                setTarget: (ctx, mark, radius, quantum) =>
                    kineticsHub.AssignMark(ctx, mark, radius, quantum),
                clearTarget: () => kineticsHub.WipeMark(),
                getTargetQuantum: () =>
                    kineticsHub.MarkKeeper.FetchMarkQuantum(),
                setTargetQuantum: quantum =>
                    kineticsHub.MarkKeeper.AssignMarkQuantum(quantum),
                curMoment: () => driver.SimMomentSecs);
            relocateTo.StickTo = (mark, radius, height) =>
                kineticsHub.LocusKeeper.StickTo(mark, radius, height);
            relocateTo.Unstick = kineticsHub.LocusKeeper.UnStick;
            return relocateTo;
        };
        kineticsHub = new ActorKineticsHarbor(
            capture.ServerGuid,
            fetchLocus: () => new Locus(
                corpus.CellPosition.ObjCellId,
                corpus.Position,
                corpus.Orientation),
            fetchVel: () => corpus.Velocity,
            fetchRadius: () => readiedActivation.Radius,
            inLink: () => corpus.InContact,
            minterpUpperPace: () => locomotion.FetchAdjustedUpperPace(),
            curMoment: () => driver.SimMomentSecs,
            kineticsTickerMoment: () => driver.SimMomentSecs,
            fetchObjectA: _physics.LocateObjectChartHub,
            hndRefreshMark: details =>
            {
                travel.ProcessRefreshMark(details);
            },
            interruptLatestTravel: () =>
            {
                travel.CancelMoveTo(WeenieProblem.ActionCancelled);
            });
        travel.CraftRelocateToKeeper();
        locomotion.UnstickFromObject = kineticsHub.LocusKeeper.UnStick;
        locomotion.InterruptCurrentMovement = () =>
        {
            travel.CancelMoveTo(WeenieProblem.ActionCancelled);
        };
        driver.PlaceKeeper = kineticsHub.LocusKeeper;
        corpus.InWorld = false;
        corpus.TransientState &= ~TransientPhaseFlagSet.Active;
        driver.CloseBulletinContender();

        if (!MayJuncture(capture, stance, directive))
        {
            driver.TossCoreContender();
            return SimAvatarKineticsPublicationStatus.RejectedAuthority;
        }

        DiscardLined();
        _bulletinIdents = bulletinIdent;
        _activationIds = activationIdent;
        ticket = new SimAvatarKineticsPublicationTicket(
            capture.Key.Value,
            stance,
            bulletinIdent,
            _identity.ServerGuid,
            _identity.Revision,
            capture.KineticsOwnershipEpoch,
            capture.ObjectTimerEpoch,
            _movement.DriverOwnershipEpoch,
            _actors.SessionLifetimeVersion);
        ulong anticipatedDriverEpoch = checked(
            _movement.DriverOwnershipEpoch + 1UL);
        ArmingRun activationEnvelope = new ArmingRun
        {
            Token = new SimAvatarKineticsArmingTicket(
                ticket.Entity,
                ticket.Placement,
                activationIdent,
                ticket.LocalPlayerServerGuid,
                ticket.LocalPlayerIdentityRevision,
                checked(ticket.PhysicsOwnershipEpoch + 1UL),
                ticket.ObjectClockEpoch,
                anticipatedDriverEpoch,
                ticket.SessionGenerationAuthority),
            Record = capture,
            StanceDirective = directive,
            Driver = driver,
            Body = corpus,
            PhysicsHost = kineticsHub,
            Movement = travel,
            Locomotion = locomotion,
            ActivationPrep = readiedActivation,
        };
        _lined = new Staged
        {
            Token = ticket,
            Record = capture,
            StanceDirective = directive,
            Driver = driver,
            Body = corpus,
            PhysicsHost = kineticsHub,
            Movement = travel,
            Locomotion = locomotion,
            ActivationPrep = readiedActivation,
            ReadiedActivation = activationEnvelope,
        };
        return SimAvatarKineticsPublicationStatus.Prepared;
    }

    internal SimAvatarKineticsPublicationStatus Seal(
        in SimAvatarKineticsPublicationTicket ticket) =>
        Seal(ticket, out _);

    internal SimAvatarKineticsPublicationStatus Seal(
        in SimAvatarKineticsPublicationTicket ticket,
        out SimAvatarKineticsArmingTicket activationTicket)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        activationTicket = default;
        if (!ticket.IsValid
            || _lined is not { } lined
            || lined.Token != ticket)

            return SimAvatarKineticsPublicationStatus.RejectedToken;
        if (!StillLatest(lined))
        {
            DiscardLined();
            return SimAvatarKineticsPublicationStatus.RejectedAuthority;
        }

        _physics.SetPosition.ReadyDormantOwnActivationOwnership(
            lined.Record,
            lined.Body,
            lined.ReadiedActivation.Token.Placement);

        lined.Driver.SealCoreOwnership(
            lined.Record.ObjectClock);
        lined.Record.AssignKineticsCorpus(lined.Body);
        _movement.SealCorePossessedDriver(lined.Driver);
        activationTicket = lined.ReadiedActivation.Token;
        _arming = lined.ReadiedActivation;
        _lined = null;
        return SimAvatarKineticsPublicationStatus.Committed;
    }

    internal SimAvatarKineticsPublicationStatus Toss(
        in SimAvatarKineticsPublicationTicket ticket)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!ticket.IsValid
            || _lined is not { } lined
            || lined.Token != ticket)

            return SimAvatarKineticsPublicationStatus.RejectedToken;
        DiscardLined();
        return SimAvatarKineticsPublicationStatus.Discarded;
    }

    internal bool TryGrabContenderCapture(
        in SimAvatarKineticsPublicationTicket ticket,
        out SimAvatarKineticsCandidateCapture capture)
    {
        if (!_destroyed
            && ticket.IsValid
            && _lined is { } lined
            && lined.Token == ticket)
        {
            KineticBody corpus = lined.Body;
            capture = new SimAvatarKineticsCandidateCapture(
                corpus.Position,
                corpus.Orientation,
                corpus.CellPosition.ObjCellId,
                corpus.CellPosition.Frame.Origin,
                corpus.State,
                corpus.TransientState,
                corpus.InWorld);
            return true;
        }
        capture = default;
        return false;
    }

    internal SimAvatarKineticsPublicationHoldingCapture GrabOwnership()
    {
        return new(IsBound: true, _destroyed, _lined is null ? 0 : 1, _arming is null ? 0 : 1, _bulletinIdents);
    }

    internal void RestartSess()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        DiscardLined();
        TossActivation();
    }

    internal long ActivationRelayMissTally =>
        _armingMisses;

    private static float FrictionOrDefault(float? val)
    {
        return val is >= 0f and <= 1f && float.IsFinite(val.Value)
            ? val.Value
            : KineticBody.DefaultFriction;
    }

    private static float LimitElasticity(float val) => float.IsNaN(val) || val <= 0f ? 0f : MathF.Min(val, 0.1f);

    private bool MayJuncture(
        SimActorRecord capture,
        in SimActorPlacementTicket stance,
        in SimSetPositionDirective directive)
    {
        return _arming is null
        && capture.Key is { } tag
        && tag == stance.Entity
        && _actors.IsCurrent(capture)
        && !capture.EraseApprovedForTeardown
        && directive.Kind is SimSetPositionOperationKind.InitialLogin
            or SimSetPositionOperationKind.LocalAuthoritative
        && directive.Physics.MovingEntityId == tag.LocalEntityId
        && !_identity.IsDisposed
        && _identity.ServerGuid is not 0u
        && _identity.ServerGuid == capture.ServerGuid
        && capture.KineticBody is null
        && _movement.Controller is null
        && capture.PhysicsHost is null
        && capture.PeerMotion is null
        && capture.Projectile is null
        && !capture.KineticsCorpusAcquisitionInHeadway
        && !capture.DistantLocomotionMappingInHeadway
        && !capture.MissileMappingInHeadway
        && !capture.RequiresDistantStanceCore
        && _physics.SetPosition.IsPreciseReadiedStanceLatest(
            capture,
            stance,
            directive);
    }

    private bool StillLatest(Staged lined)
    {
        return _arming is null
        && lined.Driver.IsSealedBulletinContender
        && lined.Driver.OwnsKineticsCorpus(lined.Body)
        && _actors.SessionLifetimeVersion
            == lined.Token.SessionGenerationAuthority
        && _actors.IsCurrent(lined.Record)
        && lined.Record.Key == lined.Token.Entity
        && !_identity.IsDisposed
        && _identity.ServerGuid == lined.Token.LocalPlayerServerGuid
        && _identity.ServerGuid == lined.Record.ServerGuid
        && _identity.Revision == lined.Token.LocalPlayerIdentityRevision
        && lined.Record.KineticsOwnershipEpoch
            == lined.Token.PhysicsOwnershipEpoch
        && lined.Record.ObjectTimerEpoch
            == lined.Token.ObjectClockEpoch
        && _movement.CanSealCorePossessedDriver(
            lined.Token.ControllerOwnershipEpoch,
            anticipatedDriver: null)
        && lined.Record.KineticBody is null
        && lined.Record.PhysicsHost is null
        && lined.Record.PeerMotion is null
        && lined.Record.Projectile is null
        && !lined.Record.KineticsCorpusAcquisitionInHeadway
        && !lined.Record.DistantLocomotionMappingInHeadway
        && !lined.Record.MissileMappingInHeadway
        && !lined.Record.RequiresDistantStanceCore
        && !lined.Record.EraseApprovedForTeardown
        && _physics.SetPosition.IsPreciseReadiedStanceLatest(
            lined.Record,
            lined.Token.Placement,
            lined.StanceDirective);
    }

    private void DiscardLined()
    {
        Staged? lined = _lined;
        _lined = null;
        lined?.Driver.TossCoreContender();
    }
}

using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

public sealed partial class AvatarLocomotionDriver
{
    private readonly LocomotionKeeper _gait;
    private ImmutableArray<PackedContactSphere> _orbs;

    public ImmutableArray<PackedContactSphere> OrbRoster
    {
        get => _orbs;
        set
        {
            DemandMutable();
            _orbs = value;
        }
    }

    private readonly KineticEngine _engine;

    private readonly KineticBody _hull;

    private readonly MotionUnpacker _unpacker;

    private readonly PlayerActor _actor;

    private float _hopUp = 0.4f;

    private float _hopDown = 0.4f;

    private MoverState _pvpFlagSet = MoverState.None;

    private float _scaling = 1f;

    private AvatarPhase _stage = AvatarPhase.InWorld;

    private uint _actorIdent;

    private PositionKeeper? _locusKeeper;

    public Action<string, CanonLogTextType>? OnInterfaceWording { get; set; }

    public float StepUpHeight
    {
        get => _hopUp;
        set
        {
            DemandMutable();
            _hopUp = value;
        }
    }

    public float StepDownHeight
    {
        get => _hopDown;
        set
        {
            DemandMutable();
            _hopDown = value;
        }
    }

    public MoverState OwnPvpFlagSet
    {
        get => _pvpFlagSet;
        set
        {
            DemandMutable();
            _pvpFlagSet = value;
        }
    }

    public float ObjectScaling
    {
        get => _scaling;
        set
        {
            DemandMutable();
            _scaling = value;
        }
    }

    public AvatarPhase State
    {
        get => _stage;
        set
        {
            DemandPublished();
            _stage = value;
        }
    }

    public float Yaw
    {
        get => ApproachMath.YawFromBearing(
            ApproachMath.FetchBearing(_hull.Orientation));
        set
        {
            DemandPublished();
            float wrapped = value;
            while (wrapped > MathF.PI) wrapped -= 2f * MathF.PI;
            while (wrapped < -MathF.PI) wrapped += 2f * MathF.PI;
            _hull.Orientation = ApproachMath.ApplyBearing(
                _hull.Orientation,
                ApproachMath.BearingFromYaw(wrapped));
        }
    }

    public Vector3 Position => _hull.Position;

    public Vector3 RasterizeLocus => PaintSpot();

    public uint CellId { get; private set; }

    public Locus CellPosition => _hull.CellPosition;

    internal Locus LatestChamberLocus
    {
        get
        {
            Locus carried = _hull.CellPosition;
            return new Locus(
                carried.ObjCellId,
                carried.Frame.Origin,
                _hull.Orientation);
        }
    }

    public uint OwnEntityId
    {
        get => _actorIdent;
        set
        {
            if (value == _actorIdent)
                return;
            DemandMutable();
            _actorIdent = value;
        }
    }

    public LocomotionKeeper Movement
    {
        get
        {
            DemandMutable();
            return _gait;
        }
    }

    public MoveToKeeper? MoveTo
    {
        get => Movement.MoveTo;
        set
        {
            DemandMutable();
            var mtm = value ?? throw new ArgumentNullException(nameof(value));
            Movement.RelocateToMaker = () => mtm;
            Movement.CraftRelocateToKeeper();
        }
    }

    public PositionKeeper? PlaceKeeper
    {
        get
        {
            DemandMutable();
            return _locusKeeper;
        }
        set
        {
            DemandMutable();
            _locusKeeper = value;
        }
    }

    public AvatarLocomotionDriver(
        KineticEngine kinetics,
        CanonQuantumClock? objectTimer = null)
        : this(
            kinetics,
            objectTimer,
            AvatarLocomotionAssemblyOptions.Backup,
            AvatarLocomotionDriverPublicationLifespan.StandalonePublished)
    {
    }

    public AvatarLocomotionDriver(
        KineticEngine kinetics,
        CanonQuantumClock? objectTimer,
        AvatarLocomotionAssemblyOptions knobs)
        : this(
            kinetics,
            objectTimer,
            knobs,
            AvatarLocomotionDriverPublicationLifespan.StandalonePublished)
    {
    }

    private AvatarLocomotionDriver(
        KineticEngine kinetics,
        CanonQuantumClock? objectTimer,
        AvatarLocomotionAssemblyOptions knobs,
        AvatarLocomotionDriverPublicationLifespan bulletinLifecycle)
    {
        _engine = kinetics;
        _quantumTimer = objectTimer ?? new CanonQuantumClock();
        _lifespan = bulletinLifecycle;

        _hull = new KineticBody
        {
            State = KineticStateFlags.Gravity | KineticStateFlags.ReportCollisions,
        };

        _actor = new PlayerActor(
            execAptitude: knobs.RunSkill,
            leapAptitude: knobs.JumpSkill);
        _unpacker = new MotionUnpacker(_hull, _actor);
        _gait = new LocomotionKeeper(_unpacker);
        _gait.EngageKineticsObject = GoOnlineFromTravel;
        _hull.PreviousRelocateWasAutonomous = true;
    }

    internal static AvatarLocomotionDriver BuildBulletinContender(
        KineticEngine kinetics,
        AvatarLocomotionAssemblyOptions knobs) => new(
            kinetics,
            new CanonQuantumClock(),
            knobs,
            AvatarLocomotionDriverPublicationLifespan.CandidatePreparing);

    internal KineticBody KineticBody
    {
        get
        {
            DemandMutable();
            return _hull;
        }
    }

    internal bool OwnsKineticsCorpus(KineticBody corpus) =>
        ReferenceEquals(_hull, corpus);

    internal bool IsSealedBulletinContender => _lifespan
        is AvatarLocomotionDriverPublicationLifespan.CandidateSealed;

    internal bool IsCorePossessedDormant => _lifespan
        is AvatarLocomotionDriverPublicationLifespan.RuntimeOwnedDormant;

    internal bool IsRuntimePublished => _lifespan
        is AvatarLocomotionDriverPublicationLifespan.RuntimePublished;

    public bool CanPerformOnlineTravel => _lifespan
        is AvatarLocomotionDriverPublicationLifespan.StandalonePublished
            or AvatarLocomotionDriverPublicationLifespan.RuntimePublished;

    private bool _idleTerrainStage;

    internal void CommenceDormantSetLocusTerrainStage()
    {
        if (!IsCorePossessedDormant || _idleTerrainStage)
            throw new InvalidOperationException(
                "Dormant SetPosition ground phase needs one dormant Runtime owner");
        _idleTerrainStage = true;
    }

    internal void FinishDormantSetLocusTerrainStage()
    {
        if (!_idleTerrainStage)
            throw new InvalidOperationException(
                "Dormant SetPosition ground phase isn't active");
        _hull.TransientState &= ~TransientPhaseFlagSet.Active;
        _idleTerrainStage = false;
    }

    internal bool IsDormantSetLocusTerrainStageEngaged =>
        _idleTerrainStage;

    internal void RenewDormantCoreKineticsPhase(
        KineticStateFlags phase,
        bool recalculateAcceleration)
    {
        if (!IsCorePossessedDormant || _idleTerrainStage)
            throw new InvalidOperationException(
                "Only an idle dormant Runtime owner can refresh physics state");
        _hull.State = phase;
        if (recalculateAcceleration)
            _hull.calc_acceleration();
    }

    internal void RenewDormantCoreVector(
        Vector3? vel,
        Vector3? omega)
    {
        if (!IsCorePossessedDormant || _idleTerrainStage)
            throw new InvalidOperationException(
                "Only an idle dormant Runtime owner can refresh vector state");
        if (vel is { } onlineVel)
            _hull.set_velocity(onlineVel);
        if (omega is { } onlineOmega)
            _hull.Omega = onlineOmega;
        _hull.TransientState &= ~TransientPhaseFlagSet.Active;
    }

    internal void CloseBulletinContender()
    {
        if (_lifespan
            is not AvatarLocomotionDriverPublicationLifespan
                .CandidatePreparing)
        {
            throw new InvalidOperationException(
                "Only a preparing Runtime movement candidate can be sealed");
        }
        _lifespan = AvatarLocomotionDriverPublicationLifespan
            .CandidateSealed;
    }

    internal void SealCoreOwnership(CanonQuantumClock objectTimer)
    {
        ArgumentNullException.ThrowIfNull(objectTimer);
        if (_lifespan
            is not AvatarLocomotionDriverPublicationLifespan.CandidateSealed)
        {
            throw new InvalidOperationException(
                "Only a sealed Runtime movement candidate can be published");
        }
        _quantumTimer = objectTimer;
        _lifespan = AvatarLocomotionDriverPublicationLifespan
            .RuntimeOwnedDormant;
    }

    internal void EngageCoreBulletin()
    {
        if (_lifespan
            is not AvatarLocomotionDriverPublicationLifespan
                .RuntimeOwnedDormant
            || _idleTerrainStage)
        {
            throw new InvalidOperationException(
                "Only a dormant Runtime-owned movement controller can be activated");
        }
        _lifespan = AvatarLocomotionDriverPublicationLifespan
            .RuntimePublished;
    }

    internal void SealCoreActivationCycle()
    {
        if (_lifespan
            is not AvatarLocomotionDriverPublicationLifespan
                .RuntimeOwnedDormant
            || _idleTerrainStage)
        {
            throw new InvalidOperationException(
                "Only a dormant Runtime-owned movement controller can accept its activation frame");
        }

        _hullSpotPrior = _hull.Position;
        _hullSpotInstant = _hull.Position;
        CellId = _hull.CellPosition.ObjCellId;
    }

    internal void TossCoreContender()
    {
        if (_lifespan
            is AvatarLocomotionDriverPublicationLifespan.CandidatePreparing
                or AvatarLocomotionDriverPublicationLifespan.CandidateSealed)
        {
            _lifespan = AvatarLocomotionDriverPublicationLifespan
                .Discarded;
        }
    }

    internal void RetireCoreBulletin()
    {
        if (_lifespan
            is AvatarLocomotionDriverPublicationLifespan.RuntimeOwnedDormant
                or AvatarLocomotionDriverPublicationLifespan.RuntimePublished)
        {
            _lifespan = AvatarLocomotionDriverPublicationLifespan
                .RuntimeRetired;
        }
    }

    private void DemandMutable()
    {
        if (_lifespan
            is AvatarLocomotionDriverPublicationLifespan.StandalonePublished
                or AvatarLocomotionDriverPublicationLifespan
                    .CandidatePreparing
                or AvatarLocomotionDriverPublicationLifespan.RuntimePublished
            || IsCorePossessedDormant && _idleTerrainStage)
        {
            return;
        }
        throw new InvalidOperationException(
            "A sealed, retired, or discarded Runtime movement controller can't be mutated");
    }

    private void DemandPublished()
    {
        if (_lifespan
            is AvatarLocomotionDriverPublicationLifespan.StandalonePublished
                or AvatarLocomotionDriverPublicationLifespan.RuntimePublished)
        {
            return;
        }
        throw new InvalidOperationException(
            "An unpublished or retired Runtime movement controller can't execute live movement operations");
    }

    private void GoOnlineFromTravel()
    {
        DemandMutable();
        if ((_hull.State & KineticStateFlags.Static) != 0)
            return;

        if (IsCorePossessedDormant && _idleTerrainStage)
            return;

        _quantumTimer.Engage();
        _hull.TransientState |= TransientPhaseFlagSet.Active;
    }

    private CanonQuantumClock _quantumTimer;

    private AvatarLocomotionDriverPublicationLifespan _lifespan;

    internal MotionUnpacker Locomotion
    {
        get
        {
            DemandMutable();
            return _unpacker;
        }
    }
}

using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Kinetics;

internal sealed class PeerMotion : ISimPeerPlacement, ISimCanonicalKineticsConsumer
{

    public KineticBody Body { get; }
    KineticBody ISimPeerMotion.Body => Body;

    public LocomotionKeeper Movement;
    public MotionTableDispatchTap? Sink;

    public MotionUnpacker Motion => Movement.Minterp;
    public MoveToKeeper? MoveTo => Movement.MoveTo;

    public LerpKeeper Lerp { get; } = new();
    public RemoteMotionMerger Position { get; } = new();
    public MotionDeltaPose LocusKeeperDiffTemp { get; } = new();

    public bool Airborne;

    // Last UpdatePosition timestamp - drives body.update_object sub-stepping
    public double PreviousSrvSpotMoment;
    // Last known server position - kept for diagnostics / HUD
    public Vector3 PreviousSrvSpot;
    public Vector3 EarlierSrvSpot;
    public double EarlierSrvSpotMoment;
    public Vector3 SrvVel;
    public bool HasSrvVel;
    public Vector3 PreviousShadeSynchronizeSpot;
    public Quaternion LastShadowSyncOrientation;
    public double PreviousOmegaDiagTraceMoment;
    public float UpperTrunkLocomotionPaceSincePreviousUP;

    public bool SrvRelocateToEngaged;
    public bool HasRelocateToDest;
    public Vector3 RelocateToDestRealm;
    public float RelocateToLowerGap;
    public float RelocateToGapToObject;
    public bool RelocateToRelocateTowards;
    public double PreviousRelocateToPacketMoment;

    internal SimActorPlacementTicket InterpolationRecoveryStance;
    internal InterpolationRecoveryMark InterpolationRecoveryMark;

    private Func<IKineticObjHost?>? _hubReader;
    private bool _hubFullyTied;
    private uint _chamberIdent;
    private Func<uint>? _chamberReader;
    private Action<uint>? _chamberWriter;

    public PeerMotion(KineticBody? sharedCorpus = null)
    {
        Body = sharedCorpus ?? new KineticBody
        {
            State = KineticStateFlags.ReportCollisions,
            TransientState = TransientPhaseFlagSet.Contact | TransientPhaseFlagSet.OnWalkable | TransientPhaseFlagSet.Active,
            InWorld = true,
        };
        Movement = new LocomotionKeeper(new MotionUnpacker(Body) { WeenieObjRef = new RemoteActor() });
    }

    // The full physics host, once it has been marked bound; null before that
    public ActorKineticsHarbor? Host => _hubFullyTied ? _hubReader?.Invoke() as ActorKineticsHarbor : null;

    // The canonical cell when bound, else the locally tracked one; writes go to both
    public uint CellId
    {
        get => _chamberReader?.Invoke() ?? _chamberIdent;
        set
        {
            _chamberIdent = value;
            _chamberWriter?.Invoke(value);
        }
    }

    public void AttachCanonCore(Func<IKineticObjHost?> scanKineticsHub, Func<uint> scanChamber, Action<uint> emitChamber)
    {
        ArgumentNullException.ThrowIfNull(scanKineticsHub);
        ArgumentNullException.ThrowIfNull(scanChamber);
        ArgumentNullException.ThrowIfNull(emitChamber);
        if (_hubReader is not null || _chamberReader is not null || _chamberWriter is not null)
            throw new InvalidOperationException("The remote canonical runtime context is by now bound");
        _hubReader = scanKineticsHub;
        _chamberReader = scanChamber;
        _chamberWriter = emitChamber;
    }

    public void AttachCanonChamber(Func<uint> scan, Action<uint> emit)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(emit);
        if (_chamberReader is not null || _chamberWriter is not null)
            throw new InvalidOperationException("The remote canonical cell source is by now bound");
        _chamberReader = scan;
        _chamberWriter = emit;
    }

    public void FlagWholeKineticsHubTied()
    {
        if (_hubReader is null)
            throw new InvalidOperationException("The canonical physics-host source isn't bound");
        _hubFullyTied = true;
    }

    Vector3 ISimPeerPlacement.LastServerPosition { get => PreviousSrvSpot; set => PreviousSrvSpot = value; }
    double ISimPeerPlacement.LastServerPositionTime { get => PreviousSrvSpotMoment; set => PreviousSrvSpotMoment = value; }
    Vector3 ISimPeerPlacement.LastShadowSyncPosition { get => PreviousShadeSynchronizeSpot; set => PreviousShadeSynchronizeSpot = value; }
    Quaternion ISimPeerPlacement.LastShadowSyncOrientation { get => LastShadowSyncOrientation; set => LastShadowSyncOrientation = value; }
    bool ISimPeerPlacement.Airborne { get => Airborne; set => Airborne = value; }
    void ISimPeerPlacement.HitGround() => Movement.HitGround();
    void ISimPeerPlacement.LeaveGround() => Motion.LeaveGround();
}

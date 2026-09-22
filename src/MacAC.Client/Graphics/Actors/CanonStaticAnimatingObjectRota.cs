using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonStaticAnimatingObjectRota(
    IAnimReader animationLoader,
    Action<uint, AnimSequencer> captureHooks,
    Action<RealmActor, IReadOnlyList<Matrix4x4>, IReadOnlyList<bool>> publishPartPoses,
    Func<RealmActor, bool>? isHoused = null,
    Action<RealmActor, KineticBody>? sealOnlineTrunk = null,
    Func<RealmActor, ulong>? residencyVer = null,
    Func<RealmActor,
            (OnlineActorMotionLedger Animation, KineticBody Body)?>?
            locateOnlineHolder = null) : IOnlineStaticPartFrameSource
{
    private const double CycleEpsilon = 0.000199999995;

    private const float CeilingPassed = 2f;

    private const double DatStaticOmegaReferenceHz = 30d;

    private sealed class ClientOwner
    {
        public required RealmActor Entity;
        public required RigSpec Setup;
        public required AnimSequencer? Sequencer;
        public required uint[] PieceGfxIdents;
        public required IReadOnlyDictionary<uint, uint>?[] CanvasSubstitutions;
        public required bool[] PieceOnHand;
        public KineticBody? Body;

        public Vector3 Omega;
        public AnimSequencer? QueuedProcTaps;
        public ulong QueuedResidencyVer;
        public readonly List<PieceTransform> ReadiedOnlinePieceCycles = [];
        public bool HasReadiedOnlinePieceCycles;
        public AnimSequencer? ReadiedCycleScheduler;
        public ulong ReadiedExhibitRev;
        public OnlineActorMotionLedger? OnlineAnim;
        public double PassedSinceRefresh;
        public readonly Pose TrunkCycleTemp = new();
        public readonly List<TriMeshRef> MeshRefs = [];
        public readonly List<Matrix4x4> PiecePostures = [];
        public readonly List<Matrix4x4> VisualPiecePostures = [];
    }

    private readonly IAnimReader _animFetcher = animationLoader
            ?? throw new ArgumentNullException(nameof(animationLoader));

    private readonly Action<uint, AnimSequencer> _grabTaps = captureHooks
            ?? throw new ArgumentNullException(nameof(captureHooks));

    private readonly Action<RealmActor, IReadOnlyList<Matrix4x4>, IReadOnlyList<bool>>
        _broadcastPiecePostures = publishPartPoses
            ?? throw new ArgumentNullException(nameof(publishPartPoses));

    private readonly Func<RealmActor, bool> _isHoused = isHoused ?? (_ => true);

    private readonly Action<RealmActor, KineticBody> _sealOnlineTrunk = sealOnlineTrunk ?? ((_, _) => { });

    private readonly Func<RealmActor, ulong> _residencyVer = residencyVer ?? (_ => 0UL);

    private readonly Func<RealmActor,
        (OnlineActorMotionLedger Animation, KineticBody Body)?>?
        _locateOnlineHolder = locateOnlineHolder;

    private readonly Dictionary<uint, ClientOwner> _holders = [];

    private readonly List<ClientOwner> _capture = [];

    private readonly List<ClientOwner> _tapCapture = [];
}

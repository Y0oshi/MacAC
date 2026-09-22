using MacAC.Dat;
using MacAC.Client.Controls;
using MacAC.Client.Kinetics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics;

internal sealed partial class OnlineActorMotionRota(
    OnlineActorCore liveEntities,
    IAvatarIdentitySource localPlayer,
    RemotePhysicsUpdater remotePhysics,
    OnlineActorOrdinaryKineticsPulser ordinaryPhysics,
    MissileDriver projectiles,
    IActorRootPoseHerald rootPoses,
    IMotionHookCaptureSink animationHooks)
{
    private readonly OnlineActorCore _onlineActors = liveEntities
            ?? throw new ArgumentNullException(nameof(liveEntities));

    private readonly IAvatarIdentitySource _ownAvatar = localPlayer
            ?? throw new ArgumentNullException(nameof(localPlayer));

    private readonly RemotePhysicsUpdater _distantKinetics = remotePhysics
            ?? throw new ArgumentNullException(nameof(remotePhysics));

    private readonly OnlineActorOrdinaryKineticsPulser _plainKinetics = ordinaryPhysics
            ?? throw new ArgumentNullException(nameof(ordinaryPhysics));

    private readonly MissileDriver _missiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));

    private readonly IActorRootPoseHerald _rootPoses = rootPoses ?? throw new ArgumentNullException(nameof(rootPoses));

    private readonly IMotionHookCaptureSink _animTaps = animationHooks ?? throw new ArgumentNullException(nameof(animationHooks));

    private readonly List<OnlineActorRecord> _trunkCapture = [];

    private readonly Dictionary<SimActorKey, OnlineActorMotionSchedule>
        _schedules = [];

    private readonly Pose _trunkCycleTemp = new();

    private readonly MotionDeltaPose _trunkDiffTemp = new();
}

internal readonly record struct OnlineActorMotionSchedule(
    IReadOnlyList<PieceTransform>? SequenceFrames,
    float LegacyAdvanceSeconds,
    bool ComposeParts,
    OnlineActorRecord? Record = null,
    RealmActor? Entity = null,
    OnlineActorMotionLedger? Animation = null,
    ulong ObjectClockEpoch = 0UL,
    ulong ProjectionMutationVersion = 0UL,
    ulong PresentationRevision = 0UL);

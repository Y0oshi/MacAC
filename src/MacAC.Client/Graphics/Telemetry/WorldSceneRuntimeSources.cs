using System.Numerics;
using MacAC.Client.Paging;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IRealmStageHeavensStateSource
{
    DayGroupRow? EngagedDayGroup { get; }

    int EngagedDayClusterOrdinal => 0;

    float DayFraction { get; }
}

internal interface IRealmStageActorSource
{
    IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> LbEntries
    { get; }

    IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)> LbLimits { get; }

    ResidentPagingWindowFact GrabHousedPagingPane(
        int middleX,
        int middleY) => ResidentPagingWindowFact.Unavailable(rev: 0);
}

internal sealed class EngineRealmStageActorSource(GpuRealmPhase world) : IRealmStageActorSource
{
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));

    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> LbEntries =>
        _world.LandblockListings;

    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)> LbLimits =>
        _world.LbBounds;

    public ResidentPagingWindowFact GrabHousedPagingPane(
        int middleX,
        int middleY) =>
        _world.GrabHousedPagingPane(middleX, middleY);
}

internal interface IRealmStagePViewTelemetrySource
{
    Vector3 RawAvatarLocusOr(Vector3 backup);

    float AvatarYaw { get; }

    float? ProbeLandZ(float realmX, float realmY);

    CameraChamberResolution CameraChamberResolution { get; }
}

internal sealed class EngineRealmStagePViewTelemetrySource(
    ISimAvatarDriverSource player,
    KineticEngine physics,
    ChamberVis visibility) :
    IRealmStagePViewTelemetrySource
{
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly ChamberVis _vis = visibility ?? throw new ArgumentNullException(nameof(visibility));

    public Vector3 RawAvatarLocusOr(Vector3 backup) =>
        _avatar.Controller?.Position ?? backup;

    public float AvatarYaw => _avatar.Controller?.Yaw ?? 0f;

    public float? ProbeLandZ(float realmX, float realmY) =>
        _physics.TasteLandZ(realmX, realmY);

    public CameraChamberResolution CameraChamberResolution =>
        _vis.PreviousCamChamberResolution;
}

internal interface IRealmStageDebugStateSource
{
    bool ImpactWireframesVisible { get; }
}

internal sealed class RealmTableauDiagPhase : IRealmStageDebugStateSource
{
    public bool ImpactWireframesVisible { get; private set; }

    public bool FlipImpactWireframes()
    {
        ImpactWireframesVisible = !ImpactWireframesVisible;
        return ImpactWireframesVisible;
    }
}

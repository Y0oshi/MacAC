using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Kinetics;

internal interface IProjectileSetupPicker
{
    RigSpec? Resolve(uint rigIdent);
}

internal sealed class DatProjectileSetupPicker(IDatAccess dats, object datLock) : IProjectileSetupPicker
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));

    public RigSpec? Resolve(uint rigIdent)
    {
        lock (_datMutex)
            return _datFiles.Get<RigSpec>(rigIdent);
    }
}

internal sealed partial class MissileDriver
{
    internal readonly record struct QuantumHop(
        OnlineActorRecord Record,
        RealmActor Entity,
        SimMissileKineticsCommit RuntimeCommit);

    private readonly OnlineActorCore _onlineActors;

    private readonly SimMissileKineticsStepper _runtimeUpdater;

    private readonly ProxyRegistry _shades;

    private readonly IProjectileSetupPicker? _setupResolver;

    private readonly IActorRootPoseHerald? _rootPoses;

    private readonly OnlineRealmOriginLedger? _origin;

    private readonly List<OnlineActorRecord> _spatialMissileCapture = [];

    private double _previousFinitePlayMoment;

    internal MissileDriver(
        OnlineActorCore liveEntities,
        IProjectileSetupPicker? rigLocator = null,
        IActorRootPoseHerald? trunkPostures = null,
        OnlineRealmOriginLedger? origin = null)
    {
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _runtimeUpdater = new SimMissileKineticsStepper(
            liveEntities.Physics);
        _shades = liveEntities.Physics.Engine.ShadeObjects;
        _setupResolver = rigLocator;
        _rootPoses = trunkPostures;
        _origin = origin;
        _onlineActors.ProjectionVisibilityChanged += OnProjVisAltered;
    }
}

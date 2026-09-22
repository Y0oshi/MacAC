using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Effects;

public sealed partial class ActorEffectDriver : IAnimHookTap,
    IActorEffectAdvanceSource
{
    private readonly OnlineActorCore _onlineActors;

    private readonly KineticScriptRunner _runner;

    private readonly KineticScriptTableLookup _charts;

    private readonly ActorEffectPoseRegistry _postures;

    private readonly Func<uint, uint, uint?> _descendantAtPiece;

    private readonly Func<uint, uint?> _ancestorOfAffixedDescendant;

    private readonly Action<uint> _holderUnregistered;

    private readonly Action<uint, uint?> _holderSfxChartAltered;

    private readonly Action<uint, Vector3, uint, float> _playSrvSfx;

    private readonly Dictionary<SimActorKey, ActorEffectProfile> _liveProfiles = [];

    private readonly HashSet<SimActorKey> _readyLiveOwners = [];

    private readonly HashSet<SimActorKey> _startingExhibitBarriers = [];

    private readonly Dictionary<uint, Queue<QueuedFx>> _queuedBySrvOid = [];

    private readonly Dictionary<uint, RealmActor> _staticHolders = [];

    private readonly Dictionary<uint, ActorEffectProfile> _staticProfiles = [];

    private readonly HashSet<uint> _syntheticHolders = [];

    private readonly HashSet<OnlineActorRecord> _staleOnlineHolders =
        new(ReferenceEqualityComparer.Instance);

    private readonly List<OnlineActorRecord> _staleOnlineHolderOrdering = [];

    private readonly List<OnlineActorRecord> _staleOnlineHolderCapture = [];

    private readonly List<SimActorKey> _primedTagCapture = [];

    private uint _postureBroadcastOwnIdent;

    public ActorEffectDriver(
        OnlineActorCore liveEntities,
        KineticScriptRunner runner,
        KineticScriptTableLookup tables,
        ActorEffectPoseRegistry poses,
        Func<uint, uint, uint?>? descendantAtPiece = null,
        Func<uint, uint?>? ancestorOfAffixedDescendant = null,
        Action<uint>? holderUnregistered = null,
        Action<uint, uint?>? holderSfxChartAltered = null,
        Action<uint, Vector3, uint, float>? playSrvSfx = null)
    {
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _charts = tables ?? throw new ArgumentNullException(nameof(tables));
        _postures = poses ?? throw new ArgumentNullException(nameof(poses));
        _descendantAtPiece = descendantAtPiece ?? ((_, _) => null);
        _ancestorOfAffixedDescendant = ancestorOfAffixedDescendant ?? (_ => null);
        _holderUnregistered = holderUnregistered ?? (_ => { });
        _holderSfxChartAltered = holderSfxChartAltered ?? ((_, _) => { });
        _playSrvSfx = playSrvSfx ?? ((_, _, _, _) => { });
        _runner.DiagnosticSink = msg => DiagnosticSink?.Invoke(msg);
        _postures.EffectPoseChanged += OnFxPostureAltered;
        _onlineActors.ProjectionVisibilityChanged += OnProjVisAltered;
    }

    private enum QueuedFxFlavor
    {
        Direct,
        Typed,
        Sound,
    }

    private readonly record struct QueuedFx(
        QueuedFxFlavor Kind,
        uint ScriptDid,
        uint RawScriptType,
        float Intensity)
    {
        public static QueuedFx Direct(uint programDid) =>
            new(QueuedFxFlavor.Direct, programDid, 0u, 0f);

        public static QueuedFx Typed(uint rawProgramKind, float intensity) =>
            new(QueuedFxFlavor.Typed, 0u, rawProgramKind, intensity);

        public static QueuedFx Sound(uint sfxKind, float volume) =>
            new(QueuedFxFlavor.Sound, 0u, sfxKind, volume);
    }
}

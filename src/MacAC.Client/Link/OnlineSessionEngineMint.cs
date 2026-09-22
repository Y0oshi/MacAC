using System.Diagnostics;
using MacAC.Assets;
using MacAC.Client.Arcana;
using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Kinetics;
using MacAC.Client.Paging;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Cockpit.Panels.Vitals;
using MacAC.Host;
using MacAC.Mechanics.Comms;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Link;

internal sealed record OnlineSessionAvatarEngine(
    AvatarIdentityLedger Identity,
    SimAvatarLocomotionLedger Controller,
    OnlineRealmOriginLedger WorldOrigin);

internal sealed record OnlineSessionDomainEngine(
    SimCore Runtime,
    SimActorObjectLifetime EntityObjects,
    SimToonLedger Character,
    SimActionLedger Actions,
    SimStashLedger Inventory,
    SimCommsLedger Communication);

internal sealed record OnlineSessionWidgetEngine(
    CanonWidgetEngine? RetailUi,
    VitalsModel? Vitals,
    ToonSheetSupplier? CharacterSheet,
    ArcanaEngine? Magic,
    EffigyFrameExhibitor? Paperdoll);

internal sealed record OnlineSessionDealingEngine(
    EnginePreferencesDriver Settings,
    GameplayInputFrameDriver GameplayInput,
    AvatarModeDriver PlayerMode,
    AvatarMannerAutoListing PlayerModeAutoEntry,
    GearDealingDriver ItemInteraction,
    SimFightingAttackLedger CombatAttack,
    PickingDealingDriver SelectionInteractions);

internal sealed record OnlineSessionRealmEngine(
    IDatAccess Dats,
    object DatLock,
    Sound.RealmAudioSessionTurnstile? WorldAudio,
    GpuRealmPhase WorldState,
    OnlineActorCore LiveEntities,
    OnlineActorSessionDriver EntitySession,
    RealmEnvironmentDriver Environment,
    DeferredAvatarWarpWireSink Teleport,
    DatSpawnClaimFillingClassifier SpawnClaims,
    EquippedChildRenderDriver EquippedChildren,
    CanonPickingStage SelectionScene,
    MoteVisibilityDriver ParticleVisibility,
    CanonInboundEventRouter InboundEvents,
    OnlineActorOnlinenessDriver Liveness,
    OnlineActorNetworkRefreshDriver NetworkUpdates,
    OnlineActorFillingDriver Hydration,
    ActorEffectDriver EntityEffects,
    MotionHookFrameQueue AnimationHookFrames,
    OnlineActorDisplayDriver Presentation,
    RemoteLocomotionObservationLedger RemoteMovementObservations,
    RenderStageShadeEngine? RenderSceneShadow,
    EnginePlacementDisplaySink PlacementProjection,
    EnginePlacementMirrorRetrySlot PlacementRetries,
    SimDebutPilot FirstEntryDrive,
    SimGrantedPositionPilot AcceptedPositionDrive,
    SimPeerPlacementPilot RemotePlacementDrive);

internal sealed partial class OnlineSessionEngineMint
{
    private readonly OnlineSessionAvatarEngine _avatar;

    private readonly OnlineSessionDomainEngine _domain;

    private readonly OnlineSessionWidgetEngine _widget;

    private readonly OnlineSessionDealingEngine _dealing;

    private readonly OnlineSessionRealmEngine _world;

    private readonly OnlineSessionDirectiveSurface _commands;

    private readonly Action<string> _trace;

    private readonly OnlineLocomotionStatsApplier _travelStats;

    private readonly SessionStatusScribe _conditionWriter;

    private readonly string _sessIdent;

    private readonly IReadOnlyList<string> _signinDirectives;

    private readonly TimeSpan _signinDirectiveDelay;

    private readonly TimeProvider _momentSupplier;

    private readonly DatCommsPoseRegistry _commsPostures;

    private readonly string _commsTraceFolder;

    private SessionTranscript? _commsSessTrace;

    private CommsTranscriptLogWriter? _commsTraceWriter;

    public OnlineSessionEngineMint(
        OnlineSessionAvatarEngine player,
        OnlineSessionDomainEngine domain,
        OnlineSessionWidgetEngine ui,
        OnlineSessionDealingEngine interaction,
        OnlineSessionRealmEngine world,
        OnlineSessionDirectiveSurface commands,
        Action<string> log,
        SessionStatusScribe? conditionWriter = null,
        string sessionId = "app",
        IReadOnlyList<string>? signinDirectives = null,
        int loginCommandDelayMs = 500,
        TimeProvider? momentSupplier = null,
        string? commsTraceFolder = null)
    {
        _avatar = player ?? throw new ArgumentNullException(nameof(player));
        _domain = domain ?? throw new ArgumentNullException(nameof(domain));
        _widget = ui ?? throw new ArgumentNullException(nameof(ui));
        _dealing = interaction
            ?? throw new ArgumentNullException(nameof(interaction));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _trace = log ?? throw new ArgumentNullException(nameof(log));
        _conditionWriter = conditionWriter ?? new SessionStatusScribe(null);
        _sessIdent = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        if (loginCommandDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loginCommandDelayMs));
        }
        _commsTraceFolder = commsTraceFolder
            ?? UserStateLayout.Locate().Logs;
        _signinDirectives = signinDirectives is null ? [] : [.. signinDirectives];
        _signinDirectiveDelay = TimeSpan.FromMilliseconds(loginCommandDelayMs);
        _momentSupplier = momentSupplier ?? TimeProvider.System;
        _commsPostures = DatCommsPoseRegistry.Load(_world.Dats, _world.DatLock);
        _travelStats = new OnlineLocomotionStatsApplier(
            _avatar.Controller,
            _domain.Character.TravelAptitudes,
            _trace);
    }

    private ChatTranscriptResult AssignCommsTraceFile(string label)
    {
        var trace = _commsSessTrace ??= new SessionTranscript(_commsTraceFolder);
        var writer = _commsTraceWriter ??= new CommsTranscriptLogWriter(trace);
        string? closedLabel = trace.LatestLabel;

        writer.Unfasten();
        bool closed = trace.Close();

        if (string.IsNullOrWhiteSpace(label))
            return new ChatTranscriptResult(Opened: false, closed, string.Empty, closedLabel);

        bool opened = trace.Open(label, out string settled);
        if (opened)
            writer.Attach(_domain.Communication.Chat);

        return new ChatTranscriptResult(opened, closed, settled, closedLabel);
    }

    private void ResetPlayerPresentation()
    {
        _travelStats.Reset();
        _dealing.PlayerMode.RestartSess();
        _world.SpawnClaims.Reset();
    }

    private void ResetIdentityPresentation(
        SimEpochTicket sunsettingGen)
    {
        var restart =
            _domain.Runtime.GenRestart.CaptureSnapshot();
        if (restart.IsActive
            || restart.LastCompletedGeneration != sunsettingGen)
        {
            throw new InvalidOperationException(
                $"Runtime generation {sunsettingGen.Value} hasn't "
                + "converged prior to App identity projection teardown");
        }

        _widget.Vitals?.AssignOwnAvatarOid(0u);
        ActorVanishProbe.AvatarGuid = 0u;
        _dealing.Settings.RestartEngagedToonTag();
        _world.NetworkUpdates.RestartSessPhase();
        _world.Hydration.RestartSessCondition();
        _widget.Paperdoll?.ResetSession();

        _avatar.WorldOrigin.Reset();
    }

    private static double ClientTickerInstant() =>
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

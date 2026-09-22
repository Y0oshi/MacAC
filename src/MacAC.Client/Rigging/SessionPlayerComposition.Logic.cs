using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Preferences;

namespace MacAC.Client.Rigging;

internal sealed partial class SessionAvatarAssemblyPhase
{
    public SessionAvatarResult Compose(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        OnlineDisplayResult online)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(dealing);
        ArgumentNullException.ThrowIfNull(online);
        if (!ReferenceEquals(_deps.SettingsDevTools, prefs))
        {
            throw new InvalidOperationException(
                "Session/player dependencies do not match the ordered settings result");
        }

        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        SessionAvatarEngineWiring? mappings = null;
        bool mappingsPossessedByAmbit = false;
        try
        {
            var outcome = ComposeCore(
                hub,
                substance,
                realm,
                dealing,
                online,
                ambit,
                ref mappings,
                ref mappingsPossessedByAmbit);
            ambit.Complete();
            return outcome;
        }
        catch (Exception miss)
        {
            if (mappings is not null && !mappingsPossessedByAmbit)
            {
                ambit.Own(
                    "session/player runtime bindings",
                    mappings,
                    static val => val.Dispose());
                mappingsPossessedByAmbit = true;
            }

            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private SessionAvatarResult ComposeCore(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        OnlineDisplayResult online,
        AssemblyAcquisitionScope ambit,
        ref SessionAvatarEngineWiring? mappings,
        ref bool mappingsPossessedByAmbit)
    {
        var dependencies = _deps;
        var foundation = realm.Foundation;
        int nearbyRadius = dependencies.Settings.SettledFidelity.NearRadius;
        int farawayRadius = dependencies.Settings.SettledFidelity.FarRadius;
        if (dependencies.Options.LegacyStreamRadius is { } legacyRadius)
        {
            nearbyRadius = legacyRadius;
            farawayRadius = Math.Max(legacyRadius, farawayRadius);
        }
        dependencies.RenderRange.NearbyRadius = nearbyRadius;
        dependencies.RenderRange.FarawayRadius = farawayRadius;
        dependencies.Log(
            $"streaming: nearRadius={nearbyRadius} " +
            $"(window={2 * nearbyRadius + 1}x{2 * nearbyRadius + 1})  " +
            $"farRadius={farawayRadius} " +
            $"(window={2 * farawayRadius + 1}x{2 * farawayRadius + 1})");
        Flaw(SessionAvatarAssemblyPoint.StreamingRadiiResolved);

        IBakedContactSource readiedImpacts =
            substance.PreparedAssets as IBakedContactSource ??
            throw new NotSupportedException(
                "Production prepared assets must expose the matching " +
                "prepared-collision catalog");
        LandblockBuildMint lbAssembleMaker = new LandblockBuildMint(
            substance.Dats,
            readiedImpacts,
            dependencies.DatLock,
            realm.TerrainBuild.HeightTable,
            dependencies.Options.DumpSceneryZ);
        MacAC.Mechanics.Contracts.QuestCatalogue? extensionContractRegistry = null;
        dependencies.WorldGameState.ContractsSrc = () =>
        {
            if (extensionContractRegistry is null)
            {
                lock (dependencies.DatLock)
                    extensionContractRegistry =
                        MacAC.Assets.QuestTableReader.Load(substance.Dats);
            }

            return MacAC.Sim.Play.ContractPluginMirror.Project(
                dependencies.Runtime.ContractsHolder.View,
                extensionContractRegistry,
                DateTime.UtcNow);
        };

        var streamerTenancy = ambit.Acquire(
            "landblock streamer",
            () => LandblockStreamer.CreateForRequests(
                lbAssembleMaker.Build,
                (ident, lb) =>
                {
                    if (lb is null)
                        return null;
                    uint x = (ident >> 24) & 0xFFu;
                    uint y = (ident >> 16) & 0xFFu;
                    return MacAC.Mechanics.Landscape.LandblockTessellation.Build(
                        lb.Heightmap,
                        x,
                        y,
                        realm.TerrainBuild.HeightTable,
                        realm.TerrainBuild.Blending,
                        realm.TerrainBuild.SurfaceCache);
                }),
            static val => val.Dispose());
        Flaw(SessionAvatarAssemblyPoint.StreamerCreated);
        streamerTenancy.Resource.Start();
        Flaw(SessionAvatarAssemblyPoint.StreamerStarted);

        PagingDriver paging = new PagingDriver(
            queuePull: (ident, sort, gen) => streamerTenancy.Resource.EnqueueLoad(
                new LandblockBuildAsk(
                    ident,
                    sort,
                    gen,
                    new LandblockAssembleOrigin(
                        dependencies.WorldOrigin.CenterX,
                        dependencies.WorldOrigin.CenterY))),
            queueUnload: streamerTenancy.Resource.QueueUnload,
            wrapUpSrc: streamerTenancy.Resource,
            phase: online.WorldState,
            nearbyRadius: nearbyRadius,
            farawayRadius: farawayRadius,
            exhibitPipe: online.LandblockPipeline,
            wipeQueuedLoads: streamerTenancy.Resource.WipeQueuedLoads,
            jobAllowanceKnobs: dependencies.Options.StreamingWorkBudgets);
        var pagingOriginRecenter = new PagingOriginRecenterMarshal(
            paging,
            dependencies.WorldOrigin);
        paging.UpperCompletionsPerCycle =
            dependencies.Settings.SettledFidelity.MaxCompletionsPerFrame;
        Flaw(SessionAvatarAssemblyPoint.StreamingCreated);

        mappings = new SessionAvatarEngineWiring();
        var onlineSessDirectives = new OnlineSessionDirectiveSurface(
            dependencies.TryHandlePluginCommand);
        EnginePreferencesTargets prefsMarks = new EnginePreferencesTargets(
            dependencies.Settings.ReadoutPaneMark ?? new SilkEngineDisplayWindowTarget(dependencies.Window),
            online.DrawDispatcher,
            foundation.TerrainAtlas,
            paging,
            dependencies.RenderRange,
            dealing.RetainedUi?.Host.Root,
            onlineSessDirectives,
            commsDensity: dealing.RetainedUi?.Runtime.PaneDensity,
            cameras: hub.CameraController,
            trace: dependencies.Log,
            sound: substance.Audio?.Engine);
        mappings.Adopt(
            "runtime settings targets",
            dependencies.Settings.AttachCoreMarksPossessed(prefsMarks));
        Flaw(SessionAvatarAssemblyPoint.RuntimeSettingsBound);

        var summonClaimClassifier = new DatSpawnClaimFillingClassifier(
            substance.Dats,
            dependencies.DatLock);
        RealmEpochQuiescence realmStillness = new RealmEpochQuiescence(
            dependencies.Actions.Selection,
            online.WorldState,
            oid => online.LiveEntities.TryFetchProjTag(
                oid,
                out MacAC.Sim.Actors.SimActorKey tag)
                    ? tag
                    : null,
            substance.Audio?.Engine);
        var compoundWarmupSrc =
            new CompositeWarmupActorSource(online.WorldState);
        var unveilRouter = online.DrawDispatcher;
        dependencies.KineticEngine.ProbeTrace =
            static stroke => System.Console.WriteLine(stroke);
        var unveilRasterizeAssetList = new RealmRevealRenderResourceRota(
            foundation.MeshAdapter is { } unveilTriMeshes
                ? unveilTriMeshes.AssignDestUnveilPushPrecedence
                : static _ => { },
            foundation.TextureCache.AssignDestUnveilPushPrecedence);
        IRenderFrameResourceTelemetrySource? unveilAssetTelemetry =
            PagingTelemetry.SensorUnveilTiming
                ? new EngineRenderFrameResourceTelemetrySource(
                    motes: null,
                    moteMappings: null,
                    realmRouter: unveilRouter,
                    surroundingsChambers: null,
                    motePainter: null,
                    widgetPhrasePainter: null,
                    gatewayZDepthBitmask: null,
                    clipCycle: null,
                    land: null,
                    illumination: null,
                    triMeshes: foundation.MeshAdapter,
                    textures: foundation.TextureCache,
                    readiedHoldings: substance.PreparedAssets)
                : null;
        RealmRevealMarshal realmUnveil = new RealmRevealMarshal(
            online.WorldTransit,
            () => PagingTelemetry.ApplyRevealRadiusOverride(
                new PagingRevealWindow(
                    paging.NearRadius,
                    paging.FarRadius)),
            paging.IsRasterizeNeighborhoodHoused,
            dependencies.KineticEngine.IsSummonChamberPrimed,
            dependencies.KineticEngine.IsNeighborhoodLandHoused,
            () => unveilRouter?.CompoundTexturesPrimed ?? true,
            (destChamber, radius) =>
            {
                if (unveilRouter is null)
                    return;
                compoundWarmupSrc.Refresh(destChamber, radius);
                unveilRouter.ReadyCompoundTextures(
                    compoundWarmupSrc.Entities,
                    compoundWarmupSrc.Generation,
                    destChamber,
                    radius);
            },
            () =>
            {
                compoundWarmupSrc.Reset();
                unveilRouter?.DirtyCompoundWarmupReadiness();
            },
            summonClaimClassifier.IsUnhydratable,
            realmStillness,
            paging,
            unveilRasterizeAssetList,
            () => online.WorldState.FetchedLbTally,
            unveilAssetTelemetry);
        Flaw(SessionAvatarAssemblyPoint.WorldRevealCreated);

        return CompleteSessionPlayer(
            hub,
            substance,
            realm,
            dealing,
            online,
            ambit,
            streamerTenancy,
            paging,
            pagingOriginRecenter,
            realmUnveil,
            summonClaimClassifier,
            mappings,
            onlineSessDirectives,
            ref mappingsPossessedByAmbit);
    }

    private void Flaw(SessionAvatarAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}

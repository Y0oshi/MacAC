using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Controls;
using MacAC.Client.Fighting;
using MacAC.Client.Graphics;
using MacAC.Client.Kinetics;
using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Client.SimBridge;
using MacAC.Client.Sound;
using MacAC.Client.Telemetry;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Presence;

namespace MacAC.Client.Rigging;

internal sealed partial class SessionAvatarAssemblyPhase
{
    private SessionAvatarResult CompleteSessionPlayer(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        OnlineDisplayResult online,
        AssemblyAcquisitionScope ambit,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<LandblockStreamer>
            streamerTenancy,
        PagingDriver paging,
        PagingOriginRecenterMarshal pagingOriginRecenter,
        RealmRevealMarshal realmUnveil,
        DatSpawnClaimFillingClassifier summonClaimClassifier,
        SessionAvatarEngineWiring mappings,
        OnlineSessionDirectiveSurface onlineSessDirectives,
        ref bool mappingsPossessedByAmbit)
    {
        var dependencies = _deps;
        var networkRefreshBridge = new DeferredOnlineActorWirePulseSink();
        var sealedDungeonChambers = new DatSealedDungeonChamberClassifier(
            substance.Dats,
            dependencies.DatLock);
        var projMaterializer = new DatOnlineActorMirrorAssembler(
            dependencies.Options,
            substance.Dats,
            online.LiveEntities,
            substance.CollisionAssets,
            substance.AnimationLoader,
            online.EntitySpawnAdapter,
            realm.Foundation.TextureCache,
            dependencies.ClassificationCache,
            dependencies.EffectPoses,
            online.EquippedChildren,
            dependencies.WorldGameState,
            dependencies.WorldEvents,
            dependencies.KineticEngine.ShadeObjects,
            substance.CollisionBuilder,
            online.ProjectileController,
            online.AnimationPresenter,
            online.StaticAnimationScheduler,
            dependencies.WorldOrigin,
            dependencies.UpdateClock,
            dependencies.Runtime.PassageHolder);
        var originCoordinator = new OnlineActorRealmOriginMarshal(
            dependencies.WorldOrigin,
            paging,
            online.WorldState,
            realmUnveil,
            dependencies.PlayerIdentity,
            sealedDungeonChambers,
            dependencies.Log);

        var onlineSess = dependencies.Runtime.Session;
        OnlineSessionAppSource onlineSessSrc = new OnlineSessionAppSource(
            onlineSess,
            onlineSessDirectives);
        mappings.Adopt(
            "retained-UI live session",
            dealing.LateBindings.Session.Bind(onlineSessSrc));
        var ownKineticsTimestamps =
            new OnlineSessionLocalKineticsTimestampHerald(
                dependencies.PlayerIdentity,
                onlineSessSrc);
        Flaw(SessionAvatarAssemblyPoint.LiveSessionCreated);

        var teardown = new OnlineActorEngineTeardownDriver(
            online.LiveEntities,
            online.Presentation,
            online.EntityEffects,
            online.SelectionInteractions,
            dependencies.Actions.Selection,
            dependencies.AnimatedEntities,
            dependencies.RemoteMovementObservations,
            dependencies.TranslucencyFades,
            online.ProjectionWithdrawal,
            online.EquippedChildren,
            dependencies.KineticEngine.ShadeObjects,
            online.Lights,
            dependencies.ClassificationCache,
            dependencies.PlayerIdentity);
        var deletion = new OnlineActorDeletionDriver(
            online.LiveEntities,
            dependencies.EntityObjects,
            teardown,
            dependencies.PlayerIdentity);
        online.LiveEntities.Physics.AttachObjectChartHubLocator(
            oid => dependencies.MotionBindings.ResolvePhysicsHost(oid));
        IBakedContactSource leadListingImpact =
            substance.PreparedAssets as IBakedContactSource
            ?? throw new NotSupportedException(
                "Production prepared assets must expose the matching "
                + "prepared-collision catalog");
        uint leadListingCylinderOwnIdent = 0u;
        float leadListingCylinderRadius = 0f;
        float leadListingCylinderHeight = 0f;
        SimDebutPilot leadListingSteer = new SimDebutPilot(
            dependencies.EntityObjects,
            dependencies.Runtime.Clock,
            leadListingImpact,
            () => AvatarLocomotionAssemblyOptions.From(
                dependencies.Runtime.ToonHolder.TravelAptitudes.Snapshot),
            capture =>
            {
                float radius = 0.48f;
                float height = 1.835f;
                uint ownIdent = capture.Key?.LocalEntityId ?? 0u;
                if (ownIdent is not 0u && ownIdent == leadListingCylinderOwnIdent)
                {
                    radius = leadListingCylinderRadius;
                    height = leadListingCylinderHeight;
                }
                else if (online.LiveEntities.TryFetchRealmActor(
                        capture.ServerGuid,
                        out RealmActor? avatarActor)
                    && avatarActor is not null)
                {
                    (float rigRadius, float rigHeight) =
                        dependencies.MotionBindings.GetSetupCylinder(
                            capture.ServerGuid,
                            avatarActor);
                    if (rigRadius >= 0.05f)
                    {
                        radius = rigRadius;
                        height = rigHeight;
                        if (ownIdent is not 0u)
                        {
                            leadListingCylinderOwnIdent = ownIdent;
                            leadListingCylinderRadius = radius;
                            leadListingCylinderHeight = height;
                        }
                    }
                }
                bool hasAuthoredShade = capture.Key is { } tag
                    && dependencies.KineticEngine.ShadeObjects.HasLogicalHolder(
                        tag.LocalEntityId);
                return new SimAvatarKineticsArmingStaging(
                    radius,
                    height,
                    hasAuthoredShade
                        ? SimAvatarProxyVerdict
                            .RegisteredAuthoredPayload
                        : SimAvatarProxyVerdict.ProvenShapeless);
            });
        SimGrantedPositionPilot approvedLocusSteer = new SimGrantedPositionPilot(
            dependencies.EntityObjects,
            dependencies.Runtime.Clock,
            leadListingImpact,
            dependencies.PlayerOutbound,
            () => dependencies.Runtime.Generation,
            () => dependencies.PlayerIdentity.SrvOid,
            () => dependencies.PlayerController.Controller,
            () => dependencies.Character.UseLocusFromSrv,
            () => onlineSessSrc.LatestSess,
            () => dependencies.PlayerController,
            isGatewayArbiterLatest: gateway => online.WorldTransit
                .CanPlaceGatewayDestination(
                    gateway.RevealGeneration,
                    gateway.TeleportSequence,
                    gateway.Projection.DestinationCell));
        SimPeerPlacementPilot distantStanceSteer = new SimPeerPlacementPilot(
            dependencies.EntityObjects,
            dependencies.Runtime.Clock,
            leadListingImpact,
            new GraphicalDistantStanceFacilityPane(
                online.WorldState,
                paging.IsLbExhibitPrimed));
        OnlineActorFillingDriver hydration = new OnlineActorFillingDriver(
            online.LiveEntities,
            dependencies.EntityObjects,
            dependencies.DatLock,
            projMaterializer,
            new OnlineActorRelationshipMirror(online.EquippedChildren),
            new OnlineActorReadyHerald(
                online.LiveEntities,
                online.EntityEffects,
                online.Presentation,
                online.RenderSceneShadow?.OnlineProjections),
            originCoordinator,
            networkRefreshBridge,
            ownKineticsTimestamps,
            dependencies.PlayerIdentity,
            deletion,
            leadListingSteer,
            approvedLocusSteer);
        mappings.Adopt(
            "landblock-loaded hydration",
            online.LandblockLoaded.Bind(hydration));
        Flaw(SessionAvatarAssemblyPoint.HydrationCreated);

        var realmDiscardProj =
            new StashRealmDropMirrorDriver(
                dealing.ItemInteraction,
                dependencies.EntityObjects.Objects,
                online.LiveEntities,
                hydration,
                dependencies.Actions.Selection,
                () => dependencies.UpdateClock.SimulationMomentSecs);
        mappings.Adopt(
            "inventory world-drop projection",
            realmDiscardProj);

        var networkUpdates = new OnlineActorNetworkRefreshDriver(
            online.LiveEntities,
            dependencies.EntityObjects.Objects,
            hydration,
            online.EntityEffects,
            online.Presentation,
            online.Lights,
            online.EquippedChildren,
            online.ProjectileController,
            dependencies.AnimatedEntities,
            dependencies.RemoteMovementObservations,
            dependencies.RemotePhysicsUpdater,
            dependencies.RemoteInboundMotion,
            online.MotionRuntime,
            dependencies.KineticEngine,
            substance.Dats,
            substance.AnimationLoader,
            dependencies.Actions.CombatTarget,
            dependencies.WorldOrigin,
            dependencies.TeleportSink,
            dependencies.PlayerController,
            dependencies.PlayerOutbound,
            dependencies.PlayerHost,
            dependencies.PlayerIdentity,
            dependencies.UpdateClock,
            onlineSessSrc,
            ownKineticsTimestamps.Publish,
            dependencies.MovementDiagnostics,
            approvedLocusSteer,
            distantStanceSteer,
            realmDiscardProj);
        var liveness = new OnlineActorOnlinenessDriver(
            online.LiveEntities,
            dependencies.PlayerIdentity,
            deletion);
        OnlineActorSessionDriver sessSignals = new OnlineActorSessionDriver(
            dependencies.InboundEntityEvents,
            hydration,
            networkUpdates,
            dependencies.TeleportSink,
            online.EntityEffects);
        mappings.Adopt(
            "live parent acceptance",
            online.ParentAcceptance.BindOwned(
                hydration.TryAdmitAncestorForProj));
        mappings.Adopt(
            "same-generation network updates",
            networkRefreshBridge.BindOwned(networkUpdates));
        mappings.AttachActorPrimed(
            online.EquippedChildren,
            contender => _ = hydration.OnActorReady(contender));
        mappings.AttachLooksImposed(
            hydration,
            oid =>
            {
                if (oid == dependencies.PlayerIdentity.SrvOid)
                    online.PaperdollPresenter?.FlagStale();
            });
        Flaw(SessionAvatarAssemblyPoint.LiveEntityGraphBound);

        dependencies.WorldOrigin.AssignPlaceholder(
            realm.TerrainBuild.InitialCenterX,
            realm.TerrainBuild.InitialCenterY);
        mappings.Adopt(
            "combat attack operations",
            dependencies.CombatAttackOperations.BindOwned(
                new OnlineFightingAttackOperations(
                    dependencies.Actions.Combat,
                    new FightingAttackTargetSource(
                        dependencies.Actions.Selection,
                        online.LiveEntities,
                        dependencies.EntityObjects.Objects,
                        dependencies.PlayerIdentity),
                    new ToonKnobFightingPreferencesSource(dependencies.Character.Options),
                    dependencies.PlayerController,
                    dependencies.PlayerOutbound,
                    onlineSessSrc,
                    onlineSessSrc,
                    dependencies.CombatFeedback)));
        mappings.Adopt(
            "combat feedback",
            dependencies.CombatFeedback.BindOwned(
                phrase => dependencies.Communication.AddText(
                    phrase, CanonLogTextType.ClientLocal)));
        Flaw(SessionAvatarAssemblyPoint.CombatOperationsBound);

        MouseLookDriver? pointerGaze =
            hub.MouseSource is not null && hub.MouseLookCursor is not null
                ? new MouseLookDriver(
                    hub.MouseSource,
                    dependencies.PointerPosition,
                    dependencies.PlayerMode,
                    dependencies.PlayerController,
                    hub.CameraController,
                    dependencies.ChaseCameraInput,
                    dependencies.LocomotionInput,
                    dependencies.PlayerOutbound,
                    onlineSessSrc,
                    hub.MouseLookCursor,
                    new SurroundingsFeedMonotonicTimer())
                : null;
        GameplayInputFrameDriver gameplayFeed = new GameplayInputFrameDriver(
            hub.InputRouter,
            dependencies.LocomotionInput,
            pointerGaze,
            new FightingAttackInputFrameBridge(dealing.CombatAttack));
        if (hub.CameraPointerInput is { } camPtr)
        {
            mappings.Adopt(
                "camera pointer gameplay frame",
                camPtr.AttachGameplayCyclePossessed(gameplayFeed));
        }
        Flaw(SessionAvatarAssemblyPoint.GameplayInputBound);

        PagingFrameDriver pagingCycle = new PagingFrameDriver(
            dependencies.Options.LiveMode,
            dependencies.PlayerMode,
            dependencies.PlayerController,
            onlineSessSrc,
            dependencies.WorldOrigin,
            networkUpdates,
            new FlyCameraPagingWatcherSource(hub.CameraController),
            new KineticsPagingDungeonCellSource(dependencies.KineticEngine),
            pagingOriginRecenter,
            paging,
            new OnlineMirrorSalvageRebucketter(
                online.WorldState,
                online.LiveEntities));
        IAmbientCycleStage? AssembleAmbientCycle()
        {
            if (substance.Audio?.Ambient is not { } ambient)
                return null;

            WorldRegion zone =
                substance.Dats.Get<WorldRegion>(0x13000000u)!;
            if (zone is null)
                return null;

            ambient.SetupZone(zone, PullLandWords);
            return new AmbientCycleStage(
                ambient,
                new AvatarAmbientListenerSource(
                    dependencies.PlayerController,
                    insideLbOwn: (chamberIdent, chamberOwn) =>
                    {
                        var chamberStruct = dependencies.KineticAssetCache.FetchChamberStruct(chamberIdent);
                        return chamberStruct is null || !chamberStruct.SeenOutside
                            ? null
                            : System.Numerics.Vector3.Transform(
                            chamberOwn,
                            chamberStruct.WorldTransform);
                    }));

            ushort[]? PullLandWords(uint lbIdent)
            {
                if (!online.WorldState.TryFetchLb(lbIdent, out MountedLandblock? fetched)
                    || fetched?.Heightmap is not { } heightmap)

                    return null;

                ushort[] words = new ushort[heightmap.Samples.Length];
                for (int idx = 0; idx < words.Length; ++idx)
                    words[idx] = (ushort)heightmap.Samples[idx];
                return words;
            }
        }

        OnlineEffectFrameDriver onlineFxCycle = new OnlineEffectFrameDriver(
            dependencies.TranslucencyFades,
            substance.AnimationHookFrames,
            online.EntityEffects,
            substance.ParticleSink,
            online.Lights,
            dependencies.ParticleVisibility,
            substance.ParticleSystem,
            substance.ScriptRunner,
            dependencies.UpdateClock,
            new PreferencesMoteRangeSource(dependencies.Settings),
            AssembleAmbientCycle());
        var onlineSpatialReconciler = new OnlineSpatialDisplayReconciler(
            online.EntityEffects,
            online.EquippedChildren,
            substance.ParticleSink,
            online.Lights,
            online.RenderSceneShadow?.OnlineProjections);
        AvatarMotionDriver ownAvatarAnim = new AvatarMotionDriver(
            online.LiveEntities,
            dependencies.PlayerIdentity,
            dependencies.AnimatedEntities,
            online.AnimationPresenter,
            substance.AnimationHookFrames);
        var ownAvatarShade = online.LocalPlayerShadowSynchronizer;
        AvatarMirrorDriver ownAvatarProj = new AvatarMirrorDriver(
            new OnlineAvatarMirrorEngine(
                online.LiveEntities,
                dependencies.PlayerIdentity,
                dependencies.WorldOrigin,
                ownAvatarShade));
        OnlineAvatarFrameEngine ownAvatarCycleCore = new OnlineAvatarFrameEngine(
            hub.CameraController,
            dependencies.PlayerMode,
            dependencies.PlayerController,
            dependencies.ChaseCameraInput,
            dependencies.LocomotionInput,
            dependencies.InputCapture,
            online.LiveEntities,
            dependencies.PlayerIdentity,
            dependencies.PlayerHost,
            ownAvatarProj,
            dependencies.PlayerOutbound,
            onlineSessSrc);
        CanonAvatarFrameDriver ownAvatarCycle = new CanonAvatarFrameDriver(
            dependencies.Runtime,
            ownAvatarCycleCore,
            dependencies.LocomotionInput);
        OnlineObjectFrameDriver onlineObjectCycle = new OnlineObjectFrameDriver(
            dependencies.InboundEntityEvents,
            ownAvatarCycle,
            online.SelectionInteractions,
            online.LiveEntities,
            dependencies.PlayerIdentity,
            dependencies.WorldOrigin,
            online.AnimationScheduler,
            online.StaticAnimationScheduler,
            online.AnimationPresenter,
            dependencies.AnimatedEntities,
            online.EquippedChildren,
            onlineFxCycle,
            online.RenderSceneShadow?.OnlineProjections,
            online.RenderSceneShadow?.StaticProjections);
        Flaw(SessionAvatarAssemblyPoint.UpdateLeavesCreated);

        AvatarModeDriver avatarManner = new AvatarModeDriver(
            dependencies.PlayerMode,
            dependencies.PlayerController,
            dependencies.PlayerHost,
            dependencies.ChaseCameraInput,
            hub.CameraController,
            dependencies.KineticEngine,
            online.LiveEntities,
            dependencies.PlayerIdentity,
            dependencies.WorldOrigin,
            dependencies.MotionBindings,
            substance.Dats,
            dependencies.DatLock,
            substance.CollisionAssets,
            dependencies.AnimatedEntities,
            ownAvatarAnim,
            ownAvatarShade,
            dependencies.PlayerApproachCompletions,
            gameplayFeed,
            onlineSessSrc,
            dependencies.MovementDiagnostics,
            dependencies.Character.TravelAptitudes,
            dependencies.ViewportAspect);
        AvatarMannerAutoListing avatarMannerAutoListing = new AvatarMannerAutoListing(
            new OnlineAvatarModeAutoEntryScope(
                onlineSessSrc,
                online.LiveEntities,
                dependencies.PlayerIdentity,
                realmUnveil,
                dependencies.PlayerMode,
                avatarManner));
        avatarManner.AttachAutoListing(avatarMannerAutoListing);
        Flaw(SessionAvatarAssemblyPoint.PlayerModeBound);

        var ownWarp =
            dependencies.PortalTunnelFallback.Transfer(BuildOwnWarpWithTunnel);

        AvatarWarpDriver BuildOwnWarp(
            IAvatarWarpDisplay exhibit) =>
            new(
                new OnlineAvatarWarpAuthority(
                    online.LiveEntities,
                    dependencies.PlayerIdentity),
                gameplayFeed,
                avatarManner,
                new AvatarWarpPagingOperations(
                    dependencies.WorldOrigin,
                    pagingOriginRecenter,
                    paging,
                    sealedDungeonChambers),
                online.WorldTransit,
                realmUnveil,
                new AvatarWarpPlacement(
                    online.LiveEntities,
                    dependencies.PlayerIdentity,
                    dependencies.PlayerController,
                    dependencies.PlayerHost,
                    dependencies.ChaseCameraInput,
                    onlineSpatialReconciler),
                new AvatarWarpSession(onlineSessSrc),
                exhibit,
                approvedLocusSteer,
                new EngineSignInLifespanSource(dependencies.Runtime),
                new EngineAvatarLogoutOperations(
                    dependencies.Runtime,
                    dependencies.PlayerController,
                    onlineSessSrc,
                    dependencies.Inventory.Objects,
                    dependencies.PlayerIdentity));

        AvatarWarpDriver BuildOwnWarpWithTunnel(
            PortalTunnelDisplay gatewayTunnel)
        {
            AvatarWarpDisplay tunnelExhibit = new AvatarWarpDisplay(gatewayTunnel);
            if (substance.Audio?.UiSounds is { } gatewayWidgetSfxList)
                tunnelExhibit.WidgetSfxDrain = sfx => gatewayWidgetSfxList.Play(sfx);
            return BuildOwnWarp(tunnelExhibit);
        }
        if (substance.Audio?.UiSounds is { } environWidgetSfxList)
        {
            dependencies.WorldEnvironment.EnvironSfxDrain =
                editKind => environWidgetSfxList.PlayEnvironCue(editKind);
        }
        var warpTenancy = ambit.Own(
            "local-player teleport",
            ownWarp,
            static val => val.Dispose());
        Flaw(SessionAvatarAssemblyPoint.PortalTransferred);
        mappings.Adopt(
            "local teleport network sink",
            dependencies.TeleportSink.BindOwned(ownWarp));
        mappings.Adopt(
            "selection view plane",
            dealing.LateBindings.PickLensPlane.Bind(ownWarp));
        Flaw(SessionAvatarAssemblyPoint.TeleportBound);

        IDisposable moduleLifecycleMapping =
            online.ComponentLifecycle.BindOwned(teardown);
        IDisposable moduleLifecycleAdoption;
        try
        {
            moduleLifecycleAdoption = online.RuntimeBindings.AdoptPossessed(
                "live runtime component teardown",
                moduleLifecycleMapping);
        }
        catch
        {
            moduleLifecycleMapping.Dispose();
            throw;
        }
        var moduleLifecycleTenancy = ambit.Own(
            "live runtime component teardown adoption",
            moduleLifecycleAdoption,
            static val => val.Dispose());

        var vitals =
            dealing.RetainedUi?.Vitals;
        var stanceProjReattempt =
            new EnginePlacementMirrorRetrySlot(
                () => dependencies.Runtime.Generation);
        OnlineSessionEngineMint sessCoreMaker = new OnlineSessionEngineMint(
            new OnlineSessionAvatarEngine(
                dependencies.PlayerIdentity,
                dependencies.PlayerController,
                dependencies.WorldOrigin),
            new OnlineSessionDomainEngine(
                dependencies.Runtime,
                dependencies.EntityObjects,
                dependencies.Character,
                dependencies.Actions,
                dependencies.Inventory,
                dependencies.Communication),
            new OnlineSessionWidgetEngine(
                dealing.RetainedUi?.Runtime,
                vitals,
                dealing.RetainedUi?.CharacterSheet,
                dealing.Magic,
                online.PaperdollPresenter),
            new OnlineSessionDealingEngine(
                dependencies.Settings,
                gameplayFeed,
                avatarManner,
                avatarMannerAutoListing,
                dealing.ItemInteraction,
                dealing.CombatAttack,
                online.SelectionInteractions),
            new OnlineSessionRealmEngine(
                substance.Dats,
                dependencies.DatLock,
                substance.Audio?.Engine is { } sessSoundEngine
                    ? new MacAC.Client.Sound.RealmAudioSessionTurnstile(
                        sessSoundEngine,
                        substance.Audio.Ambient)
                    : null,
                online.WorldState,
                online.LiveEntities,
                sessSignals,
                dependencies.WorldEnvironment,
                dependencies.TeleportSink,
                summonClaimClassifier,
                online.EquippedChildren,
                online.SelectionScene,
                dependencies.ParticleVisibility,
                dependencies.InboundEntityEvents,
                liveness,
                networkUpdates,
                hydration,
                online.EntityEffects,
                substance.AnimationHookFrames,
                online.Presentation,
                dependencies.RemoteMovementObservations,
                online.RenderSceneShadow,
                online.PlacementProjection,
                stanceProjReattempt,
                leadListingSteer,
                approvedLocusSteer,
                distantStanceSteer),
            onlineSessDirectives,
            dependencies.Log,
            dependencies.StatusWriter,
            dependencies.Options.SessionId ?? "app",
            dependencies.Options.LoginCommands,
            dependencies.Options.LoginCommandDelayMs);
        var sessHub = sessCoreMaker.Create(
            onlineSess,
            new OnlineSessionConnectOptions(
                dependencies.Options.LiveMode,
                dependencies.Options.LiveHost,
                dependencies.Options.LivePort,
                dependencies.Options.LiveUser ?? string.Empty,
                dependencies.Options.LivePass ?? string.Empty,
                dependencies.Options.LiveCharacterSelector,
                AwaitCharacterSelection:
                    dependencies.Options.LiveCharacterSelector is null));
        Flaw(SessionAvatarAssemblyPoint.SessionHostCreated);

        Action<string>? diagToast = null;
        var fightingMannerOps = new OnlineFightingModeOperations(
            new OnlineSessionFightingModeAuthority(sessHub),
            new AvatarFightingEquipmentSource(
                dependencies.EntityObjects.Objects,
                dependencies.PlayerIdentity),
            new GearDealingFightingModeIntentSink(
                dealing.ItemInteraction));
        mappings.Adopt(
            "runtime combat-mode operations",
            dependencies.CombatModeOperations.BindOwned(fightingMannerOps));
        var fightingDirective = new EngineFightingModeDirectiveBridge(
            dependencies.Actions.CombatMode,
            dependencies.Log,
            diagToast,
            phrase => dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal));
        mappings.Adopt(
            "live combat-mode commands",
            dependencies.CombatModeCommands.BindOwned(fightingDirective));
        CurrentGameEngineBridge playCore = new CurrentGameEngineBridge(
            dependencies.Runtime,
            sessHub,
            onlineSessDirectives,
            online.SelectionInteractions);
        mappings.Adopt("current game runtime adapter", playCore);
        mappings.Adopt(
            "retained-UI game runtime commands",
            dealing.LateBindings.SimCore.Bind(playCore, playCore));

        var nearbyTelemetry = new NearbyRealmTelemetryDumper(
            new EngineNearbyRealmTelemetrySource(
                dependencies.PlayerMode,
                dependencies.PlayerController,
                hub.CameraController,
                dependencies.WorldOrigin,
                online.WorldState,
                dependencies.KineticEngine),
            dependencies.Log);
        var coreTelemetry = new EngineTelemetryDirectiveDriver(
            dependencies.WorldEnvironment,
            dependencies.WorldSceneDebugState,
            hub.CameraPointerInput,
            nearbyTelemetry,
            diagToast);
        mappings.Adopt(
            "runtime diagnostic commands",
            dependencies.RuntimeDiagnosticCommands.BindOwned(coreTelemetry));
        Flaw(SessionAvatarAssemblyPoint.CommandsBound);

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            GameplayFeedActRouter>? gameplayActsTenancy = null;
        if (hub.InputRouter is { } router)
        {
            CameraPointerInputDriver ptr = hub.CameraPointerInput
                ?? throw new InvalidOperationException(
                    "Gameplay action routing needs the composed pointer owner");
            var directives = new GameplayInputDirectiveDriver(
                new RetainedGameplayWindowDirectives(
                    dealing.RetainedUi?.Runtime),
                coreTelemetry,
                new AvatarModeGameplayDirectives(avatarManner),
                new GearTargetModeDirectives(dealing.ItemInteraction),
                playCore,
                playCore.Combat,
                flipSoundMute: substance.Audio?.Engine is { } soundEngine
                    ? () =>
                    {
                        soundEngine.Muted = !soundEngine.Muted;
                        Console.WriteLine(
                            soundEngine.Muted
                                ? "audio: muted (Ctrl+M to unmute)"
                                : "audio: unmuted");
                    }
            : null);
            var marks = new EngineGameplayInputPriorityTargets(
                gameplayFeed,
                ptr,
                dealing.RetainedUi?.Runtime,
                online.SelectionInteractions,
                playCore,
                playCore.Selection,
                playCore.MovementCommands,
                playCore.ToonDirectives,
                directives);
            var gameplayActs =
                GameplayFeedActRouter.Create(
                    router,
                    dependencies.Actions.Combat,
                    marks,
                    dependencies.HostQuiescence,
                    dependencies.Log);
            gameplayActsTenancy = ambit.Own(
                "gameplay input actions",
                gameplayActs,
                static val => val.Dispose());
            gameplayActs.Fasten();
        }
        Flaw(SessionAvatarAssemblyPoint.GameplayActionsAttached);

        var mappingsTenancy = ambit.Own(
            "session/player runtime bindings",
            mappings,
            static val => val.Dispose());
        mappingsPossessedByAmbit = true;
        SessionAvatarResult outcome = new SessionAvatarResult(
            streamerTenancy.Resource,
            paging,
            pagingOriginRecenter,
            realmUnveil,
            summonClaimClassifier,
            onlineSess,
            hydration,
            deletion,
            networkUpdates,
            liveness,
            sessSignals,
            gameplayFeed,
            pagingCycle,
            ownAvatarAnim,
            ownAvatarShade,
            ownAvatarCycleCore,
            ownAvatarCycle,
            onlineSpatialReconciler,
            onlineObjectCycle,
            avatarManner,
            avatarMannerAutoListing,
            warpTenancy.Resource,
            sessHub,
            stanceProjReattempt,
            playCore,
            gameplayActsTenancy?.Resource,
            mappings);
        _bulletin.PublishSessionPlayer(outcome);

        streamerTenancy.Transfer();
        warpTenancy.Transfer();
        gameplayActsTenancy?.Transfer();
        moduleLifecycleTenancy.Transfer();
        mappingsTenancy.Transfer();
        Flaw(SessionAvatarAssemblyPoint.ResultPublished);
        return outcome;
    }
}

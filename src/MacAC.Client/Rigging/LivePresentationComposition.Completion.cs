using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Heavens;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Kinetics;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Cockpit.Panels.SpewBox;
using MacAC.Sim.Realm;
using MacAC.Wire.Messages;

namespace MacAC.Client.Rigging;

internal sealed partial class OnlineDisplayAssemblyPhase
{
    private OnlineDisplayResult CompletePresentation(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        DeferredOnlineActorEngineComponentLifespan moduleLifecycle,
        OnlineActorMotionEngineDriver locomotionCore,
        DeferredOnlineActorParentAcceptance ancestorAcceptance,
        ActorSummonBridge actorSummonBridge,
        ActorProgramActivator actorProgramActivator,
        CanonStaticAnimatingObjectRota staticAnimScheduler,
        SimRealmCrossingLedger realmPassage,
        RealmEpochAvailabilityLedger realmReadiness,
        GpuRealmPhase realmPhase,
        RenderStageShadeEngine? rasterizeTableauShade,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            RenderStageShadeEngine>? rasterizeTableauShadeTenancy,
        OnlineActorCore onlineActors,
        EnginePlacementDisplaySink stanceProj,
        OwnAvatarShadeSyncer ownAvatarShadeSynchronizer,
        MissileDriver missileDriver,
        OnlineActorMirrorWithdrawalDriver projWithdrawal,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<OnlineActorLightDriver> lampsTenancy,
        OnlineActorMotionRota animScheduler,
        OnlineActorMotionExhibitor animPresenter,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<EquippedChildRenderDriver> equippedTenancy,
        ActorEffectDriver actorFxList,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<OnlineActorDisplayDriver> exhibitTenancy,
        DeferredPickingDealingSource pickDealingSrc,
        OnlineDisplayEngineWiring mappings,
        AssemblyAcquisitionScope ambit,
        ref bool mappingsPossessedByAmbit,
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<OnlineActorCore> onlineCoreTenancy)
    {
        var dependencies = _deps;
        var foundation = realm.Foundation;
        var alphaTempBudgets =
            AlphaScratchAllowanceProfile.Create(
                dependencies.Options.ResidencyBudgets.AlphaScratchBytes);

        CanonPickingStage pickTableau = new CanonPickingStage(
            new CanonPickingGeometryShelf(substance.Dats, dependencies.DatLock));
        var realmPassAmbit = dependencies.Graphics.RealmPassAmbit;
        var routerTenancy = ambit.Acquire(
            "WB draw dispatcher",
            () => new RealmPaintRouter(
                hub.GpuDevice,
                hub.GpuFrameLifetime,
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope"),
                foundation.TextureCache,
                foundation.MeshAdapter!,
                actorSummonBridge,
                dependencies.ClassificationCache,
                dependencies.TranslucencyFades,
                pickTableau,
                dependencies.RetailAlphaQueue,
                alphaTempBudgets.DispatcherBytes,
                foundation.TerrainAtlas?.StructureSpecificsTexture ?? default,
                () => dependencies.Settings.ReadoutPreview.BuildingDetailTextures,
                srvOid => srvOid is not 0u
                    && srvOid == dependencies.PlayerIdentity.SrvOid
                        ? dependencies.ChaseCameraInput.Retail?.AvatarSeeThrough
                            ?? (dependencies.ChaseCameraInput.Legacy?.IsInHead == true ? 1f : 0f)
                        : 0f),
            static val => val.Dispose());
        RealmPickingProbe pickAsk = new RealmPickingProbe(
            onlineActors,
            dependencies.EntityObjects.Objects,
            pickTableau,
            () => dependencies.PlayerIdentity.SrvOid,
            dealing.LateBindings.PickCam.Snapshot,
            () => new Vector2(
                dependencies.PointerPosition.X,
                dependencies.PointerPosition.Y),
            () => dependencies.PlayerDriver.Controller is { } avatar
                ? new AvatarDealingPose(avatar.CellId, avatar.Position)
                : null,
            dependencies.MotionBindings.GetSetupCylinder,
            rigIdent =>
            {
                lock (dependencies.DatLock)
                {
                    return !substance.Dats.TryGet<RigSpec>(rigIdent, out RigSpec? rig)
                        || rig.SelectionOrb is not { } orb
                        ? null
                        : (orb.Center, orb.Radius);
                }
            },
            ownActorIdent =>
                dependencies.EffectPoses.TryFetchTrunkPosture(ownActorIdent, out Matrix4x4 descendantTrunk)
                    ? descendantTrunk
                    : null,
            hasOpenedCorpse:
                dependencies.Runtime.SatchelHolder.ExternalVessels.HasCorpseBeenOpened,
            fightingManner: () => dependencies.Runtime.ActHolder.Combat.LatestMode,
            isFellow: oid => dependencies.Runtime.Fellowship.TryFetchMember(oid, out _));
        RadarCaptureSupplier radarCaptureSupplier = new RadarCaptureSupplier(
            dependencies.EntityObjects.Objects,
            onlineActors,
            () => onlineActors.Snapshots,
            playerGuid: () => dependencies.PlayerIdentity.SrvOid,
            playerYawRadians: () => dependencies.PlayerDriver.Controller?.Yaw ?? 0f,
            playerCellId: () => dependencies.PlayerDriver.Controller?.CellId ?? 0u,
            selectedGuid: () => dependencies.Selection.ChosenObjectTag,
            coordinatesOnRadar: () => dependencies.Character.Options.GetOptionBit(
                CharacterOptionId.CoordinatesOnRadar),
            uiLocked: () => dependencies.Character.Options.GetOptionBit(
                CharacterOptionId.LockUI),
            spatialAsk: () => realmPhase);
        mappings.Adopt(
            "radar snapshot",
            dealing.LateBindings.Radar.Bind(radarCaptureSupplier));
        PickingDealingDriver pickInteractions = new PickingDealingDriver(
            dependencies.Selection,
            pickAsk,
            dealing.ItemInteraction,
            new RealmSessionPickingDealingTransport(
                () => dealing.LateBindings.Session.LatestSess),
            new AvatarDealingLocomotionSink(
                () => dependencies.PlayerDriver.Controller,
                dependencies.PlayerApproachCompletions),
            dependencies.Toast,
            dependencies.PlayerApproachCompletions,
            dividePile: oid =>
                dealing.RetainedUi?.Runtime.ChosenObjectDriver?
                    .FocusDividePileListing(oid) ?? false,
            fellowshipParticipants: () =>
                dependencies.Runtime.Fellowship.FetchParticipants().Select(static participant => participant.Guid));
        pickDealingSrc.Bind(pickInteractions);
        mappings.Adopt(
            "world selection",
            dealing.LateBindings.Selection.Bind(
                pickAsk,
                pickInteractions));
        Flaw(OnlineDisplayAssemblyPoint.SelectionAndRadarBound);

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            RetainedWidgetGameplayWiring>? keptGameplayTenancy = null;
        if (dealing.RetainedUi is { } keptWidget)
        {
            keptGameplayTenancy = ambit.Acquire(
                "retained gameplay binding",
                () => RetainedWidgetGameplayWiring.Create(
                    keptWidget.Host.Root,
                    (gear, x, y) =>
                        pickInteractions.PutDraggedGear(gear, x, y),
                    dependencies.HostQuiescence),
                static val => val.Dispose());
            keptGameplayTenancy.Resource.Affix();
        }
        Flaw(OnlineDisplayAssemblyPoint.RetainedGameplayBound);
        if (routerTenancy.Resource is { } alphaRouter)
        {
            alphaRouter.AlphaToCoverage =
                dependencies.Settings.SettledFidelity.AlphaToCoverage;
        }

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            EffigyViewportPainter>? paperdollTenancy = null;
        EffigyFrameExhibitor? paperdollPresenter = null;
        if (routerTenancy.Resource is { } paperdollRouter
            && dealing.RetainedUi?.Runtime.PaperdollViewportWidget is { } viewRect
            && dealing.RetainedUi.Runtime.InventoryFrame is { } satchelCycle)
        {
            paperdollTenancy = ambit.Acquire(
                "paperdoll viewport",
                () => new EffigyViewportPainter(
                    realmPassAmbit
                        ?? throw new InvalidOperationException(
                            "The graphics backend must publish a world pass scope"),
                    hub.GpuDevice,
                    hub.GpuFrameLifetime,
                    paperdollRouter,
                    foundation.SceneLighting!,
                    foundation.TextureCache,
                    foundation.MeshAdapter!),
                static val => val.Dispose());
            var earlierPainter = viewRect.Painter;
            viewRect.Painter = paperdollTenancy.Resource;
            mappings.AdoptFree(
                "paperdoll viewport target",
                () =>
                {
                    if (ReferenceEquals(viewRect.Painter, paperdollTenancy.Resource))
                        viewRect.Painter = earlierPainter;
                });
            paperdollPresenter = new EffigyFrameExhibitor(
                paperdollTenancy.Resource,
                new CanonEffigyFrameView(
                    viewRect,
                    new EffigyStashVisibility(satchelCycle)),
                new CanonEffigyDollMint(
                    new OnlineEffigyActorLookup(onlineActors),
                    dependencies.PlayerIdentity,
                    new CanonEffigyPoseApplicator(
                        substance.Dats,
                        substance.AnimationLoader,
                        dependencies.DatLock)));
        }

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            CreatureAssayViewportPainter>? beastAppraisalTenancy = null;
        CreatureAssayFrameExhibitor? beastAppraisalPresenter = null;
        if (routerTenancy.Resource is { } appraisalRouter
            && dealing.RetainedUi?.Runtime.BeastAppraisalViewRectWidget
                is { } beastViewRect
            && dealing.RetainedUi.Runtime.ExaminationCycle
                is { } examinationCycle
            && dealing.RetainedUi.Runtime.AppraisalDriver
                is { } appraisalDriver)
        {
            beastAppraisalTenancy = ambit.Acquire(
                "creature appraisal viewport",
                () => new CreatureAssayViewportPainter(
                    realmPassAmbit
                        ?? throw new InvalidOperationException(
                            "The graphics backend must publish a world pass scope"),
                    hub.GpuDevice,
                    hub.GpuFrameLifetime,
                    appraisalRouter,
                    foundation.SceneLighting!,
                    foundation.TextureCache,
                    foundation.MeshAdapter!),
                static val => val.Dispose());
            var earlierPainter = beastViewRect.Painter;
            beastViewRect.Painter = beastAppraisalTenancy.Resource;
            mappings.AdoptFree(
                "creature appraisal viewport target",
                () =>
                {
                    if (ReferenceEquals(
                            beastViewRect.Painter,
                            beastAppraisalTenancy.Resource))

                        beastViewRect.Painter = earlierPainter;
                });
            beastAppraisalPresenter = new CreatureAssayFrameExhibitor(
                beastAppraisalTenancy.Resource,
                new CanonCreatureAssayFrameView(
                    beastViewRect,
                    examinationCycle,
                    appraisalDriver),
                new CanonCreatureAssayCloneMint(
                    new OnlineCreatureAssayActorLookup(onlineActors)));
        }

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            ChargenPreviewPainter>? chargenPreviewTenancy = null;
        ChargenPreviewDriver? chargenPreviewDriver = null;
        if (routerTenancy.Resource is { } chargenRouter
            && dealing.RetainedUi?.Runtime.ChargenPreviewViewRectWidget is { } chargenViewRect)
        {
            ClientChargenPreviewCamera chargenCam = new ClientChargenPreviewCamera();
            chargenPreviewTenancy = ambit.Acquire(
                "chargen preview viewport",
                () => new ChargenPreviewPainter(
                    realmPassAmbit
                        ?? throw new InvalidOperationException(
                            "The graphics backend must publish a world pass scope"),
                    hub.GpuDevice,
                    hub.GpuFrameLifetime,
                    chargenRouter,
                    foundation.SceneLighting!,
                    foundation.TextureCache,
                    foundation.MeshAdapter!,
                    cam: chargenCam),
                static val => val.Dispose());
            var earlierChargenPainter = chargenViewRect.Painter;
            chargenViewRect.Painter = chargenPreviewTenancy.Resource;
            mappings.AdoptFree(
                "chargen preview viewport target",
                () =>
                {
                    if (ReferenceEquals(chargenViewRect.Painter, chargenPreviewTenancy.Resource))
                        chargenViewRect.Painter = earlierChargenPainter;
                });

            var chargenRegistry = new MacAC.Assets.CharGen.GenesisLookCatalog(substance.Dats);
            chargenPreviewDriver = new ChargenPreviewDriver(
                chargenPreviewTenancy.Resource,
                chargenCam,
                new CanonChargenPreviewFrameView(
                    chargenViewRect,
                    new CanonChargenPreviewPageVisibility(dealing.RetainedUi.Runtime)),
                substance.Dats,
                substance.AnimationLoader,
                chargenRegistry,
                chargenRegistry,
                dependencies.DatLock);
            dealing.RetainedUi.Runtime.ChargenPreviewControl = chargenPreviewDriver;
            dealing.RetainedUi.Runtime.ChargenPalSetSrc = chargenRegistry;
            dealing.RetainedUi.Runtime.ChargenClothingChartSrc = chargenRegistry;
            dealing.RetainedUi.Runtime.ChargenSwatchTintSrc = chargenRegistry;
            ChargenTintSpotComposer chargenSwatchTextures = new MacAC.Client.Shell.Panels.ChargenTintSpotComposer(
                substance.Dats, foundation.TextureCache);
            dealing.RetainedUi.Runtime.ChargenSwatchTextureSrc = chargenSwatchTextures;
            mappings.AdoptFree(
                "chargen preview control",
                () =>
                {
                    if (ReferenceEquals(
                            dealing.RetainedUi.Runtime.ChargenPreviewControl,
                            chargenPreviewDriver))

                        dealing.RetainedUi.Runtime.ChargenPreviewControl = null;
                });
        }
        else if (routerTenancy.Resource is not null && dealing.RetainedUi is not null)
        {
            Console.WriteLine(
                "[UI] chargen preview viewport not available at composition "
                + "time - the Appearance page's zoom/rotate controls and "
                + "3D preview will not function this session");
        }

        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            ChargenPreviewPainter>? summaryPreviewTenancy = null;
        ChargenPreviewDriver? summaryPreviewDriver = null;
        if (routerTenancy.Resource is { } summaryRouter
            && dealing.RetainedUi?.Runtime.SummaryPreviewViewRectWidget is { } summaryViewRect)
        {
            ClientChargenPreviewCamera summaryCam = new ClientChargenPreviewCamera();
            summaryPreviewTenancy = ambit.Acquire(
                "summary preview viewport",
                () => new ChargenPreviewPainter(
                    realmPassAmbit
                        ?? throw new InvalidOperationException(
                            "The graphics backend must publish a world pass scope"),
                    hub.GpuDevice,
                    hub.GpuFrameLifetime,
                    summaryRouter,
                    foundation.SceneLighting!,
                    foundation.TextureCache,
                    foundation.MeshAdapter!,
                    cam: summaryCam,
                    rasterizeIdent: MacAC.Client.Graphics.ChargenPreviewActorAssembler.SummaryPreviewRasterizeIdent,
                    backdropRasterizeIdent: MacAC.Client.Graphics.ChargenPreviewActorAssembler.SummaryPreviewBackdropRasterizeIdent),
                static val => val.Dispose());
            var earlierSummaryPainter = summaryViewRect.Painter;
            summaryViewRect.Painter = summaryPreviewTenancy.Resource;
            mappings.AdoptFree(
                "summary preview viewport target",
                () =>
                {
                    if (ReferenceEquals(summaryViewRect.Painter, summaryPreviewTenancy.Resource))
                        summaryViewRect.Painter = earlierSummaryPainter;
                });

            var summaryRegistry = new MacAC.Assets.CharGen.GenesisLookCatalog(substance.Dats);
            summaryPreviewDriver = new ChargenPreviewDriver(
                summaryPreviewTenancy.Resource,
                summaryCam,
                new CanonChargenPreviewFrameView(
                    summaryViewRect,
                    new CanonSummaryPreviewPageVisibility(dealing.RetainedUi.Runtime)),
                substance.Dats,
                substance.AnimationLoader,
                summaryRegistry,
                summaryRegistry,
                dependencies.DatLock,
                useZoomedOutEyePt: true,
                rasterizeIdent: MacAC.Client.Graphics.ChargenPreviewActorAssembler.SummaryPreviewRasterizeIdent,
                backdropRasterizeIdent: MacAC.Client.Graphics.ChargenPreviewActorAssembler.SummaryPreviewBackdropRasterizeIdent);
            dealing.RetainedUi.Runtime.SummaryPreviewControl = summaryPreviewDriver;
            mappings.AdoptFree(
                "summary preview control",
                () =>
                {
                    if (ReferenceEquals(
                            dealing.RetainedUi.Runtime.SummaryPreviewControl,
                            summaryPreviewDriver))

                        dealing.RetainedUi.Runtime.SummaryPreviewControl = null;
                });
        }
        else if (routerTenancy.Resource is not null && dealing.RetainedUi is not null)
        {
            Console.WriteLine(
                "[UI] summary preview viewport not available at composition "
                + "time - the Summary page's 3D preview will not function "
                + "this session");
        }
        Flaw(OnlineDisplayAssemblyPoint.PrivateCreatureViewportsCreated);

        BatchFrustum environChamberFrustum = new BatchFrustum();
        var environChamberTenancy = ambit.Acquire(
            "environment-cell renderer",
            () => new EnvironChamberPainter(
                hub.GpuDevice,
                hub.GpuFrameLifetime,
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope"),
                foundation.MeshAdapter!.TriMeshKeeper!,
                environChamberFrustum,
                foundation.TerrainAtlas?.SurroundingsSpecificsTexture ?? default,
                () => dependencies.Settings.ReadoutPreview.BuildingDetailTextures),
            static val => val.Dispose());
        Flaw(OnlineDisplayAssemblyPoint.EnvironmentCellsCreated);

        var landPainter = foundation.Terrain;
        EnvironChamberPainter? environChambers = environChamberTenancy.Resource;
        LandblockRenderHerald lbRasterizePublisher = new LandblockRenderHerald(
            (lbIdent, triMeshBlob, origin) =>
                landPainter?.AppendLbWithTriMesh(
                    lbIdent,
                    triMeshBlob,
                    origin),
            lbIdent => landPainter?.RemoveLandblock(lbIdent),
            dependencies.CellVisibility,
            realmPhase,
            readyEnvironChambers: assemble =>
            {
                if (foundation.MeshAdapter?.TriMeshKeeper is { } environChamberTriMeshes)
                    EnvCellMeshPreparationRota.Plan(assemble, environChamberTriMeshes);
            },
            dropEnvironChambers: lbIdent => environChambers?.RemoveLandblock(lbIdent),
            envCellPublisher: environChambers);
        LandblockKineticsHerald lbKineticsPublisher = new LandblockKineticsHerald(
            dependencies.EntityObjects.Physics,
            realm.TerrainBuild.HeightTable);
        var lbStaticPublisher =
            new LandblockStaticDisplayHerald(
                substance.LightingSink,
                dependencies.TranslucencyFades,
                dependencies.WorldGameState,
                dependencies.WorldEvents);
        var lbSunsetHolder =
            new LandblockDisplayRetirementOwner(
                lbRasterizePublisher,
                lbKineticsPublisher,
                lbStaticPublisher,
                substance.LightingSink,
                dependencies.TranslucencyFades);
        var lbFetched = new DeferredOnlineActorLandblockLoadedSink();
        LandblockDisplayPipeline lbPipe = new LandblockDisplayPipeline(
            lbRasterizePublisher,
            lbKineticsPublisher,
            lbStaticPublisher,
            realmPhase,
            lbSunsetHolder,
            lbFetched.OnLbLoaded,
            lbRasterizePublisher.ReadyFollowingRasterizePins,
            rasterizeTableauShade?.StaticProjections);
        Flaw(OnlineDisplayAssemblyPoint.LandblockPipelineCreated);

        var clipCycleTenancy = ambit.Acquire(
            "portal clip frame",
            ClipCycle.NoClip,
            static val => val.Dispose());
        var gatewayZDepthTenancy = ambit.Acquire(
            "portal depth mask",
            () => new GatewayZDepthBitmaskPainter(
                hub.GpuDevice,
                hub.GpuFrameLifetime,
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope")),
            static val => val.Dispose());
        void showGatewayPauseNotice(string phrase) =>
            dependencies.Runtime.CommunicationHolder.AddText(
                phrase,
                MacAC.Mechanics.Comms.CanonLogTextType.ClientLocal);
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            SpewBoxDriver>? spewBboxTenancy = null;
        if (dealing.RetainedUi is { } spewBboxKeptWidget)
        {
            var spewBboxHoldings = spewBboxKeptWidget.Runtime.Assets;
            var spewBboxTypeface =
                spewBboxHoldings.ResolveFont(SpewBoxDriver.CanonTypefaceIdent);
            spewBboxTenancy = ambit.Acquire(
                "spew box",
                () => new SpewBoxDriver(
                    spewBboxKeptWidget.Host.Root,
                    new SpewBoxModel(dependencies.Runtime.CommunicationHolder.SpewBox),
                    spewBboxTypeface,
                    spewBboxHoldings.DebugFont,
                    isGameplayEngaged: () => dependencies.Settings.IsGameplayReadout),
                static val => val.Dispose());
        }
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<
            PortalTunnelDisplay>? gatewayTunnelTenancy = null;
        if (routerTenancy.Resource is { } gatewayRouter)
        {
            PortalTunnelDisplay gatewayTunnel;
            try
            {
                gatewayTunnel = dependencies.PortalTunnelFallback.AcquirePrepared(
                    () => PortalTunnelDisplay.BuildNeeded(
                        realmPassAmbit
                            ?? throw new InvalidOperationException(
                                "The graphics backend must publish a world pass scope"),
                        hub.GpuFrameLifetime,
                        substance.Dats,
                        substance.AnimationLoader,
                        new MacAC.Client.Sound.WidgetDisplayHookSink(
                            dependencies.HookRouter,
                            substance.Audio?.HookSink),
                        gatewayRouter,
                        foundation.SceneLighting!,
                        foundation.MeshAdapter!,
showGatewayPauseNotice),
                    static tunnel => tunnel.ReadyAssetList());
            }
            catch (Exception acquisitionMiss)
            {
                try
                {
                    dependencies.PortalTunnelFallback.FreeBackup();
                }
                catch (Exception tidyMiss)
                {
                    throw new AggregateException(
                        "Portal-tunnel construction and fallback rollback both failed",
                        acquisitionMiss,
                        tidyMiss);
                }

                throw;
            }
            gatewayTunnelTenancy = ambit.Own(
                "portal tunnel fallback",
                gatewayTunnel,
                _ => dependencies.PortalTunnelFallback.FreeBackup());
        }
        Flaw(OnlineDisplayAssemblyPoint.PortalResourcesCreated);

        var heavensTenancy = ambit.Acquire(
            "sky renderer",
            () => new HeavensPainter(
                hub.GpuDevice,
                hub.GpuFrameLifetime,
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope"),
                substance.Dats,
                foundation.TextureCache)
            {
                AnimStageSecsOverride = dependencies.Options.SkyAnimationPhaseSeconds,
            },
            static val => val.Dispose());
        var moteTenancy = ambit.ObtainOptional(
            "particle renderer",
            () => new MotePainter(
                hub.GpuDevice,
                hub.GpuFrameLifetime,
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope"),
                substance.ParticleSystem,
                foundation.TextureCache,
                substance.Dats,
                foundation.MeshAdapter!,
                dependencies.RetailAlphaQueue,
                alphaTempBudgets.ParticleBytes),
            static val => val.Dispose());
        Flaw(OnlineDisplayAssemblyPoint.SkyAndParticlesCreated);

        IRenderFrameResourceTelemetrySource? assetTelemetry =
            dependencies.Options.UiProbeDump
            && routerTenancy.Resource is { } probeRouter
            && environChamberTenancy.Resource is { } probeEnvironChambers
            && moteTenancy.Resource is { } probeMotes
            && gatewayZDepthTenancy.Resource is { } probeGatewayZDepth
                ? new EngineRenderFrameResourceTelemetrySource(
                    substance.ParticleSystem,
                    substance.ParticleSink,
                    probeRouter,
                    probeEnvironChambers,
                    probeMotes,
                    dealing.RetainedUi?.Host.TextRenderer,
                    probeGatewayZDepth,
                    clipCycleTenancy.Resource,
                    foundation.Terrain!,
                    foundation.SceneLighting!,
                    foundation.MeshAdapter!,
                    foundation.TextureCache,
                    substance.PreparedAssets)
                : null;
        var cycleTelemetry = new RenderFrameTelemetryDriver(
            new EngineRenderFrameTitleFactsSource(
                realmPhase,
                dependencies.AnimatedEntities,
                dependencies.WorldTime),
            new SilkRasterizeCycleBannerDrain(dependencies.Window),
            dependencies.RenderDiagnosticLog,
            dependencies.Options.UiProbeDump,
            assetTelemetry,
            dependencies.RenderPackDiagnostics);
        if (dependencies.DevFrameDiagnostics is { } devCycleTelemetry)
        {
            mappings.Adopt(
                "developer frame diagnostics",
                devCycleTelemetry.BindOwned(cycleTelemetry));
        }
        mappings.Adopt(
            "retained-UI frame diagnostics",
            dependencies.UiFrameDiagnostics.BindOwned(cycleTelemetry));
        Flaw(OnlineDisplayAssemblyPoint.DiagnosticsBound);

        var mappingsTenancy = ambit.Own(
            "live-presentation runtime bindings",
            mappings,
            static val => val.Dispose());
        mappingsPossessedByAmbit = true;
        OnlineDisplayResult outcome = new OnlineDisplayResult(
            moduleLifecycle,
            locomotionCore,
            ancestorAcceptance,
            actorSummonBridge,
            actorProgramActivator,
            staticAnimScheduler,
            realmPassage,
            realmReadiness,
            realmPhase,
            rasterizeTableauShade,
            onlineActors,
            stanceProj,
            ownAvatarShadeSynchronizer,
            missileDriver,
            projWithdrawal,
            lampsTenancy.Resource,
            animScheduler,
            animPresenter,
            equippedTenancy.Resource,
            actorFxList,
            exhibitTenancy.Resource,
            routerTenancy.Resource,
            pickTableau,
            pickAsk,
            pickInteractions,
            keptGameplayTenancy?.Resource,
            paperdollTenancy?.Resource,
            paperdollPresenter,
            beastAppraisalTenancy?.Resource,
            beastAppraisalPresenter,
            chargenPreviewTenancy?.Resource,
            chargenPreviewDriver,
            summaryPreviewTenancy?.Resource,
            summaryPreviewDriver,
            environChamberFrustum,
            environChamberTenancy.Resource,
            lbPipe,
            clipCycleTenancy.Resource,
            gatewayZDepthTenancy.Resource,
            heavensTenancy.Resource,
            moteTenancy.Resource,
            cycleTelemetry,
            mappings,
            lbFetched);
        foundation.Residency.EnrollDomainSrc(
            new DelegateTenancyDomainSource(
                TenancyDomain.AlphaScratch,
                () => new TenancyDomainCapture(
                    TenancyDomain.AlphaScratch,
                    EntryCount: 3,
                    OwnerCount: 3,
                    Charges: new TenancyCharges(
                        ScratchBytes: checked(
                            dependencies.RetailAlphaQueue.KeptTempOctets
                            + (routerTenancy.Resource?.KeptAlphaTempOctets ?? 0)
                            + (moteTenancy.Resource?.KeptAlphaTempOctets ?? 0))),
                    BudgetBytes: alphaTempBudgets.SumOctets)));
        _bulletin.PublishLivePresentation(outcome);

        onlineCoreTenancy.Transfer();
        rasterizeTableauShadeTenancy?.Transfer();
        lampsTenancy.Transfer();
        equippedTenancy.Transfer();
        exhibitTenancy.Transfer();
        routerTenancy.Transfer();
        keptGameplayTenancy?.Transfer();
        paperdollTenancy?.Transfer();
        beastAppraisalTenancy?.Transfer();
        chargenPreviewTenancy?.Transfer();
        summaryPreviewTenancy?.Transfer();
        environChamberTenancy.Transfer();
        clipCycleTenancy.Transfer();
        gatewayZDepthTenancy.Transfer();
        gatewayTunnelTenancy?.Transfer();
        spewBboxTenancy?.Transfer();
        heavensTenancy.Transfer();
        moteTenancy.Transfer();
        mappingsTenancy.Transfer();
        Flaw(OnlineDisplayAssemblyPoint.ResultPublished);
        ambit.Complete();
        return outcome;
    }
}

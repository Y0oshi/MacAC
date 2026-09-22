using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Kinetics;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Rigging;

internal sealed partial class OnlineDisplayAssemblyPhase
{
    public OnlineDisplayResult Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, Silk.NET.Input.IInputContext> platform,
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(dealing);
        return !ReferenceEquals(_deps.Graphics, platform.Graphics)
            ? throw new InvalidOperationException(
                "Live-presentation dependencies do not match the ordered platform result")
            : ComposeCore(hub, substance, realm, dealing);
    }

    private OnlineDisplayResult ComposeCore(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing)
    {
        var dependencies = _deps;
        var foundation = realm.Foundation;
        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        OnlineDisplayEngineWiring? mappings = null;
        bool mappingsPossessedByAmbit = false;

        try
        {
            AssemblyAcquisitionScope.AssemblyAcquisitionLease<
                RenderStageShadeEngine>? rasterizeTableauShadeTenancy = null;
            rasterizeTableauShadeTenancy = ambit.Acquire(
                "render scene",
                () => new RenderStageShadeEngine(
                    RenderStageEpoch.FromRaw(1)),
                static val => val.Dispose());
            var rasterizeTableauShade =
                rasterizeTableauShadeTenancy?.Resource;

            var moduleLifecycle =
                new DeferredOnlineActorEngineComponentLifespan();
            LandblockSpawnBridge wbSummonBridge = new LandblockSpawnBridge(
                foundation.MeshAdapter
                ?? throw new InvalidOperationException(
                    "The landblock spawn ledger needs the mesh pipeline, which "
                    + "has to be available prior to the ledger is composed"));
            RigSpec? PullReadiedRig(uint srcIdent)
            {
                return !substance.Dats.TryLocatePreferred(
                        srcIdent,
                        out IDatDatabase? database,
                        out RecordKind kind)
                    || kind != RecordKind.Setup
                    ? null
                    : database.TryGet<RigSpec>(
                    srcIdent,
                    out RigSpec? rig)
                    ? rig
                    : null;
            }

            PreparedSetupPicker rigLocator = new PreparedSetupPicker(
                substance.PreparedAssets,
                PullReadiedRig,
                msg => Console.Error.WriteLine(
                    $"setup activation: {msg}"));

            AnimSequencer SchedulerMaker(RealmActor actor)
            {
                if (rigLocator.TryResolve(
                        actor.SrcGfxObjRefOrRigIdent,
                        out RigSpec? rig))
                {
                    uint locomotionChartIdent = (uint)rig.DefaultMotionBookId;
                    return locomotionChartIdent is not 0
                        && substance.Dats.Get<MotionBook>(locomotionChartIdent) is { } locomotionChart
                        ? new AnimSequencer(
                            rig,
                            locomotionChart,
                            substance.AnimationLoader)
                        : new AnimSequencer(
                        rig,
                        new MotionBook(),
                        substance.AnimationLoader);
                }

                return new AnimSequencer(
                    new RigSpec(),
                    new MotionBook(),
                    NullAnimFetcher.Instance);
            }

            ActorSummonBridge actorSummonBridge = new ActorSummonBridge(
                foundation.TextureCache,
                SchedulerMaker,
                foundation.MeshAdapter);
            ActorEffectDriver? actorFxList = null;
            OnlineActorCore? onlineActors = null;
            var staticTrunkCommitter = new StaticOnlineRootCommitter(
                dependencies.RuntimeSlot,
                dependencies.KineticEngine.ShadeObjects,
                dependencies.WorldOrigin,
                dependencies.EffectPoses);
            var staticResidency = new OnlineStaticMotionTenancy(dependencies.RuntimeSlot);
            (OnlineActorMotionLedger Animation, KineticBody Body)?
                LocateOnlineStaticHolder(RealmActor actor)
            {
                return actor.ServerGuid is 0
                    || onlineActors?.TryFetchRecord(
                        actor.ServerGuid,
                        out OnlineActorRecord capture) != true
                    || !ReferenceEquals(capture.WorldEntity, actor)
                    || !capture.IsSpatiallyProjected
                    || !capture.IsSpatiallyVisible
                    || capture.AnimationRuntime
                        is not OnlineActorMotionLedger anim
                    || capture.KineticBody is not { } corpus
                    ? null
                    : (anim, corpus);
            }
            var staticAnimScheduler =
                new CanonStaticAnimatingObjectRota(
                    substance.AnimationLoader,
                    substance.AnimationHookFrames.Capture,
                    dependencies.EffectPoses.Publish,
                    staticResidency.IsHoused,
                    (actor, body) => _ = staticTrunkCommitter.Seal(actor, body),
                    staticResidency.ProjVer,
                    LocateOnlineStaticHolder);

            ProgramActivationDetails? LocateActivation(RealmActor actor)
            {
                if (!rigLocator.TryResolve(
                        actor.SrcGfxObjRefOrRigIdent,
                        out RigSpec? rig))

                    return null;
                uint programIdent = rig.DefaultEffectId;
                if (actor.IndexedPieceXforms.Count is 0)
                {
                    var indexed = IndexedSetupPartPoseAssembler.Build(rig, actor);
                    actor.AssignIndexedPiecePostures(indexed.Poses, indexed.Available);
                }

                bool usesStaticAnimWorkset = actor.ServerGuid is 0
                    || (onlineActors?.TryFetchRecord(
                            actor.ServerGuid,
                            out OnlineActorRecord onlineCapture) == true
                        && (onlineCapture.FinalKineticsPhase
                            & KineticStateFlags.Static) != 0);
                return new ProgramActivationDetails(
                    programIdent,
                    actor.IndexedPieceXforms,
                    ActorEffectProfile.BuildDatStatic(rig),
                    actor.IndexedPieceOnHand,
                    rig,
                    (uint)rig.DefaultClipId,
                    usesStaticAnimWorkset);
            }

            ActorProgramActivator actorProgramActivator = new ActorProgramActivator(
                substance.ScriptRunner,
                substance.ParticleSink,
                dependencies.EffectPoses,
                LocateActivation,
                (holderIdent, actor, profile) =>
                    actorFxList?.OnDatStaticActorPrimed(holderIdent, actor, profile),
                holderIdent =>
                {
                    actorFxList?.OnDatStaticActorRemoved(holderIdent);
                    substance.LightingSink.WithdrawHolder(holderIdent);
                    dependencies.TranslucencyFades.WipeActor(holderIdent);
                },
                (actor, details) => staticAnimScheduler.Register(actor, details),
                staticAnimScheduler.Unregister,
                (actor, details) => staticAnimScheduler.Rebind(actor, details));

            var realmPassage = dependencies.RealmPassage;
            var realmReadiness =
                new RealmEpochAvailabilityLedger(realmPassage);
            GpuRealmPhase realmPhase = new GpuRealmPhase(
                wbSummonBridge,
                dependencies.ClassificationCache.DirtyLb,
                actorProgramActivator,
                realmReadiness);
            mappings = new OnlineDisplayEngineWiring();
            if (dependencies.DevWorldEntities is { } devRealmActors)
            {
                mappings.Adopt(
                    "developer world-entity source",
                    devRealmActors.BindOwned(realmPhase));
            }
            var assetHolders =
                new List<CompositeOnlineActorResourceLifespan.OwnerDef>
                {
                    new(
                        actor =>
                        {
                            if (onlineActors is null
                                || !onlineActors.TryFetchCaptureByOwnActorIdent(
                                    actor.Id,
                                    out OnlineActorRecord capture)
                                || capture.ProjTag is not { } tag)
                            {
                                throw new InvalidOperationException(
                                    "Live presentation registration needs an exact Runtime projection key");
                            }
                            _ = actorSummonBridge.OnCreate(tag, actor);
                        },
                        actor => _ = actorSummonBridge.OnRemove(actor)),
                    new(
                        actorProgramActivator.OnBuild,
                        actorProgramActivator.OnDrop),
                };
            if (rasterizeTableauShade is not null)
            {
                assetHolders.Add(new(
                    rasterizeTableauShade.OnLiveAssetRegistered,
                    rasterizeTableauShade.OnLiveAssetUnregistered));
            }
            onlineActors = new OnlineActorCore(
                realmPhase,
                new CompositeOnlineActorResourceLifespan(
                    [.. assetHolders]),
                moduleLifecycle,
                dependencies.EntityObjects);
            var onlineCoreTenancy = ambit.Own(
                "canonical live-entity runtime",
                onlineActors,
                static core => core.Clear());
            var onlineRasterizeProjections =
                rasterizeTableauShade?.AttachOnlineCore(
                    onlineActors,
                    new GpuRealmRenderTraversalOrderSource(realmPhase),
                    dependencies.PlayerIdentity);
            Flaw(OnlineDisplayAssemblyPoint.CanonicalRuntimeCreated);

            mappings.Adopt(
                "canonical live-runtime slot",
                dependencies.RuntimeSlot.BindOwned(onlineActors));
            Flaw(OnlineDisplayAssemblyPoint.CanonicalRuntimeBound);

            var pickDealingSrc =
                new DeferredPickingDealingSource();
            var locomotionCore = new OnlineActorMotionEngineDriver(
                onlineActors,
                dependencies.KineticAssetCache,
                () => pickDealingSrc.Current,
                dependencies.Selection,
                dependencies.WorldOrigin);
            mappings.Adopt(
                "live motion runtime",
                dependencies.MotionBindings.BindOwned(locomotionCore));
            Flaw(OnlineDisplayAssemblyPoint.MotionRuntimeBound);

            void wbVis(OnlineActorRecord record, bool shown)
            {
                if (record.WorldEntity is { } entity)
                    actorSummonBridge.AssignExhibitHoused(entity, shown);
            }
            mappings.AttachProjVis(
                onlineActors,
wbVis,
                "WB projection visibility");
            if (onlineRasterizeProjections is not null)
            {
                mappings.AttachProjVis(
                    onlineActors,
                    onlineRasterizeProjections.OnProjVisAltered,
                    "shadow render projection visibility");
            }
            void moteVis(OnlineActorRecord record, bool shown)
            {
                if (record.WorldEntity is { } entity)
                    substance.ParticleSink.AssignActorExhibitShown(entity.Id, shown);
            }
            mappings.AttachProjVis(
                onlineActors,
moteVis,
                "particle projection visibility");
            var stanceVisDrains = new List<
                Action<OnlineActorRecord, bool>>(4)
            { wbVis,
            };
            if (onlineRasterizeProjections is not null)
            {
                stanceVisDrains.Add(
                    onlineRasterizeProjections.OnProjVisAltered);
            }
            stanceVisDrains.Add(moteVis);
            stanceVisDrains.Add((record, shown) =>
            {
                if (shown)
                    actorFxList?.OnExhibitTied(record);
            });
            var ownAvatarShadeSynchronizer = new OwnAvatarShadeSyncer(
                dependencies.KineticEngine,
                onlineActors,
                dependencies.PlayerIdentity,
                dependencies.WorldOrigin,
                dependencies.LocalPlayerShadow);
            var stanceProj = new EnginePlacementDisplaySink(
                onlineActors,
                realmPassage,
                dependencies.WorldGameState,
                dependencies.WorldEvents,
                dependencies.EffectPoses,
                ownAvatarShadeSynchronizer,
                () => dependencies.PlayerIdentity.SrvOid,
                oid =>
                {
                    if (dependencies.Selection.ChosenObjectTag == oid)
                    {
                        dependencies.Selection.Clear(
                            PickChangeSource.System,
                            PickChangeReason.SelectedObjectRemoved);
                    }
                },
                stanceVisDrains);
            Flaw(OnlineDisplayAssemblyPoint.ProjectionVisibilityBound);

            MissileDriver missileDriver = new MissileDriver(
                onlineActors,
                new DatProjectileSetupPicker(substance.Dats, dependencies.DatLock),
                new ActorRootPoseHerald(dependencies.EffectPoses),
                dependencies.WorldOrigin)
            {
                DiagnosticSink = msg =>
                    Console.Error.WriteLine($"projectile: {msg}"),
            };
            var projWithdrawal =
                new OnlineActorMirrorWithdrawalDriver(
                    onlineActors,
                    missileDriver,
                    dependencies.WorldGameState,
                    dependencies.WorldEvents,
                    dependencies.KineticEngine.ShadeObjects,
                    dependencies.EffectPoses,
                    dependencies.LocalPlayerShadow);
            var lampsTenancy = ambit.Acquire(
                "live-entity lights",
                () => new OnlineActorLightDriver(
                    onlineActors,
                    dependencies.EffectPoses,
                    substance.LightingSink,
                    rigIdent => substance.Dats.Get<RigSpec>(rigIdent)),
                static val => val.Dispose());
            var plainKineticsUpdater = new OnlineActorOrdinaryKineticsPulser(
                dependencies.EntityObjects.Physics,
                dependencies.MotionBindings.GetSetupCylinder,
                dependencies.MotionBindings.FetchRigCarrierForm,
                oid => MacAC.Mechanics.Kinetics.EntityContactFlagsExt.LocateCarrierPvpPhase(
                    dependencies.EntityObjects.Objects,
                    oid));
            OnlineActorMotionRota animScheduler = new OnlineActorMotionRota(
                onlineActors,
                dependencies.PlayerIdentity,
                dependencies.RemotePhysicsUpdater,
                plainKineticsUpdater,
                missileDriver,
                new ActorRootPoseHerald(dependencies.EffectPoses),
                new MotionHookCaptureSink(substance.AnimationHookFrames));
            var animPresenter = new OnlineActorMotionExhibitor(
                onlineActors,
                staticAnimScheduler,
                dependencies.EffectPoses,
                new OnlineMotionDisplayScope(
                    onlineActors,
                    dependencies.PlayerIdentity,
                    dependencies.PlayerDriver),
                dependencies.AnimationDiagnostics,
                dependencies.Options.HidePartIndex);
            var ancestorAcceptance = new DeferredOnlineActorParentAcceptance();
            var equippedTenancy = ambit.Acquire(
                "equipped-child renderer",
                () => new EquippedChildRenderDriver(
                    substance.Dats,
                    dependencies.DatLock,
                    dependencies.EntityObjects.Objects,
                    onlineActors,
                    dependencies.EffectPoses,
                    ancestorAcceptance.TryAdmit,
                    (descendantCapture, locusVer, projVer) =>
                        projWithdrawal.WithdrawPrecise(
                            descendantCapture,
                            locusVer,
                            projVer,
                            dependencies.PlayerIdentity.SrvOid),
                    dependencies.KineticEngine.ShadeObjects,
                    dependencies.KineticAssetCache,
                    substance.CollisionAssets.StashGfxObjRef),
                static val => val.Dispose());
            Flaw(OnlineDisplayAssemblyPoint.CorePresentationCreated);

            KineticScriptTableLookup chartLocator = new KineticScriptTableLookup(
                ident => substance.Dats.Get<EffectBook>(ident));
            actorFxList = new ActorEffectDriver(
                onlineActors,
                substance.ScriptRunner,
                chartLocator,
                dependencies.EffectPoses,
                (ancestorOwnIdent, pieceOrdinal) =>
                    equippedTenancy.Resource.SeekDescendantOwnIdentAtPiece(
                        ancestorOwnIdent,
                        pieceOrdinal),
                equippedTenancy.Resource.SeekAncestorOwnIdent,
                holderIdent => substance.Audio?.EntitySoundTables.Remove(holderIdent),
                (holderIdent, sfxChartDid) =>
                {
                    substance.Audio?.EntitySoundTables.Remove(holderIdent);
                    if (sfxChartDid is { } did)
                        substance.Audio?.EntitySoundTables.Set(holderIdent, did);
                },
                (holderIdent, realmLocus, sfxKind, wireVolume) =>
                    substance.Audio?.HookSink?.PlaySrvSfx(
                        holderIdent,
                        realmLocus,
                        sfxKind,
                        wireVolume));
            mappings.Adopt(
                "entity-effect advance",
                dependencies.EffectAdvance.BindOwned(actorFxList));
            actorFxList.DiagnosticSink = msg =>
                Console.Error.WriteLine($"vfx: {msg}");
            var pieceArrLifecycle = new OnlineActorPartArrayLifespan(
                dependencies.AnimatedEntities);
            var exhibitTenancy = ambit.Acquire(
                "live-entity presentation",
                () => new OnlineActorDisplayDriver(
                    onlineActors,
                    dependencies.KineticEngine.ShadeObjects,
                    actorFxList.PlayTypedFromConcealedChangeover,
                    new OnlineActorPartArrayEnterRealmPort(
                        pieceArrLifecycle.HandleEnterWorld),
                    equippedTenancy.Resource.AssignStraightDescendantsNoPaint,
                    dependencies.MotionBindings.ClearTargetForHiddenEntity,
                    dependencies.WorldOrigin.FetchMiddle),
                static val => val.Dispose());
            mappings.Adopt(
                "live-entity pvp bitfield sync",
                new OnlineActorPvpBitfieldSync(
                    dependencies.EntityObjects.Objects,
                    onlineActors,
                    dependencies.KineticEngine.ShadeObjects));
            mappings.AttachProjPosturePrimed(
                equippedTenancy.Resource,
                lampsTenancy.Resource.OnAffixedPosturePrimed);
            if (onlineRasterizeProjections is not null)
            {
                mappings.AttachProjPosturePrimed(
                    equippedTenancy.Resource,
                    onlineRasterizeProjections.OnProjPosturePrimed);
                mappings.AttachProjRemoved(
                    equippedTenancy.Resource,
                    onlineRasterizeProjections.OnProjRemoved);
            }
            mappings.Adopt(
                "entity-effect animation hooks",
                substance.HookRegistrations.EnrollPossessed(actorFxList));
            Flaw(OnlineDisplayAssemblyPoint.EffectRoutingBound);

            return CompletePresentation(
                hub,
                substance,
                realm,
                dealing,
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
                rasterizeTableauShadeTenancy,
                onlineActors,
                stanceProj,
                ownAvatarShadeSynchronizer,
                missileDriver,
                projWithdrawal,
                lampsTenancy,
                animScheduler,
                animPresenter,
                equippedTenancy,
                actorFxList,
                exhibitTenancy,
                pickDealingSrc,
                mappings,
                ambit,
                ref mappingsPossessedByAmbit,
                onlineCoreTenancy);
        }
        catch (Exception miss)
        {
            if (mappings is not null && !mappingsPossessedByAmbit)
            {
                ambit.Own(
                    "live-presentation runtime bindings",
                    mappings,
                    static val => val.Dispose());
                mappingsPossessedByAmbit = true;
            }

            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private void Flaw(OnlineDisplayAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}

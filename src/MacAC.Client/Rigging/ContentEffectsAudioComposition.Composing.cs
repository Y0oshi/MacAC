using MacAC.Assets;
using MacAC.Client.Sound;
using MacAC.Mechanics.Kinetics;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed partial class ContentEffectsAudioAssemblyPhase
{
    public SubstanceFxListSoundOutcome Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        HubFeedCameraOutcome hub)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hub);

        AssemblyAcquisitionScope primaryAmbit = new AssemblyAcquisitionScope();
        try
        {
            IDatAccess datFiles = primaryAmbit.Acquire(
                "DAT collection",
                () => _maker.OpenDatCollection(_deps.DatDirectory),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishDatCollection);
            Flaw(ContentEffectsAudioAssemblyPoint.DatCollectionPublished);

            var readiedHoldings = primaryAmbit.Acquire(
                "prepared asset source",
                () => _maker.OpenReadiedAssetSrc(
                    _deps.PreparedAssetPath,
                    _deps.PreparedAssetOverlayPath,
                    _deps.PreparedAssetBaseRecipeVersion,
                    _deps.PreparedAssetEffectiveRecipeVersion,
                    datFiles,
                    _deps.Error),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishPreparedAssetSource);
            Flaw(ContentEffectsAudioAssemblyPoint.PreparedAssetSourcePublished);
            _deps.Log(
                $"prepared assets: opened '{_deps.PreparedAssetPath}'");

            var magic = _maker.PullMagicRegistry(datFiles);
            _bulletin.PublishMagicCatalog(magic);
            Flaw(ContentEffectsAudioAssemblyPoint.MagicCatalogPublished);
            _maker.SetupArcanumMetadata(_deps.Character, magic);
            _deps.Log(
                $"spells: loaded {_maker.FetchArcanumTally(magic)} entries from portal.dat");
            Flaw(ContentEffectsAudioAssemblyPoint.SpellMetadataInstalled);

            var chargen = _maker.PullChargenKnobs(datFiles);
            _maker.SetupChargenKnobs(_deps.Session, chargen);
            _deps.Log(
                $"chargen: loaded {chargen.HeritagesById.Count} heritage(s) from portal.dat");
            Flaw(ContentEffectsAudioAssemblyPoint.ChargenOptionsInstalled);

            IAnimReader anims = _maker.BuildAnimFetcher(
                datFiles,
                _deps.ResidencyBudgets.AnimationBytes,
                _deps.ResidencyBudgets.AnimationEntries);
            _bulletin.PublishAnimationLoader(anims);
            Flaw(ContentEffectsAudioAssemblyPoint.AnimationLoaderPublished);

            var impact = _maker.BuildImpactBuilder(
                _deps.KineticAssetCache,
                datFiles,
                anims,
                _deps.DumpMotionEnabled);
            _bulletin.PublishLiveEntityCollisionBuilder(impact);
            Flaw(ContentEffectsAudioAssemblyPoint.CollisionBuilderPublished);
            var impactHoldings =
                _maker.BuildOnlineImpactAssetPublisher(
                    _deps.KineticAssetCache,
                    readiedHoldings);

            var spouts = _maker.BuildSpoutRegistry(datFiles);
            _bulletin.PublishEmitterRegistry(spouts);
            Flaw(ContentEffectsAudioAssemblyPoint.EmitterRegistryPublished);

            var motes = _maker.BuildMoteSys(spouts);
            _bulletin.PublishParticleSystem(motes);
            Flaw(ContentEffectsAudioAssemblyPoint.ParticleSystemPublished);

            var moteDrain = _maker.BuildMoteDrain(
                motes,
                _deps.EffectPoses);
            moteDrain.DiagnosticSink = msg =>
                _deps.Error($"vfx: {msg}");
            _bulletin.PublishParticleSink(moteDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.ParticleSinkPublished);

            var tapCycles =
                _maker.BuildAnimTapCycles(
                    _deps.HookRouter,
                    _deps.EffectPoses);
            _bulletin.PublishAnimationHookFrames(tapCycles);
            Flaw(ContentEffectsAudioAssemblyPoint.AnimationHookFramesPublished);

            var registrations = primaryAmbit.Acquire(
                "animation hook registrations",
                () => _maker.BuildTapRegistrations(_deps.HookRouter),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishHookRegistrations);
            Flaw(ContentEffectsAudioAssemblyPoint.HookRegistrationsPublished);
            registrations.Register(moteDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.ParticleHookRegistered);

            var programFetcher =
                _maker.BuildKineticsProgramFetcher(datFiles);
            _bulletin.PublishPhysicsScriptLoader(programFetcher);
            Flaw(ContentEffectsAudioAssemblyPoint.PhysicsScriptLoaderPublished);

            var programRunner = _maker.BuildKineticsProgramRunner(
                programFetcher,
                _deps.HookRouter,
                _deps.EntityEffectAdvance);
            _bulletin.PublishPhysicsScriptRunner(programRunner);
            Flaw(ContentEffectsAudioAssemblyPoint.PhysicsScriptRunnerPublished);

            var illuminationDrain = _maker.BuildIlluminationDrain(
                _deps.Lighting,
                _deps.EffectPoses);
            _bulletin.PublishLightingSink(illuminationDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.LightingSinkPublished);
            registrations.Register(illuminationDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.LightingHookRegistered);

            var seeThroughDrain =
                _maker.BuildSeeThroughDrain(_deps.TranslucencyFades);
            _bulletin.PublishTranslucencySink(seeThroughDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.TranslucencySinkPublished);
            registrations.Register(seeThroughDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.TranslucencyHookRegistered);

            SubstanceSoundGraph? sound = _deps.NoAudio
                ? null
                : ConstructOptionalSound(datFiles, registrations);

            primaryAmbit.Complete();
            return new SubstanceFxListSoundOutcome(
                datFiles,
                readiedHoldings,
                magic,
                anims,
                impact,
                impactHoldings,
                spouts,
                motes,
                moteDrain,
                tapCycles,
                programFetcher,
                programRunner,
                illuminationDrain,
                seeThroughDrain,
                registrations,
                sound);
        }
        catch (Exception miss)
        {
            primaryAmbit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private SubstanceSoundGraph? ConstructOptionalSound(
        IDatAccess datFiles,
        MotionHookRegistrationSet registrations)
    {
        AssemblyAcquisitionScope soundAmbit = new AssemblyAcquisitionScope();
        SubstanceSoundGraph graph;
        AssemblyAcquisitionScope.AssemblyAcquisitionLease<OpenAlSoundEngine>
            engineTenancy;
        try
        {
            var stash = _maker.BuildSfxStash(
                datFiles,
                _deps.ResidencyBudgets.AudioBytes);
            Flaw(ContentEffectsAudioAssemblyPoint.SoundCacheCreated);
            engineTenancy = soundAmbit.Acquire(
                "OpenAL audio engine",
                _maker.BuildSoundEngine,
                static val => val.Dispose());
            var engine = engineTenancy.Resource;
            Flaw(ContentEffectsAudioAssemblyPoint.AudioEngineCreated);
            var sfxCharts =
                _maker.BuildActorSfxCharts();
            Flaw(ContentEffectsAudioAssemblyPoint.EntitySoundTablesCreated);
            SoundTapDrain? drain = null;
            WidgetSoundDriver? widgetSfxList = null;
            AmbientSoundDriver? ambient = null;
            if (engine.IsAvailable)
            {
                drain = _maker.BuildSoundDrain(engine, stash, sfxCharts);
                Flaw(ContentEffectsAudioAssemblyPoint.AudioSinkCreated);
                widgetSfxList = _maker.BuildWidgetSfxList(engine, stash, datFiles);
                _deps.Log(
                    widgetSfxList.ChartDid is 0
                        ? "audio: UI sound bank unresolved (no enum chain in dats) "
                          + "- interface cues silent"
                        : $"audio: UI sound bank = 0x{widgetSfxList.ChartDid:X8}");
                Flaw(ContentEffectsAudioAssemblyPoint.UiSoundsCreated);
                ambient = _maker.BuildAmbient(engine, stash);
                Flaw(ContentEffectsAudioAssemblyPoint.AmbientCreated);
            }

            graph = new SubstanceSoundGraph(
                stash, engine, sfxCharts, drain, widgetSfxList, ambient);
        }
        catch (Exception miss)
        {
            try
            {
                soundAmbit.RevertAndThrow(miss);
            }
            catch (Exception rolledBack) when (!HasIncompleteTidy(rolledBack))
            {
                _deps.Log(
                    $"audio: init failed: {rolledBack.Message} â€” audio disabled");
                return null;
            }

            throw new System.Diagnostics.UnreachableException();
        }

        try
        {
            _bulletin.PublishAudio(graph);
            engineTenancy.Transfer();
            soundAmbit.Complete();
        }
        catch (Exception miss)
        {
            soundAmbit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }

        Flaw(ContentEffectsAudioAssemblyPoint.AudioPublished);
        if (graph.HookSink is { } soundDrain)
        {
            registrations.Register(soundDrain);
            Flaw(ContentEffectsAudioAssemblyPoint.AudioHookRegistered);
            _deps.Log("audio: OpenAL engine ready (16 voices, 3D positional)");
        }
        else
        {
            _deps.Log(
                "audio: OpenAL unavailable (driver missing / headless) â€” audio disabled");
        }

        return graph;
    }
}

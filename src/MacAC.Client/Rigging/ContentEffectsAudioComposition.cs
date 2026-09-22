using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Vfx;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Kinetics;
using MacAC.Client.Sound;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Sound;
using MacAC.Sim;
using MacAC.Sim.Presence;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed record SubstanceSoundGraph(
    DatWaveCache SoundCache,
    OpenAlSoundEngine Engine,
    DictionaryActorSoundChart EntitySoundTables,
    SoundTapDrain? HookSink,
    WidgetSoundDriver? UiSounds,
    AmbientSoundDriver? Ambient);

internal sealed record SubstanceFxListSoundOutcome(
    IDatAccess Dats,
    IBakedAssetSource PreparedAssets,
    ArcanaCatalog ArcanaCatalog,
    IAnimReader AnimationLoader,
    OnlineActorContactAssembler CollisionBuilder,
    OnlineContactAssetHerald CollisionAssets,
    EmitterSpecRegistry EmitterRegistry,
    MoteSys ParticleSystem,
    ParticleHookTap ParticleSink,
    MotionHookFrameQueue AnimationHookFrames,
    CanonKineticScriptLoader PhysicsScriptLoader,
    KineticScriptRunner ScriptRunner,
    LightingHookTap LightingSink,
    SeeThroughHookTap TranslucencySink,
    MotionHookRegistrationSet HookRegistrations,
    SubstanceSoundGraph? Audio);

internal sealed record SubstanceFxListSoundDeps(
    string DatDirectory,
    string PreparedAssetPath,
    string? PreparedAssetOverlayPath,
    uint? PreparedAssetBaseRecipeVersion,
    uint? PreparedAssetEffectiveRecipeVersion,
    TenancyAllowanceKnobs ResidencyBudgets,
    KineticAssetCache KineticAssetCache,
    bool DumpMotionEnabled,
    SimCore Runtime,
    AnimHookRouter HookRouter,
    ActorEffectPoseRegistry EffectPoses,
    DeferredActorEffectAdvanceSource EntityEffectAdvance,
    LightKeeper Lighting,
    SeeThroughFadeKeeper TranslucencyFades,
    bool NoAudio,
    Action<string> Log,
    Action<string> Error)
{
    public SimToonLedger Character => Runtime.ToonHolder;

    public OnlineSessionDriver Session => Runtime.Session;
}

internal interface IPlayPaneSubstanceFxListSoundBulletin
{
    void PublishDatCollection(IDatAccess val);
    void PublishPreparedAssetSource(IBakedAssetSource val);
    void PublishMagicCatalog(ArcanaCatalog val);
    void PublishAnimationLoader(IAnimReader val);
    void PublishLiveEntityCollisionBuilder(OnlineActorContactAssembler val);
    void PublishEmitterRegistry(EmitterSpecRegistry val);
    void PublishParticleSystem(MoteSys val);
    void PublishParticleSink(ParticleHookTap val);
    void PublishAnimationHookFrames(MotionHookFrameQueue val);
    void PublishPhysicsScriptLoader(CanonKineticScriptLoader val);
    void PublishPhysicsScriptRunner(KineticScriptRunner val);
    void PublishLightingSink(LightingHookTap val);
    void PublishTranslucencySink(SeeThroughHookTap val);
    void PublishHookRegistrations(MotionHookRegistrationSet val);
    void PublishAudio(SubstanceSoundGraph val);
}

internal interface IContentEffectsAudioAssemblyMint
{
    IDatAccess OpenDatCollection(string datFolder);
    IBakedAssetSource OpenReadiedAssetSrc(
        string trail,
        string? topLayerTrail,
        uint? baseRecipeVer,
        uint? netRecipeVer,
        IDatAccess datFiles,
        Action<string> probe);
    ArcanaCatalog PullMagicRegistry(IDatAccess datFiles);
    void SetupArcanumMetadata(
        SimToonLedger toon,
        ArcanaCatalog registry);
    int FetchArcanumTally(ArcanaCatalog registry);
    GenesisOptions PullChargenKnobs(IDatAccess datFiles);
    void SetupChargenKnobs(OnlineSessionDriver sess, GenesisOptions knobs);
    IAnimReader BuildAnimFetcher(
        IDatAccess datFiles,
        long ceilingEstimatedOctets,
        int ceilingListings);
    OnlineActorContactAssembler BuildImpactBuilder(
        KineticAssetCache kineticsBlob,
        IDatAccess datFiles,
        IAnimReader animFetcher,
        bool printLocomotionTurnedOn);
    OnlineContactAssetHerald BuildOnlineImpactAssetPublisher(
        KineticAssetCache kineticsBlob,
        IBakedAssetSource readiedHoldings);
    EmitterSpecRegistry BuildSpoutRegistry(IDatAccess datFiles);
    MoteSys BuildMoteSys(EmitterSpecRegistry spouts);
    ParticleHookTap BuildMoteDrain(
        MoteSys motes,
        ActorEffectPoseRegistry postures);
    MotionHookFrameQueue BuildAnimTapCycles(
        AnimHookRouter router,
        ActorEffectPoseRegistry postures);
    CanonKineticScriptLoader BuildKineticsProgramFetcher(IDatAccess datFiles);
    KineticScriptRunner BuildKineticsProgramRunner(
        CanonKineticScriptLoader fetcher,
        AnimHookRouter router,
        IActorEffectAdvanceSource proceedSrc);
    LightingHookTap BuildIlluminationDrain(
        LightKeeper illumination,
        ActorEffectPoseRegistry postures);
    SeeThroughHookTap BuildSeeThroughDrain(SeeThroughFadeKeeper fades);
    MotionHookRegistrationSet BuildTapRegistrations(AnimHookRouter router);
    DatWaveCache BuildSfxStash(
        IDatAccess datFiles,
        long ceilingDecodedOctets);
    OpenAlSoundEngine BuildSoundEngine();
    DictionaryActorSoundChart BuildActorSfxCharts();
    SoundTapDrain BuildSoundDrain(
        OpenAlSoundEngine engine,
        DatWaveCache stash,
        DictionaryActorSoundChart actorSfxCharts);
    WidgetSoundDriver BuildWidgetSfxList(
        OpenAlSoundEngine engine,
        DatWaveCache stash,
        IDatAccess datFiles);
    AmbientSoundDriver BuildAmbient(
        OpenAlSoundEngine engine,
        DatWaveCache stash);
}

internal sealed class CanonContentEffectsAudioAssemblyMint
    : IContentEffectsAudioAssemblyMint
{
    public IDatAccess OpenDatCollection(string datFolder) =>
        LiveDatCollectionFactory.OpenScanSole(datFolder);

    public IBakedAssetSource OpenReadiedAssetSrc(
        string trail,
        string? topLayerTrail,
        uint? baseRecipeVer,
        uint? netRecipeVer,
        IDatAccess datFiles,
        Action<string> probe)
    {
        // A pak is an optional prebuilt cache. Without one the content is built from the dats as
        // the world asks for it, which is what an ordinary install does.
        if (string.IsNullOrWhiteSpace(topLayerTrail))
        {
            if (string.IsNullOrWhiteSpace(trail) || !File.Exists(trail))
                return new LiveBakedAssetSource(datFiles, new ProbeLogger(probe));

            return new PakBakedAssetSource(trail, datFiles, probe);
        }

        if (baseRecipeVer is not > 0
            || netRecipeVer is not > 0)
        {
            throw new InvalidDataException(
                "Layered prepared content is absent its recipe identities");
        }

        if (netRecipeVer
            != MacAC.Assets.Pak.PakFmt.LatestBakeToolVer)
        {
            throw new InvalidDataException(
                $"Prepared content recipe {netRecipeVer} doesn't "
                + $"match client recipe "
                + $"{MacAC.Assets.Pak.PakFmt.LatestBakeToolVer}.");
        }

        PakBakedAssetSource? baseSrc = null;
        PakBakedAssetSource? topLayerSrc = null;
        try
        {
            baseSrc = new PakBakedAssetSource(
                trail,
                BakedCatalogIdentity.From(datFiles, baseRecipeVer.Value),
                probe);
            topLayerSrc = new PakBakedAssetSource(
                topLayerTrail,
                BakedCatalogIdentity.From(datFiles, netRecipeVer.Value),
                probe);
            return new TieredBakedAssetSource(baseSrc, topLayerSrc);
        }
        catch
        {
            topLayerSrc?.Dispose();
            baseSrc?.Dispose();
            throw;
        }
    }

    public ArcanaCatalog PullMagicRegistry(IDatAccess datFiles) =>
        ArcanaCatalog.Load(datFiles);

    public void SetupArcanumMetadata(
        SimToonLedger toon,
        ArcanaCatalog registry) =>
        toon.PlaceArcanumMetadata(registry.SpellTable);

    public int FetchArcanumTally(ArcanaCatalog registry) => registry.SpellTable.Count;

    public GenesisOptions PullChargenKnobs(IDatAccess datFiles) =>
        MacAC.Assets.CharGen.GenesisTableReader.Load(datFiles);

    public void SetupChargenKnobs(OnlineSessionDriver sess, GenesisOptions knobs) =>
        sess.ToonCreationPhase.SetupKnobs(knobs);

    public IAnimReader BuildAnimFetcher(
        IDatAccess datFiles,
        long ceilingEstimatedOctets,
        int ceilingListings)
    {
        return new CanonAnimationLoader(
            datFiles,
            ceilingEstimatedOctets,
            ceilingListings);
    }

    public OnlineActorContactAssembler BuildImpactBuilder(
        KineticAssetCache kineticsBlob,
        IDatAccess datFiles,
        IAnimReader animFetcher,
        bool printLocomotionTurnedOn)
    {
        return new(
            kineticsBlob,
            new OnlineActorDefaultPosePicker(
                ident => datFiles.Get<MotionBook>(ident),
                animFetcher,
                printLocomotionTurnedOn));
    }

    public OnlineContactAssetHerald BuildOnlineImpactAssetPublisher(
        KineticAssetCache kineticsBlob,
        IBakedAssetSource readiedHoldings)
    {
        return new(
            kineticsBlob,
            readiedHoldings as IBakedContactSource
                ?? throw new NotSupportedException(
                    "Production prepared assets must expose collision data"));
    }

    public EmitterSpecRegistry BuildSpoutRegistry(IDatAccess datFiles) =>
        new(datFiles);

    public MoteSys BuildMoteSys(EmitterSpecRegistry spouts) =>
        new(spouts);

    public ParticleHookTap BuildMoteDrain(
        MoteSys motes,
        ActorEffectPoseRegistry postures) =>
        new(motes, postures);

    public MotionHookFrameQueue BuildAnimTapCycles(
        AnimHookRouter router,
        ActorEffectPoseRegistry postures) =>
        new(router, postures);

    public CanonKineticScriptLoader BuildKineticsProgramFetcher(IDatAccess datFiles) =>
        new(datFiles);

    public KineticScriptRunner BuildKineticsProgramRunner(
        CanonKineticScriptLoader fetcher,
        AnimHookRouter router,
        IActorEffectAdvanceSource proceedSrc)
    {
        return new(
            fetcher.PullKineticsProgram,
            router,
            canProceedHolder: proceedSrc.CanProceedHolder);
    }

    public LightingHookTap BuildIlluminationDrain(
        LightKeeper illumination,
        ActorEffectPoseRegistry postures) =>
        new(illumination, postures);

    public SeeThroughHookTap BuildSeeThroughDrain(
        SeeThroughFadeKeeper fades) =>
        new(fades);

    public MotionHookRegistrationSet BuildTapRegistrations(
        AnimHookRouter router) =>
        new(router);

    public DatWaveCache BuildSfxStash(
        IDatAccess datFiles,
        long ceilingDecodedOctets) =>
        new(datFiles, ceilingDecodedOctets);

    public OpenAlSoundEngine BuildSoundEngine() => new();

    public DictionaryActorSoundChart BuildActorSfxCharts() => new();

    public SoundTapDrain BuildSoundDrain(
        OpenAlSoundEngine engine,
        DatWaveCache stash,
        DictionaryActorSoundChart actorSfxCharts) =>
        new(engine, stash, actorSfxCharts);

    public WidgetSoundDriver BuildWidgetSfxList(
        OpenAlSoundEngine engine,
        DatWaveCache stash,
        IDatAccess datFiles) =>
        new(engine, stash, WidgetSoundChartPicker.Resolve(datFiles));

    public AmbientSoundDriver BuildAmbient(
        OpenAlSoundEngine engine,
        DatWaveCache stash) =>
        new(engine, stash);
}

internal enum ContentEffectsAudioAssemblyPoint
{
    DatCollectionPublished,
    PreparedAssetSourcePublished,
    MagicCatalogPublished,
    SpellMetadataInstalled,
    ChargenOptionsInstalled,
    AnimationLoaderPublished,
    CollisionBuilderPublished,
    EmitterRegistryPublished,
    ParticleSystemPublished,
    ParticleSinkPublished,
    AnimationHookFramesPublished,
    HookRegistrationsPublished,
    ParticleHookRegistered,
    PhysicsScriptLoaderPublished,
    PhysicsScriptRunnerPublished,
    LightingSinkPublished,
    LightingHookRegistered,
    TranslucencySinkPublished,
    TranslucencyHookRegistered,
    SoundCacheCreated,
    AudioEngineCreated,
    EntitySoundTablesCreated,
    AudioSinkCreated,
    UiSoundsCreated,
    AmbientCreated,
    AudioPublished,
    AudioHookRegistered,
}

internal sealed partial class ContentEffectsAudioAssemblyPhase(
    SubstanceFxListSoundDeps dependencies,
    IPlayPaneSubstanceFxListSoundBulletin publication,
    IContentEffectsAudioAssemblyMint? maker = null,
    Action<ContentEffectsAudioAssemblyPoint>? flawInjection = null) :
    IContentEffectsAudioAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome>
{
    private readonly SubstanceFxListSoundDeps _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));

    private readonly IPlayPaneSubstanceFxListSoundBulletin _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));

    private readonly IContentEffectsAudioAssemblyMint _maker = maker ?? new CanonContentEffectsAudioAssemblyMint();

    private readonly Action<ContentEffectsAudioAssemblyPoint>? _flawInjection = flawInjection;

    private static bool HasIncompleteTidy(Exception miss)
    {
        if (miss is IRetryableAssetTidy tidy
            && !tidy.IsTidyDone)

            return true;
        return miss is AggregateException aggregate
            ? aggregate.InnerExceptions.Any(HasIncompleteTidy)
            : miss.InnerException is { } interior && HasIncompleteTidy(interior);
    }

    private void Flaw(ContentEffectsAudioAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}

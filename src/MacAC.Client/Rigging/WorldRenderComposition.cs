using System.Collections.Concurrent;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Vfx;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Landscape;
using MacAC.Mechanics.Sound;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed record RealmRegionData(WorldRegion Region, float[] HeightTable);

internal sealed record RealmLandscapeBuildScope(
    uint InitialCenterLandblockId,
    int InitialCenterX,
    int InitialCenterY,
    float[] HeightTable,
    TerrainBlendContext Blending,
    ConcurrentDictionary<uint, SurfaceFacts> SurfaceCache);

internal sealed record RealmRenderFoundation(
    string ShadersDirectory,
    LandTileset? TerrainAtlas,
    StageLightingUboWiring? SceneLighting,
    DiagStrokePainter DebugLines,
    BitmapFont? DebugFont,
    PhrasePainter? TextRenderer,
    LandModernPainter? Terrain,
    RealmTriMeshBridge? MeshAdapter,
    BitmapStash TextureCache,
    TenancyKeeper Residency);

internal sealed record RealmRenderResult(
    RealmLandscapeBuildScope TerrainBuild,
    RealmRenderFoundation Foundation);

internal sealed record RealmRenderDependencies(
    RealmEnvironmentDriver Environment,
    IPlayRasterizeAssetLifespan RenderResources,
    IGpuAssetSunsetFifo ResourceRetirement,
    TenancyAllowanceKnobs ResidencyBudgets,
    uint InitialCenterLandblockId,
    string DiagnosticsDirectory,
    Action<string> Log,
    MacAC.Client.Graphics.Gpu.IClientGpuDevice GpuDevice,
    MacAC.Client.Graphics.ILatestGpuCycleOrigin GpuFrameSource);

internal interface IGameWindowRealmRenderPublication
{
    void PublishSceneLighting(StageLightingUboWiring val);
    void PublishDebugLines(DiagStrokePainter val);
    void PublishHudResources(BitmapFont typeface, PhrasePainter phrase);
    void PublishTerrain(LandModernPainter val);
    void PublishTerrainBuildState(
        float[] heightChart,
        TerrainBlendContext blending,
        ConcurrentDictionary<uint, SurfaceFacts> canvasStash);
    void PublishWbMeshAdapter(RealmTriMeshBridge val);
    void PublishTextureCache(BitmapStash val);
}

internal interface IRealmRenderAssemblyMint
{
    RealmRegionData PullZone(IDatAccess datFiles);
    void BootstrapSurroundings(
        RealmEnvironmentDriver surroundings,
        WorldRegion zone,
        IDatAccess datFiles);
    LandTileset ObtainBackendNeutralLandTileset(
        IPlayRasterizeAssetLifespan lifespan,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles);

    void ExerciseBackendNeutralRealmTextures(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        Action<string> trace);

    void AssignLandAnisotropic(LandTileset tileset, int tier);
    StageLightingUboWiring BuildBackendNeutralTableauIllumination(
        ILatestGpuCycleOrigin cycleSrc,
        IRealmPassScope ambit);
    DiagStrokePainter BuildDiagStrokes(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shadersFolder);
    byte[]? TryPullDiagTypeface();
    BitmapFont BuildDiagTypeface(MacAC.Client.Graphics.Gpu.IClientGpuDevice dev, byte[] octets);
    PhrasePainter BuildPhrasePainter(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shadersFolder);
    LandModernPainter BuildBackendNeutralLand(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        ILatestGpuCycleOrigin cycleSrc,
        IRealmPassScope ambit,
        LandTileset tileset,
        IGpuAssetSunsetFifo sunset);
    RealmLandscapeBuildScope BuildLandAssembleCtx(
        uint startingMiddleLbIdent,
        float[] heightChart,
        LandTileset? tileset);
    RealmTriMeshBridge BuildTriMeshBridge(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles,
        IBakedAssetSource readiedHoldings,
        IGpuAssetSunsetFifo sunset,
        TenancyAllowanceKnobs budgets);
    BitmapStash BuildTextureStash(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles,
        IGpuAssetSunsetFifo sunset,
        string telemetryFolder,
        TenancyAllowanceKnobs budgets);
    void EnrollResidencySrcs(
        TenancyKeeper keeper,
        RealmTriMeshBridge? triMeshes,
        BitmapStash textures,
        IBakedAssetSource readiedHoldings,
        IAnimReader anims,
        DatWaveCache? sound);
    void Release(IDisposable asset);
}

internal sealed class CanonRealmRenderAssemblyMint
    : IRealmRenderAssemblyMint
{
    public RealmRegionData PullZone(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        WorldRegion zone = datFiles.Get<WorldRegion>(0x13000000u)
            ?? throw new InvalidOperationException(
                "Region dat id 0x13000000 absent");
        float[]? heightChart = zone.Land.HeightTable;
        return heightChart is null || heightChart.Length < 256
            ? throw new InvalidOperationException(
                "Region.LandDefs.LandHeightTable absent or truncated")
            : new RealmRegionData(zone, heightChart);
    }

    public void BootstrapSurroundings(
        RealmEnvironmentDriver surroundings,
        WorldRegion zone,
        IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(surroundings);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(datFiles);
        surroundings.Prime(zone, datFiles);
    }

    public LandTileset ObtainBackendNeutralLandTileset(
        IPlayRasterizeAssetLifespan lifespan,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(lifespan);
        return lifespan.ObtainLandTileset(
            () => LandTileset.AssembleBackendNeutral(dev, datFiles));
    }

    public void ExerciseBackendNeutralRealmTextures(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        Action<string> trace) =>
        BackendNeutralRealmTextures.Exercise(dev, trace);

    public void AssignLandAnisotropic(LandTileset tileset, int tier) =>
        tileset.AssignAnisotropic(tier);

    public StageLightingUboWiring BuildBackendNeutralTableauIllumination(
        ILatestGpuCycleOrigin cycleSrc,
        IRealmPassScope ambit) =>
        new(cycleSrc, ambit.Sections);

    public DiagStrokePainter BuildDiagStrokes(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shadersFolder) =>
        new(dev, cycleSrc, shadersFolder);

    public byte[]? TryPullDiagTypeface() =>
        BitmapFont.TryPullSysMonospaceTypeface();

    public BitmapFont BuildDiagTypeface(MacAC.Client.Graphics.Gpu.IClientGpuDevice dev, byte[] octets) =>
        new(dev, octets, pixelHeight: 15f, tilesetDims: 512);

    public PhrasePainter BuildPhrasePainter(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shadersFolder) =>
        new(dev, cycleSrc, shadersFolder);

    public LandModernPainter BuildBackendNeutralLand(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        ILatestGpuCycleOrigin cycleSrc,
        IRealmPassScope ambit,
        LandTileset tileset,
        IGpuAssetSunsetFifo sunset) =>
        new(gpuDev, cycleSrc, ambit, tileset, sunset);

    public RealmLandscapeBuildScope BuildLandAssembleCtx(
        uint startingMiddleLbIdent,
        float[] heightChart,
        LandTileset? tileset)
    {
        int middleX = (int)((startingMiddleLbIdent >> 24) & 0xFFu);
        int middleY = (int)((startingMiddleLbIdent >> 16) & 0xFFu);
        if (tileset is null)
        {
            return new RealmLandscapeBuildScope(
                startingMiddleLbIdent,
                middleX,
                middleY,
                heightChart,
                new TerrainBlendContext(
                    TerrainTypeToLayer: new Dictionary<uint, byte>(),
                    RoadLayer: SurfaceFacts.None,
                    CornerAlphaLayers: [],
                    SideAlphaLayers: [],
                    RoadAlphaLayers: [],
                    CornerAlphaTCodes: [],
                    SideAlphaTCodes: [],
                    RoadAlphaRCodes: []),
                new ConcurrentDictionary<uint, SurfaceFacts>());
        }

        var strata = new Dictionary<uint, byte>(tileset.LandKindToStratum.Count);
        foreach ((uint landKind, uint stratum) in tileset.LandKindToStratum)
            strata[landKind] = (byte)stratum;

        const uint RoadKindEnumVal = 0x20;
        byte roadStratum = strata.TryGetValue(RoadKindEnumVal, out byte road)
            ? road
            : SurfaceFacts.None;
        TerrainBlendContext blending = new TerrainBlendContext(
            TerrainTypeToLayer: strata,
            RoadLayer: roadStratum,
            CornerAlphaLayers: tileset.CornerAlphaStrata,
            SideAlphaLayers: tileset.FlankAlphaStrata,
            RoadAlphaLayers: tileset.RoadAlphaStrata,
            CornerAlphaTCodes: tileset.CornerAlphaTCodes,
            SideAlphaTCodes: tileset.FlankAlphaTCodes,
            RoadAlphaRCodes: tileset.RoadAlphaRCodes);
        return new RealmLandscapeBuildScope(
            startingMiddleLbIdent,
            middleX,
            middleY,
            heightChart,
            blending,
            new ConcurrentDictionary<uint, SurfaceFacts>());
    }

    public RealmTriMeshBridge BuildTriMeshBridge(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles,
        IBakedAssetSource readiedHoldings,
        IGpuAssetSunsetFifo sunset,
        TenancyAllowanceKnobs budgets)
    {
        return new(
            dev,
            datFiles,
            readiedHoldings,
            NullLogger<RealmTriMeshBridge>.Instance,
            sunset,
            budgets);
    }

    public BitmapStash BuildTextureStash(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        IDatAccess datFiles,
        IGpuAssetSunsetFifo sunset,
        string telemetryFolder,
        TenancyAllowanceKnobs budgets)
    {
        return new(
            dev,
            datFiles,
            sunset,
            telemetryFolder,
            budgets);
    }

    public void EnrollResidencySrcs(
        TenancyKeeper keeper,
        RealmTriMeshBridge? triMeshes,
        BitmapStash textures,
        IBakedAssetSource readiedHoldings,
        IAnimReader anims,
        DatWaveCache? sound)
    {
        triMeshes?.EnrollResidencySrcs(keeper);
        textures.EnrollResidencySrcs(keeper);
        keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
            TenancyDomain.PreparedPackage,
            () => new TenancyDomainCapture(
                TenancyDomain.PreparedPackage,
                EntryCount: 1,
                OwnerCount: 1,
                Charges: new TenancyCharges(
                    MappedVirtualBytes:
                        readiedHoldings.MappedVirtualBytes))));
        if (anims is CanonAnimationLoader canonAnims)
        {
            keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                TenancyDomain.Animations,
                () =>
                {
                    var telemetry =
                        canonAnims.Diagnostics;
                    return new TenancyDomainCapture(
                        TenancyDomain.Animations,
                        EntryCount: telemetry.Count,
                        OwnerCount: 0,
                        Charges: new TenancyCharges(
                            DecodedBytes:
                                telemetry.EstimatedBytes),
                        BudgetBytes: telemetry.BudgetBytes,
                        Hits: telemetry.Stats.Hits,
                        Misses: telemetry.Stats.Misses,
                        Evictions: telemetry.Stats.Evictions);
                }));
        }
        if (sound is not null)
        {
            keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                TenancyDomain.Audio,
                () =>
                {
                    var telemetry = sound.Diagnostics;
                    return new TenancyDomainCapture(
                        TenancyDomain.Audio,
                        EntryCount: telemetry.CachedWaveCount,
                        OwnerCount: 0,
                        Charges: new TenancyCharges(
                            DecodedBytes: telemetry.ResidentWaveBytes),
                        BudgetBytes: telemetry.BudgetBytes,
                        Hits: telemetry.Hits,
                        Misses: telemetry.Misses,
                        Evictions: telemetry.Evictions);
                }));
        }
    }

    public void Release(IDisposable asset) => asset.Dispose();
}

internal enum RealmRenderAssemblyPoint
{
    RegionLoaded,
    EnvironmentInitialized,
    TerrainAtlasAcquired,
    SceneLightingPublished,
    DebugLinesPublished,
    DebugFontCreated,
    TextRendererCreated,
    HudResourcesPublished,
    HudResourcesCompleted,
    TerrainPublished,
    TerrainBuildStatePublished,
    MeshAdapterPublished,
    TextureCachePublished,
}

internal sealed partial class RealmRenderAssemblyPhase(
    RealmRenderDependencies dependencies,
    IGameWindowRealmRenderPublication publication,
    IRealmRenderAssemblyMint? maker = null,
    Action<RealmRenderAssemblyPoint>? flawInjection = null)
        : IRealmRenderAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult,
        RealmRenderResult>
{
    private readonly RealmRenderDependencies _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));

    private readonly IGameWindowRealmRenderPublication _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));

    private readonly IRealmRenderAssemblyMint _maker = maker ?? new CanonRealmRenderAssemblyMint();

    private readonly Action<RealmRenderAssemblyPoint>? _flawInjection = flawInjection;

    private T ObtainAndBroadcast<T>(
        AssemblyAcquisitionScope ambit,
        string label,
        Func<T> maker,
        Action<T> broadcast,
        RealmRenderAssemblyPoint pt)
        where T : class, IDisposable
    {
        T val = ambit.Acquire(label, maker, _maker.Release).Publish(broadcast);
        Flaw(pt);
        return val;
    }

    private (BitmapFont? Font, PhrasePainter? Text) ConstructOptionalHudAssetList(
        AssemblyAcquisitionScope ambit,
        string shadersFolder)
    {
        byte[]? typefaceOctets = _maker.TryPullDiagTypeface();
        if (typefaceOctets is null)
        {
            _deps.Log("world-hud font: no system monospace font found");
            Flaw(RealmRenderAssemblyPoint.HudResourcesCompleted);
            return (null, null);
        }

        var typefaceTenancy = ambit.Acquire(
            "world HUD font",
            () => _maker.BuildDiagTypeface(_deps.GpuDevice, typefaceOctets),
            _maker.Release);
        BitmapFont typeface = typefaceTenancy.Resource;
        Flaw(RealmRenderAssemblyPoint.DebugFontCreated);
        var phraseTenancy = ambit.Acquire(
            "world HUD text renderer",
            () => _maker.BuildPhrasePainter(
                _deps.GpuDevice,
                _deps.GpuFrameSource,
                shadersFolder),
            _maker.Release);
        var phrase = phraseTenancy.Resource;
        Flaw(RealmRenderAssemblyPoint.TextRendererCreated);

        _bulletin.PublishHudResources(typeface, phrase);
        typefaceTenancy.Transfer();
        phraseTenancy.Transfer();
        Flaw(RealmRenderAssemblyPoint.HudResourcesPublished);
        _deps.Log(
            $"world-hud font: loaded {typefaceOctets.Length / 1024}KB, " +
            $"atlas {typeface.TilesetWidth}x{typeface.TilesetHeight}, " +
            $"lineHeight={typeface.LineHeight:F1}px (reserved for D.6 HUD)");
        Flaw(RealmRenderAssemblyPoint.HudResourcesCompleted);
        return (typeface, phrase);
    }

    private void Flaw(RealmRenderAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}

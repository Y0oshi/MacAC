using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Tenancy;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed partial class RealmRenderAssemblyPhase
{
    public RealmRenderResult Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(prefs);

        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        try
        {
            TenancyKeeper residency = new TenancyKeeper(
                _deps.ResidencyBudgets);

            var zone = _maker.PullZone(substance.Dats);
            Flaw(RealmRenderAssemblyPoint.RegionLoaded);
            _maker.BootstrapSurroundings(
                _deps.Environment,
                zone.Region,
                substance.Dats);
            Flaw(RealmRenderAssemblyPoint.EnvironmentInitialized);

            var landTileset = _maker.ObtainBackendNeutralLandTileset(
                _deps.RenderResources,
                _deps.GpuDevice,
                substance.Dats);
            _maker.AssignLandAnisotropic(
                landTileset,
                prefs.ResolvedQuality.AnisotropicLevel);
            Flaw(RealmRenderAssemblyPoint.TerrainAtlasAcquired);

            _maker.ExerciseBackendNeutralRealmTextures(
                _deps.GpuDevice,
                _deps.Log);

            string shadersFolder = Path.Combine(
                AppContext.BaseDirectory,
                "Graphics",
                "Shaders");
            IRealmPassScope realmPassAmbit = platform.Graphics.RealmPassAmbit
                ?? throw new InvalidOperationException(
                    "The graphics backend must publish a world pass scope");
            var tableauIllumination = ObtainAndBroadcast(
                ambit,
                "scene lighting",
                () => _maker.BuildBackendNeutralTableauIllumination(
                    _deps.GpuFrameSource,
                    realmPassAmbit),
                _bulletin.PublishSceneLighting,
                RealmRenderAssemblyPoint.SceneLightingPublished);
            var diagStrokes = ObtainAndBroadcast(
                ambit,
                "debug lines",
                () => _maker.BuildDiagStrokes(
                    _deps.GpuDevice,
                    _deps.GpuFrameSource,
                    shadersFolder),
                _bulletin.PublishDebugLines,
                RealmRenderAssemblyPoint.DebugLinesPublished);

            (BitmapFont? diagTypeface, PhrasePainter? phrasePainter) =
                ConstructOptionalHudAssetList(ambit, shadersFolder);

            var land = ObtainAndBroadcast(
                ambit,
                "terrain renderer",
                () => _maker.BuildBackendNeutralLand(
                    _deps.GpuDevice,
                    _deps.GpuFrameSource,
                    realmPassAmbit,
                    landTileset,
                    _deps.ResourceRetirement),
                _bulletin.PublishTerrain,
                RealmRenderAssemblyPoint.TerrainPublished);

            var landAssemble =
                _maker.BuildLandAssembleCtx(
                    _deps.InitialCenterLandblockId,
                    zone.HeightTable,
                    landTileset);
            _bulletin.PublishTerrainBuildState(
                landAssemble.HeightTable,
                landAssemble.Blending,
                landAssemble.SurfaceCache);
            Flaw(RealmRenderAssemblyPoint.TerrainBuildStatePublished);

            var triMeshBridge = ObtainAndBroadcast(
                ambit,
                "WB mesh adapter",
                () => _maker.BuildTriMeshBridge(
                    _deps.GpuDevice,
                    substance.Dats,
                    substance.PreparedAssets,
                    _deps.ResourceRetirement,
                    residency.Budgets),
                _bulletin.PublishWbMeshAdapter,
                RealmRenderAssemblyPoint.MeshAdapterPublished);
            var textureStash = ObtainAndBroadcast(
                ambit,
                "texture cache",
                () => _maker.BuildTextureStash(
                    _deps.GpuDevice,
                    substance.Dats,
                    _deps.ResourceRetirement,
                    _deps.DiagnosticsDirectory,
                    residency.Budgets),
                _bulletin.PublishTextureCache,
                RealmRenderAssemblyPoint.TextureCachePublished);
            _maker.EnrollResidencySrcs(
                residency,
                triMeshBridge,
                textureStash,
                substance.PreparedAssets,
                substance.AnimationLoader,
                substance.Audio?.SoundCache);

            ambit.Complete();
            _deps.Log(
                "[N.4+N.5] WB foundation + modern path active — " +
                "routing all content through ObjectMeshManager.");
            return new RealmRenderResult(
                landAssemble,
                new RealmRenderFoundation(
                    shadersFolder,
                    landTileset,
                    tableauIllumination,
                    diagStrokes,
                    diagTypeface,
                    phrasePainter,
                    land,
                    triMeshBridge,
                    textureStash,
                    residency));
        }
        catch (Exception miss)
        {
            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }
}

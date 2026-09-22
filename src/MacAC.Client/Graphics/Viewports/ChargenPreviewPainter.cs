using MacAC.Client.Graphics.Batching;
using MacAC.Client.Shell;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IChargenPreviewPainter
{
    void AssignPreview(RealmActor? actor);

    void AssignBackdrop(RealmActor? actor);

    uint Render(int width, int height);
}

internal sealed class ChargenPreviewPainter :
    IWidgetViewportPainter,
    IChargenPreviewPainter,
    IDisposable
{
    private readonly PrivateActorViewportPainter _painter;
    private readonly ChargenPreviewViewRectCamera _cam;

    internal ChargenPreviewPainter(
        IRealmPassScope ambit,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycles,
        RealmPaintRouter router,
        StageLightingUboWiring lampUbo,
        IActorTextureLifetime textureLifespan,
        IBatchMeshBridge triMeshBridge,
        uint lineageIdent = 0u,
        ClientChargenPreviewCamera? cam = null,
        uint rasterizeIdent = ChargenPreviewActorAssembler.PreviewRasterizeIdent,
        uint backdropRasterizeIdent = ChargenPreviewActorAssembler.PreviewBackdropRasterizeIdent)
    {
        _cam = cam is not null
            ? new ChargenPreviewViewRectCamera(cam)
            : new ChargenPreviewViewRectCamera(lineageIdent);
        _painter = new PrivateActorViewportPainter(
            ambit,
            dev,
            cycles,
            router,
            lampUbo,
            textureLifespan,
            triMeshBridge,
            rasterizeIdent,
            _cam,
            "chargen preview",
            backdropRasterizeIdent);
    }

    public bool TextureIsBottomUp => _painter.TextureIsBottomUp;

    public void AssignHeritage(uint lineageIdent) => _cam.ApplyLineage(lineageIdent);

    public void AssignPreview(RealmActor? actor) => _painter.AssignActor(actor);

    public void AssignBackdrop(RealmActor? actor) => _painter.ApplyBackdrop(actor);

    public uint Render(int width, int height) => _painter.Render(width, height);

    public void Dispose() => _painter.Dispose();
}

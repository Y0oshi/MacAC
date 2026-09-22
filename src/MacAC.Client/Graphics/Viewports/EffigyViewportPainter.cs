using MacAC.Client.Graphics.Batching;
using MacAC.Client.Shell;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

public sealed class EffigyViewportPainter :
    IWidgetViewportPainter,
    IEffigyDollPainter,
    IDisposable
{
    private readonly PrivateActorViewportPainter _painter;

    internal EffigyViewportPainter(
        IRealmPassScope ambit,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycles,
        RealmPaintRouter router,
        StageLightingUboWiring lampUbo,
        IActorTextureLifetime textureLifespan,
        IBatchMeshBridge triMeshBridge)
    {
        _painter = new PrivateActorViewportPainter(
            ambit,
            dev,
            cycles,
            router,
            lampUbo,
            textureLifespan,
            triMeshBridge,
            DollActorAssembler.DollRasterizeIdent,
            new DollViewRectCamera(),
            "paperdoll");
    }

    public bool TextureIsBottomUp => _painter.TextureIsBottomUp;

    public void AssignDoll(RealmActor? doll) => _painter.AssignActor(doll);

    public void Prepare() => _painter.Prepare();

    public uint Render(int width, int height) =>
        _painter.Render(width, height);

    public void Dispose() => _painter.Dispose();
}

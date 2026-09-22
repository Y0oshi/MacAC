using MacAC.Client.Paging;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal sealed class GpuRealmRenderTraversalOrderSource(
    GpuRealmPhase world) : IRasterizeTraversalOrderingOrigin
{
    private readonly GpuRealmPhase _world =
        world ?? throw new ArgumentNullException(nameof(world));

    public bool TryFetchTraversalOrderTag(
        RealmActor actor,
        out RasterizeOrderTag orderTag) =>
        _world.TryFetchRasterizeTraversalOrderTag(actor, out orderTag);
}

using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics.Packs;

internal interface IRasterizeBundleRecipientPipeContender : IDisposable
{
}

internal interface IRenderPackReceiverPipelineMarshal
{
    IRasterizeBundleRecipientPipeContender Prepare(
        IDirectionalShadeReceiverSource? src,
        int specimenTally);

    void Publish(IRasterizeBundleRecipientPipeContender contender);

    void Clear();
}

internal sealed class RenderPackReceiverPipelineMarshal(
    LandModernPainter terrain,
    RealmPaintRouter worldMeshes) : IRenderPackReceiverPipelineMarshal
{
    private readonly LandModernPainter _land = terrain
        ?? throw new ArgumentNullException(nameof(terrain));
    private readonly RealmPaintRouter _realmTriMeshes = worldMeshes
        ?? throw new ArgumentNullException(nameof(worldMeshes));

    public IRasterizeBundleRecipientPipeContender Prepare(
        IDirectionalShadeReceiverSource? src,
        int specimenTally)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(specimenTally);
        var landPhase =
            _land.ReadyDirectedShadeRecipient(src, specimenTally);
        try
        {
            var realmPhase =
                _realmTriMeshes.ReadyDirectedShadeRecipient(src, specimenTally);
            return new Contender(this, landPhase, realmPhase);
        }
        catch
        {
            landPhase?.Dispose();
            throw;
        }
    }

    public void Publish(IRasterizeBundleRecipientPipeContender candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate is not Contender readied || !ReferenceEquals(readied.Owner, this))
            throw new ArgumentException("Receiver candidate belongs to another coordinator", nameof(candidate));

        (LandModernPainter.DirectionalShadeReceiverPipelineLedger? landPhase,
            RealmPaintRouter.DirectionalShadeReceiverPipelineLedger? realmPhase) = readied.Grab();
        var formerLand =
            _land.SwapDirectedShadeRecipient(landPhase);
        var formerRealm =
            _realmTriMeshes.SwapDirectedShadeRecipient(realmPhase);

        formerLand?.Dispose();
        formerRealm?.Dispose();
    }

    public void Clear()
    {
        var formerLand =
            _land.SwapDirectedShadeRecipient(null);
        var formerRealm =
            _realmTriMeshes.SwapDirectedShadeRecipient(null);
        formerLand?.Dispose();
        formerRealm?.Dispose();
    }

    private sealed class Contender(
        RenderPackReceiverPipelineMarshal holder,
        LandModernPainter.DirectionalShadeReceiverPipelineLedger? land,
        RealmPaintRouter.DirectionalShadeReceiverPipelineLedger? realm) :
        IRasterizeBundleRecipientPipeContender
    {
        private LandModernPainter.DirectionalShadeReceiverPipelineLedger? _land = land;
        private RealmPaintRouter.DirectionalShadeReceiverPipelineLedger? _world = realm;
        private bool _taken;

        internal RenderPackReceiverPipelineMarshal Owner { get; } = holder;

        internal (LandModernPainter.DirectionalShadeReceiverPipelineLedger?,
            RealmPaintRouter.DirectionalShadeReceiverPipelineLedger?) Grab()
        {
            ObjectDisposedException.ThrowIf(_taken, this);
            _taken = true;
            var landPhase = _land;
            var realmPhase = _world;
            _land = null;
            _world = null;
            return (landPhase, realmPhase);
        }

        public void Dispose()
        {
            if (_taken)
                return;
            _taken = true;
            _land?.Dispose();
            _world?.Dispose();
            _land = null;
            _world = null;
        }
    }
}

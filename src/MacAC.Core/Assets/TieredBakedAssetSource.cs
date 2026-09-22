using MacAC.Assets.Pak;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

public sealed class TieredBakedAssetSource : IBakedAssetSource, IBakedContactSource
{
    private IBakedAssetSource? _base;
    private IBakedAssetSource? _topLayer;
    private IBakedContactSource? _baseLink;
    private IBakedContactSource? _topLayerLink;

    public TieredBakedAssetSource(IBakedAssetSource baseSource, IBakedAssetSource overlaySource)
    {
        ArgumentNullException.ThrowIfNull(baseSource);
        ArgumentNullException.ThrowIfNull(overlaySource);
        if (ReferenceEquals(baseSource, overlaySource))
            throw new ArgumentException("The base and overlay must have independent owners", nameof(overlaySource));

        _baseLink = baseSource as IBakedContactSource
            ?? throw new ArgumentException("The base source must expose prepared collision payloads", nameof(baseSource));
        _topLayerLink = overlaySource as IBakedContactSource
            ?? throw new ArgumentException("The overlay source must expose prepared collision payloads", nameof(overlaySource));
        _base = baseSource;
        _topLayer = overlaySource;
    }

    private IBakedAssetSource Base => Live(_base);
    private IBakedAssetSource TopLayer => Live(_topLayer);
    private IBakedContactSource BaseLink => Live(_baseLink);
    private IBakedContactSource TopLayerLink => Live(_topLayerLink);

    public BakedAssetSourceStats Stats => Base.Stats + TopLayer.Stats;

    public BakedContactSourceStats ImpactStats => BaseLink.ImpactStats + TopLayerLink.ImpactStats;

    public ShelfStats DecodedTextureStashStats => Base.DecodedTextureStashStats + TopLayer.DecodedTextureStashStats;

    public long MappedVirtualBytes => checked(Base.MappedVirtualBytes + TopLayer.MappedVirtualBytes);

    public BakedAssetPresence Probe(PakAssetKind kind, uint srcFileIdent)
    {
        var over = TopLayer.Probe(kind, srcFileIdent);
        return over == BakedAssetPresence.Missing ? Base.Probe(kind, srcFileIdent) : over;
    }

    public BakedAssetRead Read(in BakedAssetRequest req, CancellationToken abortTicket = default)
    {
        var over = TopLayer.Read(req, abortTicket);
        return over.Status == BakedAssetReadStatus.Missing ? Base.Read(req, abortTicket) : over;
    }

    public BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent)
    {
        var over = TopLayerLink.InspectImpact(kind, srcFileIdent);
        return over == BakedAssetPresence.Missing ? BaseLink.InspectImpact(kind, srcFileIdent) : over;
    }

    public BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Layered(src => src.ScanGfxObjRefImpact(srcFileIdent, abortTicket));

    public BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Layered(src => src.ReadSetupCollision(srcFileIdent, abortTicket));

    public BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Layered(src => src.ScanChamberStructureImpact(srcFileIdent, abortTicket));
    }

    public BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Layered(src => src.ScanEnvironChamberWiring(srcFileIdent, abortTicket));

    public void Dispose()
    {
        var topLayer = Interlocked.Exchange(ref _topLayer, null);
        var baseSrc = Interlocked.Exchange(ref _base, null);
        _topLayerLink = null;
        _baseLink = null;

        DisposeRest(topLayer, baseSrc);
    }

    private void DisposeRest(IBakedAssetSource? topLayer, IBakedAssetSource? baseSrc)
    {
        List<Exception>? misses = null;
        TryTeardown(topLayer, ref misses);
        TryTeardown(baseSrc, ref misses);
        if (misses is { Count: > 0 })
            throw new AggregateException("One or more prepared-content layers could not dispose", misses);
    }

    // Overlay first; fall through to the base only when the overlay has no entry
    private BakedContactRead<T> Layered<T>(Func<IBakedContactSource, BakedContactRead<T>> scan) where T : class
    {
        var over = scan(TopLayerLink);
        return over.Status == BakedAssetReadStatus.Missing ? scan(BaseLink) : over;
    }

    private static T Live<T>(T? val) where T : class
    {
        return val ?? throw new ObjectDisposedException(nameof(TieredBakedAssetSource));
    }

    private static void TryTeardown(IDisposable? val, ref List<Exception>? misses)
    {
        if (val is null)
            return;
        try
        {
            val.Dispose();
        }
        catch (Exception exception)
        {
            (misses ??= []).Add(exception);
        }
    }
}

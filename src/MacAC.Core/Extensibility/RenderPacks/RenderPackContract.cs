namespace MacAC.Extensibility.RenderPacks;

/// <summary>Version handshake for the declarative render-pack API.</summary>
public static class RenderPackContract
{
    public const int Current = 1;

    public const int MinimumSupported = 1;

    public static bool IsSupported(int apiVer) =>
        apiVer is >= MinimumSupported and <= Current;
}

public interface IRenderPackExtension
{
    /// <summary>Put every pack this extension supplies on the shelf.</summary>
    void Register(IRenderPackShelf registry);
}

public interface IRenderPackShelf
{
    IDisposable Register(RenderPackCard descriptor, IRenderPackFiles holdings);
}

public interface IRenderPackFiles
{
    /// <summary>Open a fresh readable stream for a key the card declared.</summary>
    Stream OpenScan(string assetTag);
}

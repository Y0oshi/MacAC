namespace MacAC.Extensibility.Hosting;

/// <summary>Version handshake between a compiled extension and the host that loads it.</summary>
public static class ExtensionContract
{
    public const int Current = 1;

    public const int FloorSupported = 1;

    /// <summary>True when an extension built against <paramref name="apiVer"/> may run here.</summary>
    public static bool IsSupported(int apiVer) =>
        apiVer is >= FloorSupported and <= Current;
}

public interface IMacACExtension
{
    void Bootstrap(IExtensionHost hub);

    void Enable();

    void Disable();
}

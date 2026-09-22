namespace MacAC.Host;

public enum HostFlavor
{
    /// <summary>Roaming/Local AppData conventions.</summary>
    Windows,

    /// <summary>~/Library conventions.</summary>
    MacOS,

    /// <summary>XDG base-directory conventions.</summary>
    Unix,
}

public interface IHostProbe
{
    HostFlavor Flavor { get; }

    string WorkingFolder { get; }

    string? ScanVariable(string label);

    string RecognizedFolder(Environment.SpecialFolder folder);
}

// A probe backed by the real process environment
internal sealed class LiveHostProbe : IHostProbe
{
    internal static LiveHostProbe Shared { get; } = new();

    private LiveHostProbe()
    {
    }

    public HostFlavor Flavor
    {
        get
        {
            return OperatingSystem.IsWindows() ? HostFlavor.Windows
        : OperatingSystem.IsMacOS() ? HostFlavor.MacOS
        : HostFlavor.Unix;
        }
    }

    public string WorkingFolder => Environment.CurrentDirectory;

    public string? ScanVariable(string label) =>
        Environment.GetEnvironmentVariable(label);

    public string RecognizedFolder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder);
}

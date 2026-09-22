using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacAC.Client.Graphics;
using MacAC.Host;

namespace MacAC.Client.Machine;

internal enum GraphicalHubOperatingSys
{
    Windows,
    Linux,
    MacOS,
}

// The one runtime guard for Unix-only file semantics; its only other consumer is the secret picker
internal static class EnginePlatformWarden
{
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    internal static bool IsUnixCore => System.OperatingSystem.IsLinux() || System.OperatingSystem.IsMacOS();
}

internal sealed record GraphicalNativeDep(string Feature, string PublishedFileName);

internal sealed record GraphicalHubPlatformServices(
    GraphicalHubOperatingSys OperatingSystem,
    Architecture ProcessArchitecture,
    string RuntimeIdentifier,
    GraphicalWindowBackendPicking WindowBackend,
    UserStateLayout Paths,
    IFramePacingWaiterMint FramePacingWaiters,
    IReadOnlyList<GraphicalNativeDep> NativeDependencies)
{
    private static readonly (GraphicalHubOperatingSys Os, string Rid, string Window, string Audio)[] Natives =
    [
        (GraphicalHubOperatingSys.Windows, "win", "glfw3.dll", "soft_oal.dll"),
        (GraphicalHubOperatingSys.Linux, "linux", "libglfw.so.3", "libopenal.so"),
        (GraphicalHubOperatingSys.MacOS, "osx", "libglfw.3.dylib", "libopenal.dylib"),
    ];

    internal static GraphicalHubPlatformServices Resolve()
    {
        var system = SenseOperatingSys();
        var arch = RuntimeInformation.ProcessArchitecture;
        return new GraphicalHubPlatformServices(
            system,
            arch,
            CoreIdentifierFor(system, arch),
            GraphicalWindowBackendPicking.Resolve(system, Environment.GetEnvironmentVariable),
            UserStateLayout.Locate(),
            new PlatformFramePacingWaiterMint(system),
            NativesFor(system));
    }

    internal void ConfigurePaneBackend() => GraphicalWindowBackendArranger.Configure(this);

    internal static GraphicalHubOperatingSys SenseOperatingSys()
    {
        if (System.OperatingSystem.IsWindows())
            return GraphicalHubOperatingSys.Windows;
        if (System.OperatingSystem.IsLinux())
            return GraphicalHubOperatingSys.Linux;
        return System.OperatingSystem.IsMacOS()
            ? GraphicalHubOperatingSys.MacOS
            : throw new PlatformNotSupportedException("macac graphical hosting supports Windows, Linux, and macOS");
    }

    private static string CoreIdentifierFor(GraphicalHubOperatingSys system, Architecture arch)
    {
        string archLabel = arch switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException($"Not supported graphical process architecture {arch}."),
        };
        return system == GraphicalHubOperatingSys.Linux && arch != Architecture.X64
            ? throw new PlatformNotSupportedException("The first macac Linux graphical package supports linux-x64 only")
            : $"{Row(system).Rid}-{archLabel}";
    }

    private static IReadOnlyList<GraphicalNativeDep> NativesFor(GraphicalHubOperatingSys system)
    {
        var rank = Row(system);
        return [new("window/input", rank.Window), new("audio", rank.Audio)];
    }

    private static (GraphicalHubOperatingSys Os, string Rid, string Window, string Audio) Row(GraphicalHubOperatingSys os)
    {
        foreach (var rank in Natives)
        {
            if (rank.Os == os)
                return rank;
        }

        throw new ArgumentOutOfRangeException(nameof(os));
    }
}

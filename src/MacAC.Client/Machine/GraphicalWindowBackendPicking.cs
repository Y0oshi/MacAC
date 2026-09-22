using Silk.NET.Core.Loader;
using Silk.NET.GLFW;
using Silk.NET.Windowing.Glfw;

namespace MacAC.Client.Machine;

internal enum GraphicalReadoutProtocol
{
    Unknown,
    Windows,
    X11,
    Wayland,
    Cocoa,
    Automatic,
}

// Immutable process-start decision for Silk's GLFW 3.4 backend
internal sealed record GraphicalWindowBackendPicking(GraphicalReadoutProtocol RequestedProtocol, string Reason)
{
    internal const string SurroundingsVariable = "MACAC_DISPLAY_PROTOCOL";

    internal static GraphicalWindowBackendPicking Resolve(GraphicalHubOperatingSys operatingSys, Func<string, string?> surroundings)
    {
        ArgumentNullException.ThrowIfNull(surroundings);
        switch (operatingSys)
        {
            case GraphicalHubOperatingSys.Windows:
                return new(GraphicalReadoutProtocol.Windows, "Windows graphical host");
            case GraphicalHubOperatingSys.MacOS:
                return new(GraphicalReadoutProtocol.Cocoa, "macOS graphical host");
        }

        if (surroundings(SurroundingsVariable) is { } forced && !string.IsNullOrWhiteSpace(forced))
        {
            string word = forced.Trim().ToLowerInvariant();
            GraphicalReadoutProtocol protocol = word switch
            {
                "auto" => GraphicalReadoutProtocol.Automatic,
                "x11" => GraphicalReadoutProtocol.X11,
                "wayland" => GraphicalReadoutProtocol.Wayland,
                _ => throw new InvalidOperationException(
                    $"{SurroundingsVariable} has to be auto, x11, or wayland; received '{forced}'."),
            };
            return new(protocol, $"{SurroundingsVariable}={word}");
        }

        bool waylandReadout = !string.IsNullOrWhiteSpace(surroundings("WAYLAND_DISPLAY"));
        if (string.Equals(surroundings("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) && waylandReadout)
            return new(GraphicalReadoutProtocol.Wayland, "XDG_SESSION_TYPE=wayland and WAYLAND_DISPLAY is set");
        if (!string.IsNullOrWhiteSpace(surroundings("DISPLAY")))
            return new(GraphicalReadoutProtocol.X11, "DISPLAY is set");
        return waylandReadout
            ? new(GraphicalReadoutProtocol.Wayland, "WAYLAND_DISPLAY is set")
            : new(GraphicalReadoutProtocol.Automatic, "no X11 or Wayland environment was detected");
    }
}

// GLFW 3.4 platform init-hint values, shared by the arranger and the probe
internal static class GlfwPlatformCodes
{
    internal const int PlatformPrimeHint = 0x00050003;
    internal const int Any = 0x00060000;
    internal const int Win32 = 0x00060001;
    internal const int Cocoa = 0x00060002;
    internal const int Wayland = 0x00060003;
    internal const int X11 = 0x00060004;

    internal static int For(GraphicalReadoutProtocol protocol)
    {
        return protocol switch
        {
            GraphicalReadoutProtocol.Windows => Win32,
            GraphicalReadoutProtocol.X11 => X11,
            GraphicalReadoutProtocol.Wayland => Wayland,
            GraphicalReadoutProtocol.Cocoa => Cocoa,
            GraphicalReadoutProtocol.Automatic => Any,
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
    }

    internal static GraphicalReadoutProtocol Unpack(int platform)
    {
        return platform switch
        {
            X11 => GraphicalReadoutProtocol.X11,
            Wayland => GraphicalReadoutProtocol.Wayland,
            Win32 => GraphicalReadoutProtocol.Windows,
            Cocoa => GraphicalReadoutProtocol.Cocoa,
            _ => GraphicalReadoutProtocol.Unknown,
        };
    }
}

// Applies the picked protocol to Silk's process-global GLFW once; a second, different request is
// an error
internal static class GraphicalWindowBackendArranger
{
    private static readonly Lock Latch = new();
    private static GraphicalReadoutProtocol? _imposed;
    private static Glfw? _glfw;

    internal static void Configure(GraphicalHubPlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var wanted = platform.WindowBackend.RequestedProtocol;

        lock (Latch)
        {
            if (_imposed is { } already)
            {
                if (already != wanted)
                {
                    throw new InvalidOperationException(
                        $"The GLFW platform is by now configured as {already}; it can't change to {wanted} in the same process");
                }

                return;
            }

            PreferPublishedNativeLibraries();
            GlfwWindowing.Use();
            // Silk's windowing backend initializes this exact singleton. A
            // separate Glfw.GetApi() instance would receive the hint but would
            // not own the window backend's process-global GLFW state.
            Glfw glfw = GlfwProvider.UninitializedGLFW.Value;
            GraphicalVkFetcher.ConfigureForGlfw(platform.OperatingSystem, glfw);
            glfw.InitHint((InitHint)GlfwPlatformCodes.PlatformPrimeHint, GlfwPlatformCodes.For(wanted));
            if (platform.OperatingSystem == GraphicalHubOperatingSys.Windows)
                Win32GlfwActiveWindowWarden.Install();
            _glfw = glfw;
            _imposed = wanted;
        }
    }

    internal static bool TryFetchConfiguredApi(out Glfw? glfw)
    {
        lock (Latch)
        {
            glfw = _glfw;
            return glfw is not null;
        }
    }

    // Puts the application directory first so the published natives beat any system copies
    private static void PreferPublishedNativeLibraries()
    {
        if (PathResolver.Default is not DefaultPathResolver locator)
            throw new InvalidOperationException("Silk.NET's default native path resolver is not available");

        var chain = locator.Resolvers;
        chain.Remove(DefaultPathResolver.BaseDirectoryResolver);
        chain.Insert(0, DefaultPathResolver.BaseDirectoryResolver);
    }
}

// Asks the configured GLFW which native platform it actually chose, and its version string
internal static unsafe class GlfwNativePlatformSensor
{
    internal static GraphicalReadoutProtocol FetchEngagedProtocol(GraphicalHubOperatingSys operatingSys)
    {
        return operatingSys switch
        {
            GraphicalHubOperatingSys.Windows => GraphicalReadoutProtocol.Windows,
            GraphicalHubOperatingSys.MacOS => GraphicalReadoutProtocol.Cocoa,
            _ => !GraphicalWindowBackendArranger.TryFetchConfiguredApi(out Glfw? glfw)
                        || glfw is null
                        || !glfw.Context.TryGetProcAddress("glfwGetPlatform", out nint export)
                        ? GraphicalReadoutProtocol.Unknown
                        : GlfwPlatformCodes.Unpack(((delegate* unmanaged[Cdecl]<int>)export)()),
        };
    }

    internal static string FetchVer(GraphicalHubOperatingSys operatingSys)
    {
        return GraphicalWindowBackendArranger.TryFetchConfiguredApi(out Glfw? glfw) && glfw is not null
            ? glfw.GetVersionString() ?? "unknown"
            : "unknown";
    }
}

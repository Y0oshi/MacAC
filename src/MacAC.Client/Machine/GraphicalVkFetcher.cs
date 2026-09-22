using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;

namespace MacAC.Client.Machine;

// Where a packaged macOS engine keeps its own Vulkan loader, MoltenVK and the ICD manifest
internal sealed record PackagedVkArrangement(string LoaderPath, string DriverLibraryPath, string DriverManifestPath);

// On macOS the engine may ship its own Vulkan loader under Frameworks/ with a MoltenVK ICD under
// Resources/vulkan/
internal static unsafe class GraphicalVkFetcher
{
    internal const string DriverFilesSurroundingsVariable = "VK_DRIVER_FILES";

    private static readonly Lock Latch = new();
    private static string? _fetcherTrail;
    private static nint _fetcherHnd;

    internal static void ConfigureForGlfw(GraphicalHubOperatingSys operatingSys, Glfw glfw)
    {
        ArgumentNullException.ThrowIfNull(glfw);
        if (operatingSys != GraphicalHubOperatingSys.MacOS)
            return;
        if (LocatePackagedArrangement(AppContext.BaseDirectory, File.Exists) is not { } arrangement)
            return;

        lock (Latch)
        {
            if (_fetcherTrail is not null)
            {
                if (!string.Equals(_fetcherTrail, arrangement.LoaderPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The packaged Vulkan loader was by now configured from a different application directory");
                }

                return;
            }

            string? precedingDriverFiles = Environment.GetEnvironmentVariable(DriverFilesSurroundingsVariable);
            AssignNativeSurroundingsVariable(DriverFilesSurroundingsVariable, arrangement.DriverManifestPath);

            nint hnd = NativeLibrary.Load(arrangement.LoaderPath);
            try
            {
                if (!NativeLibrary.TryGetExport(hnd, "vkGetInstanceProcAddr", out nint fetchInstProcAddr))
                {
                    throw new EntryPointNotFoundException(
                        $"The packaged Vulkan loader '{arrangement.LoaderPath}' doesn't export vkGetInstanceProcAddr");
                }

                if (!glfw.Context.TryGetProcAddress("glfwInitVulkanLoader", out nint primeVulkanFetcher))
                {
                    throw new EntryPointNotFoundException(
                        "The published GLFW library doesn't export glfwInitVulkanLoader; GLFW 3.4 or newer is needed");
                }

                ((delegate* unmanaged[Cdecl]<nint, void>)primeVulkanFetcher)(fetchInstProcAddr);
                _fetcherHnd = hnd;
                _fetcherTrail = arrangement.LoaderPath;
            }
            catch
            {
                NativeLibrary.Free(hnd);
                AssignNativeSurroundingsVariable(DriverFilesSurroundingsVariable, precedingDriverFiles);
                throw;
            }
        }
    }

    // Silk's Vulkan API bound to the packaged loader when one was configured, else the default
    // resolution
    internal static Vk BuildApi()
    {
        lock (Latch)
        {
            if (_fetcherTrail is not { } fetcherTrail)
                return Vk.GetApi();
            return _fetcherHnd == 0
                ? throw new InvalidOperationException("The packaged Vulkan loader handle is not available")
                : new Vk(new DefaultNativeContext(fetcherTrail));
        }
    }

    internal static PackagedVkArrangement? LocatePackagedArrangement(string applicationFolder, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationFolder);
        ArgumentNullException.ThrowIfNull(fileExists);

        string trunk = Path.GetFullPath(applicationFolder);
        PackagedVkArrangement arrangement = new PackagedVkArrangement(
            Path.Combine(trunk, "Frameworks", "libvulkan.1.dylib"),
            Path.Combine(trunk, "Frameworks", "libMoltenVK.dylib"),
            Path.Combine(trunk, "Resources", "vulkan", "icd.d", "MoltenVK_icd.json"));

        (string Label, string Path)[] pieces =
        [
            ("Vulkan loader", arrangement.LoaderPath),
            ("MoltenVK driver", arrangement.DriverLibraryPath),
            ("MoltenVK driver manifest", arrangement.DriverManifestPath),
        ];
        string[] absent = [.. pieces.Where(p => !fileExists(p.Path)).Select(p => $"{p.Label}: {p.Path}")];
        if (absent.Length == pieces.Length)
            return null;
        return absent.Length is not 0
            ? throw new InvalidOperationException(
                "The packaged macOS Vulkan runtime is incomplete. Absent " + string.Join("; ", absent))
            : arrangement;
    }

    // Sets a variable in the process environment the native Vulkan loader reads with getenv()
    private static void AssignNativeSurroundingsVariable(string label, string? val)
    {
        Environment.SetEnvironmentVariable(label, val);
        _ = val is null ? unsetenv(label) : setenv(label, val, 1);
    }

    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int unsetenv(string name);
}

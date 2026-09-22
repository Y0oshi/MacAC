using System.Runtime.Loader;
using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Mechanics.PluginHosting;

/// <summary>The outcome of loading one plugin directory: instances, load context, or the failure.</summary>
public sealed record MountedPlugin(
    PluginCard Manifest,
    IMacACExtension? Plugin,
    AssemblyLoadContext? LoadContext,
    Exception? Error,
    IRenderPackExtension? RenderPackPlugin = null)
{
    public bool Success
    {
        get
        {
            return Error is null && LoadContext is not null && (Plugin is not null || RenderPackPlugin is not null);
        }
    }
}

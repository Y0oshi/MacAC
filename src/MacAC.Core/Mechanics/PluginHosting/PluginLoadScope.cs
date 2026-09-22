using System.Reflection;
using System.Runtime.Loader;

namespace MacAC.Mechanics.PluginHosting;

// A collectible load context per plugin
internal sealed class PluginLoadScope(string extensionFolder, string extensionListingTrail)
    : AssemblyLoadContext(name: extensionFolder, isCollectible: true)
{
    private const string SharedContractAssembly = "MacAC.Core";

    private readonly AssemblyDependencyResolver _locator = new(extensionListingTrail);

    protected override Assembly? Load(AssemblyName assemblyLabel)
    {
        if (assemblyLabel.Name == SharedContractAssembly)
            return null;
        string? trail = _locator.ResolveAssemblyToPath(assemblyLabel);
        return trail is null ? null : LoadFromAssemblyPath(trail);
    }
}

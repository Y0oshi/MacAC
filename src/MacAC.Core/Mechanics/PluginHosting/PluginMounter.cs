using System.Reflection;
using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Mechanics.PluginHosting;

public static class PluginMounter
{
    public static MountedPlugin Load(
        string extensionFolder,
        PluginCard manifest,
        IExtensionHost hub,
        IRenderPackShelf? rasterizeBundles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionFolder);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(hub);

        if (!ExtensionContract.IsSupported(manifest.ApiVersion))
        {
            return Failed(manifest, null, new PluginContractException(
                $"plugin '{manifest.Id}' declares apiVersion {manifest.ApiVersion}, "
                + $"but this build supports {ExtensionContract.FloorSupported}"
                + $"..{ExtensionContract.Current}"));
        }

        string dllTrail = Path.Combine(extensionFolder, manifest.EntryDll);
        if (!File.Exists(dllTrail))
            return Failed(manifest, null, new FileNotFoundException($"entry dll not found: {dllTrail}", dllTrail));

        PluginLoadScope? ambit = null;
        IMacACExtension? gameplay = null;
        IRenderPackExtension? rasterizeBundle = null;
        try
        {
            ambit = new PluginLoadScope(extensionFolder, dllTrail);
            Type[] concrete = ConcreteKinds(ambit.LoadFromAssemblyPath(dllTrail));

            bool wantsGameplay = manifest.Declares(PluginFlavor.Gameplay);
            bool wantsRasterizeBundle = manifest.Declares(PluginFlavor.RenderPack) && rasterizeBundles is not null;

            Type? gameplayKind = wantsGameplay
                ? concrete.FirstOrDefault(static t => typeof(IMacACExtension).IsAssignableFrom(t))
                : null;

            Type? rasterizeBundleKind = null;
            if (wantsRasterizeBundle && !TryChooseRasterizeBundleKind(manifest, concrete, out rasterizeBundleKind, out Exception? why))
                return Failed(manifest, ambit, why!);

            if (wantsGameplay && gameplayKind is null)
                return Failed(manifest, ambit, new InvalidOperationException($"no IMacACExtension implementation found in {manifest.EntryDll}"));
            if (wantsRasterizeBundle && rasterizeBundleKind is null)
                return Failed(manifest, ambit, new InvalidOperationException($"no IRenderPackExtension implementation found in {manifest.EntryDll}"));
            if (gameplayKind is null && rasterizeBundleKind is null)
                return Failed(manifest, ambit, new InvalidOperationException("the host didn't supply any facility declared by this plugin"));

            object? shared = null;
            if (gameplayKind is not null)
            {
                shared = Activator.CreateInstance(gameplayKind);
                gameplay = (IMacACExtension?)shared
                    ?? throw new InvalidOperationException($"could not construct IMacACExtension {gameplayKind.FullName}");
                gameplay.Bootstrap(hub);
            }

            if (rasterizeBundleKind is not null)
            {
                object inst = ReferenceEquals(rasterizeBundleKind, gameplayKind)
                    ? shared!
                    : Activator.CreateInstance(rasterizeBundleKind)
                        ?? throw new InvalidOperationException($"could not construct IRenderPackExtension {rasterizeBundleKind.FullName}");
                rasterizeBundle = (IRenderPackExtension)inst;

                CountingShelf counting = new CountingShelf(rasterizeBundles!);
                rasterizeBundle.Register(counting);
                if (counting.EnrollmentTally is 0)
                    throw new InvalidOperationException($"render-pack entry point '{rasterizeBundleKind.FullName}' registered no packs");
            }

            return new MountedPlugin(manifest, gameplay, ambit, Error: null, rasterizeBundle);
        }
        catch (Exception problem)
        {
            return new MountedPlugin(manifest, gameplay, ambit, problem, rasterizeBundle);
        }
    }

    private static MountedPlugin Failed(PluginCard manifest, PluginLoadScope? ambit, Exception problem) =>
        new(manifest, Plugin: null, LoadContext: ambit, Error: problem);

    private static Type[] ConcreteKinds(Assembly assembly)
    {
        IEnumerable<Type> kinds;
        try
        {
            kinds = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            kinds = partial.Types.OfType<Type>();
        }
        return kinds.Where(static t => !t.IsAbstract && !t.IsInterface).ToArray();
    }

    // Exactly one public, default-constructible IRenderPackExtension is required
    private static bool TryChooseRasterizeBundleKind(PluginCard manifest, Type[] concrete, out Type? kind, out Exception? why)
    {
        kind = null;
        why = null;
        Type[] contenders = concrete.Where(static t => typeof(IRenderPackExtension).IsAssignableFrom(t)).ToArray();
        if (contenders.Length is not 1)
        {
            why = new InvalidOperationException(
                $"render-pack entry DLL '{manifest.EntryDll}' must contain precisely "
                + "one IRenderPackExtension implementation; found " + contenders.Length);
            return false;
        }

        Type sole = contenders[0];
        if (!sole.IsVisible || sole.GetConstructor(Type.EmptyTypes) is null)
        {
            why = new InvalidOperationException(
                $"render-pack entry type '{sole.FullName}' has to be public, "
                + "non-abstract, and expose a public parameterless constructor");
            return false;
        }

        kind = sole;
        return true;
    }

    // Wraps the host shelf so the mounter can tell whether Register was ever called
    private sealed class CountingShelf(IRenderPackShelf interior) : IRenderPackShelf
    {
        private int _online;

        internal int EnrollmentTally => Volatile.Read(ref _online);

        public IDisposable Register(RenderPackCard descriptor, IRenderPackFiles holdings)
        {
            IDisposable hnd = interior.Register(descriptor, holdings)
                ?? throw new InvalidOperationException("The render-pack registry returned a null registration handle");
            Interlocked.Increment(ref _online);
            return new Handle(this, hnd);
        }

        private sealed class Handle(CountingShelf holder, IDisposable interior) : IDisposable
        {
            private CountingShelf? _holder = holder;
            private IDisposable? _interior = interior;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _holder, null) is not { } shelf)
                    return;
                Interlocked.Decrement(ref shelf._online);
                Interlocked.Exchange(ref _interior, null)?.Dispose();
            }
        }
    }
}

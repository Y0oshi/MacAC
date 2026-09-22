namespace MacAC.Host;

internal readonly record struct StateRoots(
    string Config,
    string Data,
    string Cache,
    string? PriorConfig);

internal interface IStateRootConvention
{
    StateRoots Trunks(IHostProbe sensor);
}

internal static class StateRootConventions
{
    private const string Leaf = "macac";

    internal static IStateRootConvention For(HostFlavor flavor)
    {
        return flavor switch
        {
            HostFlavor.Windows => AppDataConvention.Instance,
            HostFlavor.MacOS => LibraryConvention.Instance,
            _ => XdgConvention.Instance,
        };
    }

    internal static string Folder(
        IHostProbe sensor,
        Environment.SpecialFolder folder)
    {
        string located = sensor.RecognizedFolder(folder);
        return string.IsNullOrWhiteSpace(located)
            ? throw new InvalidOperationException(
                $"The operating system didn't provide {folder}.")
            : located;
    }

    internal static string XdgTrunk(
        IHostProbe sensor,
        string variable,
        string backup)
    {
        string? chosen = sensor.ScanVariable(variable);
        return Path.Combine(
            string.IsNullOrWhiteSpace(chosen) ? backup : chosen,
            Leaf);
    }

    private sealed class AppDataConvention : IStateRootConvention
    {
        internal static AppDataConvention Instance { get; } = new();

        public StateRoots Trunks(IHostProbe sensor)
        {
            string roaming = Folder(
                sensor,
                Environment.SpecialFolder.ApplicationData);
            string own = Folder(
                sensor,
                Environment.SpecialFolder.LocalApplicationData);
            string ownLeaf = Path.Combine(own, Leaf);
            return new StateRoots(
                Config: Path.Combine(roaming, Leaf),
                Data: ownLeaf,
                Cache: Path.Combine(ownLeaf, "cache"),
                PriorConfig: ownLeaf);
        }
    }

    private sealed class LibraryConvention : IStateRootConvention
    {
        internal static LibraryConvention Instance { get; } = new();

        public StateRoots Trunks(IHostProbe sensor)
        {
            string home = Folder(
                sensor,
                Environment.SpecialFolder.UserProfile);
            string support = Path.Combine(
                home,
                "Library",
                "Application Support",
                Leaf);
            return new StateRoots(
                Config: Path.Combine(support, "config"),
                Data: support,
                Cache: Path.Combine(home, "Library", "Caches", Leaf),
                PriorConfig: XdgTrunk(
                    sensor,
                    "XDG_CONFIG_HOME",
                    Path.Combine(home, ".config")));
        }
    }

    private sealed class XdgConvention : IStateRootConvention
    {
        internal static XdgConvention Instance { get; } = new();

        public StateRoots Trunks(IHostProbe sensor)
        {
            string home = Folder(
                sensor,
                Environment.SpecialFolder.UserProfile);
            return new StateRoots(
                Config: XdgTrunk(
                    sensor,
                    "XDG_CONFIG_HOME",
                    Path.Combine(home, ".config")),
                Data: XdgTrunk(
                    sensor,
                    "XDG_DATA_HOME",
                    Path.Combine(home, ".local", "share")),
                Cache: XdgTrunk(
                    sensor,
                    "XDG_CACHE_HOME",
                    Path.Combine(home, ".cache")),
                PriorConfig: null);
        }
    }
}

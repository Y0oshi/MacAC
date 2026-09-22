using MacAC.Host;

namespace MacAC.Client.Machine;

internal static class GraphicalLegacySetupMigrator
{
    private static readonly string[] Carried = ["settings.json", "keybinds.json"];

    // The destination paths that were freshly copied
    internal static IReadOnlyList<string> Migrate(UserStateLayout trails)
    {
        ArgumentNullException.ThrowIfNull(trails);
        if (trails.PriorConfig is not { } preceding || SameTrail(preceding, trails.Config))
            return [];

        List<string>? copied = null;
        foreach (string label in Carried)
        {
            string from = Path.Combine(preceding, label);
            string to = Path.Combine(trails.Config, label);
            if (!File.Exists(from) || File.Exists(to))
                continue;

            Directory.CreateDirectory(trails.Config);
            File.Copy(from, to, overwrite: false);
            (copied ??= []).Add(to);
        }

        return copied ?? [];
    }

    private static bool SameTrail(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

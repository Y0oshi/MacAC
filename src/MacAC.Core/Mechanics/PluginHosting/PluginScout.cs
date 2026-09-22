namespace MacAC.Mechanics.PluginHosting;

public sealed record PluginScoutHit(
    string PluginDirectory,
    PluginCard? Manifest,
    Exception? Error)
{
    public bool Success => Manifest is not null && Error is null;
}

/// <summary>Finds every plugin.json one level below a root, in ordinal directory order.</summary>
public static class PluginScout
{
    private const string ManifestFileLabel = "plugin.json";

    public static IReadOnlyList<PluginScoutHit> Scan(string extensionsTrunkFolder)
    {
        if (!Directory.Exists(extensionsTrunkFolder))
            return [];

        List<PluginScoutHit> strikes = new List<PluginScoutHit>();
        foreach (string folder in Directory.EnumerateDirectories(extensionsTrunkFolder).Order(StringComparer.Ordinal))
        {
            string manifestTrail = Path.Combine(folder, ManifestFileLabel);
            if (!File.Exists(manifestTrail))
                continue;
            try
            {
                strikes.Add(new PluginScoutHit(folder, PluginCard.Parse(File.ReadAllText(manifestTrail)), null));
            }
            catch (Exception problem)
            {
                strikes.Add(new PluginScoutHit(folder, null, problem));
            }
        }
        return strikes;
    }
}

using System.Text;
using MacAC.Extensibility.Hosting;

namespace MacAC.Client.Extensions;

internal sealed class FilePluginVault : IExtensionVault
{
    private readonly string _trunk;

    internal FilePluginVault(string trunk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trunk);
        _trunk = Path.GetFullPath(trunk);
    }

    public bool IsAvailable => true;

    public string? ScanPhrase(string tag)
    {
        string trail = Resolve(tag);
        return File.Exists(trail)
            ? File.ReadAllText(trail, Encoding.UTF8)
            : null;
    }

    public IReadOnlyList<string> List(string stem)
    {
        ArgumentNullException.ThrowIfNull(stem);
        string folder = stem.Length is 0 ? _trunk : Resolve(stem);
        return !Directory.Exists(folder)
            ? Array.Empty<string>()
            : [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(trail => Path.GetRelativePath(_trunk, trail)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)];
    }

    public void EmitPhrase(string tag, string substance)
    {
        ArgumentNullException.ThrowIfNull(substance);
        string trail = Resolve(tag);
        string folder = Path.GetDirectoryName(trail)!;
        EmitPhraseRest(substance, trail, folder);
    }

    private void EmitPhraseRest(string substance, string trail, string folder)
    {
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(
                    folder,
                    $".{Path.GetFileName(trail)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, substance, new UTF8Encoding(false));
            File.Move(temporary, trail, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public bool Delete(string tag)
    {
        string trail = Resolve(tag);
        if (!File.Exists(trail))
            return false;
        File.Delete(trail);
        return true;
    }

    private string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Path.IsPathRooted(key))
            throw new ArgumentException("Plugin storage keys has to be relative", nameof(key));
        string trail = Path.GetFullPath(Path.Combine(_trunk, key));
        string relative = Path.GetRelativePath(_trunk, trail);
        return Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            ? throw new ArgumentException("Plugin storage key escapes its root", nameof(key))
            : trail;
    }
}

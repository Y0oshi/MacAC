using System.Text.Json;

namespace MacAC.Mechanics.PluginHosting;

/// <summary>A host facility a plugin declares in <c>plugin.json</c>.</summary>
public enum PluginFlavor
{
    Gameplay,
    RenderPack,
}

public sealed class PluginContractException(string msg) : Exception(msg);

public sealed class PluginCardException : Exception
{
    public PluginCardException(string msg) : base(msg)
    {
    }

    public PluginCardException(string msg, Exception interior) : base(msg, interior)
    {
    }
}

/// <summary>The parsed <c>plugin.json</c>.</summary>
public sealed record PluginCard(
    string Id,
    string DisplayName,
    string Version,
    string EntryDll,
    int ApiVersion,
    IReadOnlyList<string> Dependencies)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<PluginFlavor> Kinds { get; init; } = [PluginFlavor.Gameplay];

    public PluginCard(
        string Ident,
        string ReadoutLabel,
        string Ver,
        string ListingDll,
        int ApiVer,
        IReadOnlyList<string> Deps,
        IReadOnlyList<PluginFlavor> Kinds)
        : this(Ident, ReadoutLabel, Ver, ListingDll, ApiVer, Deps)
    {
        ArgumentNullException.ThrowIfNull(Kinds);
        if (Kinds.Count is 0)
            throw new ArgumentException("At least one plugin kind is needed", nameof(Kinds));
        this.Kinds = Kinds.Distinct().ToArray();
    }

    public bool Declares(PluginFlavor sort) => Kinds.Contains(sort);

    public static PluginCard Parse(string json)
    {
        Shape? form;
        try
        {
            form = JsonSerializer.Deserialize<Shape>(json, Json);
        }
        catch (JsonException problem)
        {
            throw new PluginCardException($"not valid json: {problem.Message}", problem);
        }
        if (form is null)
            throw new PluginCardException("manifest is empty");

        string ident = Required(form.Id, "id");
        string readoutLabel = Required(form.DisplayName, "displayName");
        string ver = Required(form.Version, "version");
        string listingDll = Required(form.EntryDll, "entryDll");
        if (form.ApiVersion <= 0)
            throw new PluginCardException("apiVersion has to be >= 1");

        return new PluginCard(ident, readoutLabel, ver, listingDll, form.ApiVersion, form.Dependencies ?? [], Flavors(form.Kinds));
    }

    private static IReadOnlyList<PluginFlavor> Flavors(IReadOnlyList<string>? labels)
    {
        if (labels is null)
            return [PluginFlavor.Gameplay];
        if (labels.Count is 0)
            throw new PluginCardException("kinds must contain no fewer than one entry");

        List<PluginFlavor> flavors = new List<PluginFlavor>(labels.Count);
        foreach (string? label in labels)
        {
            if (string.IsNullOrWhiteSpace(label)
                || !Enum.TryParse(label, ignoreCase: true, out PluginFlavor flavor)
                || !Enum.IsDefined(flavor))
            {
                throw new PluginCardException($"unrecognized plugin kind: {label ?? "<null>"}");
            }
            if (!flavors.Contains(flavor))
                flavors.Add(flavor);
        }
        return flavors;
    }

    private static string Required(string? val, string field)
    {
        return string.IsNullOrWhiteSpace(val)
            ? throw new PluginCardException($"absent needed field: {field}")
            : val;
    }

    private sealed class Shape
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Version { get; set; }

        public string? EntryDll { get; set; }

        public int ApiVersion { get; set; }

        public IReadOnlyList<string>? Dependencies { get; set; }

        public IReadOnlyList<string>? Kinds { get; set; }
    }
}

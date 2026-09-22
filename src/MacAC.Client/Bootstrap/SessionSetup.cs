using System.Text.Json.Serialization;

namespace MacAC.Client.Bootstrap;

internal sealed class SessionSetup
{
    [JsonRequired]
    public int Version { get; init; }

    public SessionProcessPreferences? Process { get; init; }

    [JsonRequired]
    public List<SessionSpec?> Sessions { get; init; } = [];
}

internal sealed class SessionProcessPreferences
{
    public SessionContentSpec? Content { get; init; }

    public SessionProcTrailSubstitutions? Paths { get; init; }
}

internal sealed class SessionProcTrailSubstitutions
{
    public string? ConfigDirectory { get; init; }
    public string? DataDirectory { get; init; }
    public string? CacheDirectory { get; init; }
}

internal sealed class SessionContentSpec
{
    [JsonRequired]
    public string DatDirectory { get; init; } = string.Empty;

    public string PreparedAssetPath { get; init; } = string.Empty;

    public string? PreparedAssetOverlayPath { get; init; }

    public uint? PreparedAssetBaseRecipeVersion { get; init; }

    public uint? PreparedAssetEffectiveRecipeVersion { get; init; }
}

internal sealed record SessionSpec
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public SessionEndpointSpec Endpoint { get; init; } = new();

    [JsonRequired]
    public string Account { get; init; } = string.Empty;

    public SessionToonSelectorSpec? Character { get; init; }

    public SessionRuleSpec? Policy { get; init; }

    public string? Mode { get; init; }

    [JsonRequired]
    public SessionSecretSpec Credential { get; init; } = new();

    // Accepted-but-ignored by App; Headless's own loader owns the allow-list semantics for this field
    // (D8)
    public Dictionary<string, bool>? CharacterOptions { get; init; }

    // LA1/LA5: plugin ids to load
    public List<string>? Plugins { get; init; }

    public List<string>? LoginCommands { get; init; }

    public int LoginCommandDelayMs { get; init; } = 500;

    public string? StatusFile { get; init; }
}

internal sealed class SessionEndpointSpec
{
    [JsonRequired]
    public string Host { get; init; } = string.Empty;

    [JsonRequired]
    public int Port { get; init; }
}

internal sealed class SessionToonSelectorSpec
{
    public int? Index { get; init; }
    public uint? Id { get; init; }
    public string? Name { get; init; }
}

internal sealed class SessionRuleSpec
{
    public string? Id { get; init; }
    public string? Role { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SessionSecretSupplierKind>))]
internal enum SessionSecretSupplierKind
{
    Environment,
    StandardInput,
    File,
}

internal sealed class SessionSecretSpec
{
    [JsonRequired]
    public SessionSecretSupplierKind Provider { get; init; }

    [JsonRequired]
    public string Reference { get; init; } = string.Empty;
}

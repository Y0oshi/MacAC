using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacAC.Cockpit.Settings;

namespace MacAC.Cockpit.Panels.Settings;

/// <summary>JSON plumbing: tolerant field readers, in-place edits, and the section writer.</summary>
public sealed partial class SettingsVault
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private JsonObject? ScanTrunk() => JsonNode.Parse(File.ReadAllText(_trail)) as JsonObject;

    private void Correct(Action<JsonObject> edit)
    {
        string? direction = Path.GetDirectoryName(_trail);
        if (!string.IsNullOrEmpty(direction))
            Directory.CreateDirectory(direction);

        JsonObject trunk = new();
        if (File.Exists(_trail))
        {
            try { trunk = ScanTrunk() ?? new JsonObject(); }
            catch { trunk = new JsonObject(); }
        }

        edit(trunk);
        trunk["version"] = LatestSchemaVer;
        File.WriteAllText(_trail, trunk.ToJsonString(Indented));
    }

    // The child object under tag, created when absent or not an object
    private static JsonObject Branch(JsonObject ancestor, string tag)
    {
        if (ancestor[tag] is JsonObject extant)
            return extant;
        JsonObject built = new JsonObject();
        ancestor[tag] = built;
        return built;
    }

    // JsonNode readers that fall back on a missing key or a value of the wrong type
    private static class CockpitNode
    {
        public static float Float(JsonObject joint, string tag, float backup)
        {
            try { return joint[tag]?.GetValue<float>() ?? backup; }
            catch { return backup; }
        }

        public static bool Bool(JsonObject joint, string tag, bool backup)
        {
            try { return joint[tag]?.GetValue<bool>() ?? backup; }
            catch { return backup; }
        }

        public static int Int(JsonObject joint, string tag, int backup)
        {
            try { return joint[tag]?.GetValue<int>() ?? backup; }
            catch { return backup; }
        }
    }

    // Rewrites the file with the named section replaced
    private void PersistSection(string label, SortedDictionary<string, object> cargo)
    {
        string? direction = Path.GetDirectoryName(_trail);
        if (!string.IsNullOrEmpty(direction))
            Directory.CreateDirectory(direction);

        var kept = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(_trail))
        {
            try
            {
                using FileStream flow = File.OpenRead(_trail);
                using var doc = JsonDocument.Parse(flow);
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name != label && prop.Name != "version")
                        kept[prop.Name] = prop.Value.GetRawText();
                }
            }
            catch
            {
                kept.Clear();
            }
        }

        StringBuilder phrase = new StringBuilder();
        phrase.Append('{').AppendLine();
        foreach ((string tag, string raw) in kept)
            phrase.Append("  \"").Append(tag).Append("\": ").Append(raw).Append(',').AppendLine();
        phrase.Append("  \"").Append(label).Append("\": ")
            .Append(JsonSerializer.Serialize(cargo, Indented).Replace("\n", "\n  "))
            .Append(',').AppendLine();
        phrase.Append("  \"version\": ").Append(LatestSchemaVer).AppendLine();
        phrase.Append('}').AppendLine();

        File.WriteAllText(_trail, phrase.ToString());
    }

    // JsonElement readers that fall back on a missing key or a value of the wrong kind
    private static class Field
    {
        public static string Text(JsonElement objRef, string label, string backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.String
                ? elem.GetString() ?? backup
                : backup;
        }

        public static bool Flag(JsonElement objRef, string label, bool backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? elem.GetBoolean()
                : backup;
        }

        public static float Float(JsonElement objRef, string label, float backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.Number ? elem.GetSingle() : backup;
        }

        public static int Int(JsonElement objRef, string label, int backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.Number ? elem.GetInt32() : backup;
        }

        public static ulong ULong(JsonElement objRef, string label, ulong backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.Number && elem.TryGetUInt64(out ulong val)
                ? val
                : backup;
        }

        public static QualityTier Quality(JsonElement objRef, string label, QualityTier backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.String
                && Enum.TryParse(elem.GetString(), ignoreCase: true, out QualityTier tier)
                ? tier
                : backup;
        }

        public static MoteSpan Motes(JsonElement objRef, string label, MoteSpan backup)
        {
            return objRef.TryGetProperty(label, out JsonElement elem) && elem.ValueKind == JsonValueKind.String
                && Enum.TryParse(elem.GetString(), ignoreCase: true, out MoteSpan span)
                && Enum.IsDefined(span)
                ? span
                : backup;
        }
    }

    // The "renderPack" object inside the display section
    private static class RenderPackCodec
    {
        // A pack needs both ids; a malformed overrides map discards the whole selection
        public static RenderPackPick Read(JsonElement readout, RenderPackPick backup)
        {
            if (!readout.TryGetProperty("renderPack", out JsonElement val) || val.ValueKind != JsonValueKind.Object)
                return backup;

            string bundleIdent = Field.Text(val, "packId", string.Empty);
            string presetIdent = Field.Text(val, "presetId", string.Empty);
            if (string.IsNullOrWhiteSpace(bundleIdent) || string.IsNullOrWhiteSpace(presetIdent))
                return backup;

            string? ver = val.TryGetProperty("packVersion", out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

            var substitutions = new List<KeyValuePair<string, string>>();
            if (val.TryGetProperty("settingOverrides", out JsonElement lookup))
            {
                if (lookup.ValueKind != JsonValueKind.Object)
                    return backup;
                foreach (JsonProperty prop in lookup.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String || prop.Value.GetString() is not { } setting)
                        return backup;
                    substitutions.Add(new KeyValuePair<string, string>(prop.Name, setting));
                }
            }

            return new RenderPackPick(bundleIdent, ver, presetIdent)
            {
                SettingOverrides = new RenderPackOverrides(substitutions),
            };
        }

        public static SortedDictionary<string, object?> Write(RenderPackPick choose)
        {
            var substitutions = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach ((string tag, string val) in choose.SettingOverrides)
                substitutions[tag] = val;
            return new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["packId"] = choose.PackId,
                ["packVersion"] = choose.PackVersion,
                ["presetId"] = choose.PresetId,
                ["settingOverrides"] = substitutions,
            };
        }
    }
}

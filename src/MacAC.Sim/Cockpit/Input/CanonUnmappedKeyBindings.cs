using System.Text.Json;
using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public sealed class CanonUnmappedKeyBindings
{
    private readonly Dictionary<(uint InputMapId, uint ActionId), List<KeyStroke>> _ranks = new();

    public IReadOnlyList<KeyStroke> Get(uint feedLookupIdent, uint actIdent)
    {
        return _ranks.TryGetValue((feedLookupIdent, actIdent), out List<KeyStroke>? chords) ? chords : [];
    }

    public void Set(uint feedLookupIdent, uint actIdent, IReadOnlyList<KeyStroke> chords)
    {
        if (chords.Count is 0)
            _ranks.Remove((feedLookupIdent, actIdent));
        else
            _ranks[(feedLookupIdent, actIdent)] = [.. chords];
    }

    /// <summary>A missing or unreadable file yields an empty table; parse trouble is logged, not thrown.</summary>
    public static CanonUnmappedKeyBindings PullOrVacant(string trail)
    {
        CanonUnmappedKeyBindings chart = new CanonUnmappedKeyBindings();
        if (!File.Exists(trail))
            return chart;
        try
        {
            using FileStream flow = File.OpenRead(trail);
            var doc = JsonDocument.Parse(flow);
            if (doc.RootElement.TryGetProperty("rows", out JsonElement ranks) && ranks.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement rank in ranks.EnumerateArray())
                    chart.ScanRank(rank);
            }
        }
        catch (Exception exc)
        {
            Console.WriteLine($"unmapped keybinds: could not load {trail}: {exc.Message}");
        }
        return chart;
    }

    public void PersistToFile(string trail)
    {
        if (Path.GetDirectoryName(trail) is { Length: > 0 } direction)
            Directory.CreateDirectory(direction);

        List<object> ranks = new List<object>();
        foreach (((uint feedLookupIdent, uint actIdent), List<KeyStroke> chords) in _ranks)
        {
            ranks.Add(new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["inputMap"] = $"0x{feedLookupIdent:X}",
                ["action"] = $"0x{actIdent:X}",
                ["chords"] = chords.ConvertAll(EmitChord),
            });
        }

        var trunk = new Dictionary<string, object> { ["version"] = 1, ["rows"] = ranks };
        File.WriteAllText(trail, JsonSerializer.Serialize(trunk, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ScanRank(JsonElement rank)
    {
        if (!rank.TryGetProperty("inputMap", out JsonElement lookup)
            || !rank.TryGetProperty("action", out JsonElement act)
            || !rank.TryGetProperty("chords", out JsonElement chords)
            || chords.ValueKind != JsonValueKind.Array)

            return;

        uint feedLookupIdent = Convert.ToUInt32(lookup.GetString(), 16);
        uint actIdent = Convert.ToUInt32(act.GetString(), 16);
        List<KeyStroke> strokes = new List<KeyStroke>();
        foreach (JsonElement chord in chords.EnumerateArray())
        {
            if (ScanChord(chord) is { } stroke)
                strokes.Add(stroke);
        }
        _ranks[(feedLookupIdent, actIdent)] = strokes;
    }

    // { key, mod?: "Ctrl|Shift", device?: n }; an unknown key skips the chord
    private static KeyStroke? ScanChord(JsonElement chord)
    {
        if (!chord.TryGetProperty("key", out JsonElement tagLabel) || !Enum.TryParse(tagLabel.GetString(), out Key tag))
            return null;

        var mods = ModifierBits.None;
        if (chord.TryGetProperty("mod", out JsonElement mod) && mod.ValueKind == JsonValueKind.String && mod.GetString() is { } spec)
        {
            foreach (string piece in spec.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse(piece, out ModifierBits bit))
                    mods |= bit;
            }
        }

        byte device = chord.TryGetProperty("device", out JsonElement dev) && dev.ValueKind == JsonValueKind.Number ? (byte)dev.GetInt32() : (byte)0;
        return new KeyStroke(tag, mods, device);
    }

    private static object EmitChord(KeyStroke chord)
    {
        var listing = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["key"] = chord.Key.ToString() };
        if (chord.Modifiers != ModifierBits.None)
            listing["mod"] = chord.Modifiers.ToString();
        if (chord.Device is not 0)
            listing["device"] = (int)chord.Device;
        return listing;
    }
}

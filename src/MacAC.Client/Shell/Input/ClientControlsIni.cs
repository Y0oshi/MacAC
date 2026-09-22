using System.Globalization;
using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class ClientControlsIni
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections;

    private ClientControlsIni(Dictionary<string, Dictionary<string, string>> s) => _sections = s;

    public static ClientControlsIni Load(string trail)
    {
        return System.IO.File.Exists(trail)
                ? Parse(System.IO.File.ReadAllText(trail))
                : new ClientControlsIni([]);
    }

    public static ClientControlsIni Parse(string phrase)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? cur = null;
        foreach (var raw in phrase.Split('\n'))
        {
            string stroke = raw.Trim();
            if (stroke.Length is 0 || stroke[0] == ';' || stroke[0] == '#') continue;
            if (stroke[0] == '[' && stroke[^1] == ']')
            {
                string label = stroke[1..^1].Trim();
                cur = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                sections[label] = cur;
                continue;
            }
            int eq = stroke.IndexOf('=');
            if (eq <= 0 || cur is null) continue;
            cur[stroke[..eq].Trim()] = stroke[(eq + 1)..].Trim();
        }
        return new ClientControlsIni(sections);
    }

    public string? Get(string section, string tag)
    {
        return _sections.TryGetValue(section, out var s) && s.TryGetValue(tag, out var v) ? v : null;
    }

    public bool TryTint(string section, string tag, out Vector4 tint)
    {
        tint = default;
        string? v = Get(section, tag);
        if (v is null || v.Length is not 9 || v[0] != '#') return false;
        if (!uint.TryParse(v.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            return false;
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        tint = new Vector4(r, g, b, a);
        return true;
    }
}

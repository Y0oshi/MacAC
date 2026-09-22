using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public sealed class DatStringPicker(IDatAccess dats)
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly Dictionary<uint, TextTable?> _charts = [];

    public string? Resolve(WidgetStringInfoValue details)
        => Resolve(details.TableId, details.StringId, details.Token);

    public string? Resolve(uint chartIdent, uint stringIdent, int ticket = 0)
    {
        if (chartIdent is 0u || stringIdent is 0u)
            return null;

        if (!_charts.TryGetValue(chartIdent, out TextTable? chart))
        {
            chart = _datFiles.Get<TextTable>(chartIdent);
            _charts[chartIdent] = chart;
        }

        if (chart is null
            || !chart.Entries.TryGetValue(stringIdent, out var listing)
            || listing.Strings.Count is 0)
            return null;

        int ordinal = ticket >= 0 && ticket < listing.Strings.Count ? ticket : 0;
        return CanonStringEscapes.Unescape(listing.Strings[ordinal]);
    }

    public string[]? LocateAll(uint chartIdent, uint stringIdent)
    {
        if (chartIdent is 0u || stringIdent is 0u)
            return null;
        if (!_charts.TryGetValue(chartIdent, out TextTable? chart))
        {
            chart = _datFiles.Get<TextTable>(chartIdent);
            _charts[chartIdent] = chart;
        }
        return chart is not null
            && chart.Entries.TryGetValue(stringIdent, out var listing)
            && listing.Strings.Count is not 0
                ? [.. listing.Strings.Select(val => CanonStringEscapes.Unescape(val))]
                : null;
    }

    public static readonly uint AvatarVariable = CalculateDigest("PLAYER");

    public string? LocateBlueprint(
        uint chartIdent,
        string tag,
        IReadOnlyDictionary<uint, string> variables)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(variables);
        if (chartIdent is 0u)
            return null;

        if (!_charts.TryGetValue(chartIdent, out TextTable? chart))
        {
            chart = _datFiles.Get<TextTable>(chartIdent);
            _charts[chartIdent] = chart;
        }

        if (chart is null
            || !chart.Entries.TryGetValue(CalculateDigest(tag), out var listing)
            || listing.Strings.Count is 0)
            return null;

        var composed = new System.Text.StringBuilder();
        for (int idx = 0; idx < listing.Strings.Count; ++idx)
        {
            composed.Append(listing.Strings[idx]);
            if (idx < listing.Variables.Count
                && variables.TryGetValue(listing.Variables[idx], out string? val))

                composed.Append(CanonStringEscapes.Escape(val));
        }
        return CanonStringEscapes.Unescape(composed.ToString());
    }

    public static uint CalculateDigest(string val)
    {
        ArgumentNullException.ThrowIfNull(val);

        uint outcome = 0u;
        foreach (char c in val)
        {
            outcome = unchecked((outcome << 4) + (byte)c);
            uint hi = outcome & 0xF0000000u;
            if (hi is not 0u)
                outcome = ((hi >> 24) ^ outcome) & 0x0FFFFFFFu;
        }

        return outcome == uint.MaxValue ? uint.MaxValue - 1u : outcome;
    }
}

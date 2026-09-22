using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public static partial class ElemScanner
{
    public static MooringRims ToMoorings(uint left, uint top, uint right, uint bottom)
    {
        var edges = MooringRims.None;
        if (left is 1 or 4) edges |= MooringRims.Left;
        if (right is 1 || right is 4 || left is 2) edges |= MooringRims.Right;
        if (top is 1 or 4) edges |= MooringRims.Top;
        if (bottom is 1 || bottom is 4 || top is 2) edges |= MooringRims.Bottom;
        if (edges == MooringRims.None) edges = MooringRims.Left | MooringRims.Top; // default: pin top-left
        return edges;
    }

    public static ElemDetails Merge(ElemDetails base_, ElemDetails derived)
    {
        ElemDetails info = new ElemDetails
        {
            Id = derived.Id is not 0 ? derived.Id : base_.Id,
            Type = derived.Type is not 0 ? derived.Type : base_.Type,
            X = derived.X,
            Y = derived.Y,
            // NOTE: 0 is the "not set, inherit from base" sentinel for Width/Height.
            Width = derived.Width != 0 ? derived.Width : base_.Width,
            Height = derived.Height != 0 ? derived.Height : base_.Height,
            Left = derived.Left,
            Top = derived.Top,
            Right = derived.Right,
            Bottom = derived.Bottom,
            ReadOrder = derived.ReadOrder,
            ZLevel = derived.ZLevel is not 0 ? derived.ZLevel : base_.ZLevel,
            DefaultStateId = derived.DefaultStateId is not 0 ? derived.DefaultStateId : base_.DefaultStateId,
            FontDid = derived.FontDid is not 0 ? derived.FontDid : base_.FontDid,
            HJustify = HasNetEnum(derived, 0x14u) ? derived.HJustify : base_.HJustify,
            VJustify = HasNetEnum(derived, 0x15u) ? derived.VJustify : base_.VJustify,
            FontColor = derived.FontColor ?? base_.FontColor,
            TagFontColor = derived.TagFontColor ?? base_.TagFontColor,
            Outline = derived.Outline || base_.Outline,
            OutlineColor = derived.OutlineColor ?? base_.OutlineColor,
            MarginLeft = derived.MarginLeft is not 0 ? derived.MarginLeft : base_.MarginLeft,
            MarginRight = derived.MarginRight is not 0 ? derived.MarginRight : base_.MarginRight,
            MarginTop = derived.MarginTop is not 0 ? derived.MarginTop : base_.MarginTop,
            MarginBottom = derived.MarginBottom is not 0 ? derived.MarginBottom : base_.MarginBottom,
            DefaultStateName = !string.IsNullOrEmpty(derived.DefaultStateName) ? derived.DefaultStateName : base_.DefaultStateName,
            Children = [.. derived.Children],
            StateMedia = new Dictionary<string, (uint, int)>(base_.StateMedia)
        };
        foreach (var kv in derived.StateMedia)
            info.StateMedia[kv.Key] = kv.Value;
        info.StateCursors = new Dictionary<string, WidgetCursorMedia>(base_.StateCursors);
        foreach (var kv in derived.StateCursors)
            info.StateCursors[kv.Key] = kv.Value;

        info.States = [];
        foreach (var (ident, phase) in base_.States)
            info.States[ident] = phase.Clone();
        foreach (var (ident, phase) in derived.States)
        {
            info.States[ident] = info.States.TryGetValue(ident, out var basePhase)
                ? WidgetStateInfo.Merge(basePhase, phase)
                : phase.Clone();
        }

        ImposeCanonLegacyProj(info);
        return info;
    }

    internal static ClientHJustify ChartHorizontalJustification(ulong raw)
    {
        return raw switch
        {
            1UL => ClientHJustify.Center,
            3UL or 5UL => ClientHJustify.Right,
            _ => ClientHJustify.Left,
        };
    }

    internal static ClientVJustify ChartVerticalJustification(ulong raw)
    {
        return raw switch
        {
            1UL => ClientVJustify.Center,
            3UL or 5UL => ClientVJustify.Bottom,
            _ => ClientVJustify.Top,
        };
    }

    internal static Vector4[] ScanNetTintSwatch(
        ElemDetails details,
        uint propIdent)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!details.TryFetchNetProp(propIdent, out WidgetPropertyValue val))
            return [];

        IEnumerable<WidgetPropertyValue> listings = val.Kind switch
        {
            WidgetPropertyKind.Color => [val],
            WidgetPropertyKind.Array => val.ArrayValue,
            _ => [],
        };

        return [.. listings
            .Where(listing => listing.Kind == WidgetPropertyKind.Color)
            .Select(listing =>
            {
                var tint = listing.ColorValue;
                float alpha = tint.Alpha is 0 ? 1f : tint.Alpha / 255f;
                return new Vector4(
                    tint.Red / 255f,
                    tint.Green / 255f,
                    tint.Blue / 255f,
                    alpha);
            })];
    }

    internal static IReadOnlyDictionary<uint, Vector4>? AssemblePerPhaseTintLookup(
        ElemDetails details, uint propIdent)
    {
        Dictionary<uint, Vector4>? lookup = null;
        foreach (uint phaseIdent in details.States.Keys)
        {
            if (!details.TryFetchNetProp(propIdent, out WidgetPropertyValue val, phaseIdent))
                continue;

            WidgetPropertyValue? tintVal = val.Kind == WidgetPropertyKind.Color
                ? val
                : val.Kind == WidgetPropertyKind.Array
                    && val.ArrayValue.Count > 0
                    && val.ArrayValue[0].Kind == WidgetPropertyKind.Color
                        ? val.ArrayValue[0]
                        : null;
            if (tintVal is null)
                continue;

            var c = tintVal.ColorValue;
            float alpha = c.Alpha is 0 ? 1f : c.Alpha / 255f;
            (lookup ??= [])[phaseIdent] =
                new Vector4(c.Red / 255f, c.Green / 255f, c.Blue / 255f, alpha);
        }

        return lookup is { Count: > 1 } && lookup.Values.Distinct().Count() > 1 ? lookup : null;
    }

    internal static IReadOnlyDictionary<uint, bool>? AssemblePerPhaseBoolLookup(
        ElemDetails details, uint propIdent)
    {
        Dictionary<uint, bool>? lookup = null;
        foreach (uint phaseIdent in details.States.Keys)
        {
            if (!details.TryFetchNetProp(propIdent, out WidgetPropertyValue val, phaseIdent)
                || val.Kind != WidgetPropertyKind.Bool)
                continue;
            (lookup ??= [])[phaseIdent] = val.BoolValue;
        }

        return lookup is { Count: > 1 } && lookup.Values.Distinct().Count() > 1 ? lookup : null;
    }

    private static bool HasNetEnum(ElemDetails details, uint propIdent)
    {
        return details.TryFetchNetProp(propIdent, out WidgetPropertyValue val)
        && val.Kind == WidgetPropertyKind.Enum;
    }

    private static List<WidgetTabChartEntry> ScanTabChart(ElemDetails details)
    {
        var listings = new List<WidgetTabChartEntry>();
        if (!details.TryFetchNetProp(0x2Eu, out var prop)
            || prop.Kind != WidgetPropertyKind.Array)
            return listings;

        foreach (WidgetPropertyValue gear in prop.ArrayValue)
        {
            if (gear.Kind != WidgetPropertyKind.Struct) continue;
            uint btnIdent = ScanStructParticipantIdent(gear.StructValue, 0x30u);
            uint sheetIdent = ScanStructParticipantIdent(gear.StructValue, 0x31u);
            if (btnIdent is 0u || sheetIdent is 0u) continue;
            bool isDefault = gear.StructValue.TryGetValue(0x32u, out var bit)
                && bit.Kind == WidgetPropertyKind.Bool
                && bit.BoolValue;
            listings.Add(new WidgetTabChartEntry(btnIdent, sheetIdent, isDefault));
        }

        return listings;
    }

    private static List<WidgetTemplateListEntry> ScanBlueprintRoster(ElemDetails details)
    {
        var listings = new List<WidgetTemplateListEntry>();
        if (!details.TryFetchNetProp(0x64u, out var prop)
            || prop.Kind != WidgetPropertyKind.Array)
            return listings;

        foreach (WidgetPropertyValue gear in prop.ArrayValue)
        {
            if (gear.Kind != WidgetPropertyKind.Struct) continue;
            uint arrangementDid = ScanStructParticipantIdent(gear.StructValue, 0x63u);
            uint elemIdent = ScanStructParticipantIdent(gear.StructValue, 0x62u);
            listings.Add(new WidgetTemplateListEntry(arrangementDid, elemIdent));
        }

        return listings;
    }

    private static uint ScanStructParticipantIdent(IReadOnlyDictionary<uint, WidgetPropertyValue> participants, uint tag)
    {
        return !participants.TryGetValue(tag, out var val)
            ? 0u
            : val.Kind switch
            {
                WidgetPropertyKind.Enum or WidgetPropertyKind.DataId => (uint)val.UnsignedValue,
                WidgetPropertyKind.Integer when val.IntegerValue >= 0 => (uint)val.IntegerValue,
                _ => 0u,
            };
    }

    private static uint ScanReferencedElemIdent(ElemDetails details, uint propIdent)
    {
        return !details.TryFetchNetProp(propIdent, out var prop)
            ? 0u
            : prop.Kind switch
            {
                WidgetPropertyKind.Enum or WidgetPropertyKind.DataId => (uint)prop.UnsignedValue,
                WidgetPropertyKind.Integer when prop.IntegerValue >= 0 => (uint)prop.IntegerValue,
                _ => 0u,
            };
    }
}

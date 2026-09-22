using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;

namespace MacAC.Client.Shell;

public static partial class MarkupDoc
{
    public static WidgetNineSlicePane Build(
        string xml, object mapping, Func<uint, (uint, int, int)> locate,
        ClientControlsIni? styling = null, WidgetDatFont? datTypeface = null,
        IMarkupIconPicker? glyphs = null)
    {
        XElement trunk = XDocument.Parse(xml).Root ?? throw new FormatException("empty markup");
        if (trunk.Name.LocalName != "panel")
            throw new FormatException($"root has to be <panel>, got <{trunk.Name.LocalName}>");

        WidgetNineSlicePane board = new WidgetNineSlicePane(locate)
        {
            Left = F(trunk, "x"),
            Top = F(trunk, "y"),
            Width = F(trunk, "w"),
            Height = F(trunk, "h"),
        };

        bool resizable = B(trunk, "resizable", false);
        board.Resizable = resizable;
        board.MinWidth = FOr(trunk, "minw", board.Width);
        board.MinHeight = FOr(trunk, "minh", board.Height);
        board.ResizeX = resizable;
        board.ResizeY = resizable;

        string? rescale = (string?)trunk.Attribute("resize");
        if (rescale is not null)
        {
            board.ResizeX = rescale is "x" or "both";
            board.ResizeY = rescale is "y" or "both";
        }

        string? shown = (string?)trunk.Attribute("visible");
        if (shown is not null && IsMapping(shown))
        {
            var bit = mapping.GetType().GetProperty(shown[1..^1]);
            if (bit is null || bit.PropertyType != typeof(bool))
            {
                throw new FormatException(
                    $"<panel visible=\"{shown}\"> didn't resolve to a bool property "
                    + $"on {mapping.GetType().Name}");
            }
            board.ShownSrc = () => bit.GetValue(mapping) is true;
        }

        string? banner = (string?)trunk.Attribute("title");
        if (!string.IsNullOrEmpty(banner))
        {
            Vector4 tc = styling is not null && styling.TryTint("title", "color", out var c) ? c : Vector4.One;
            board.AddChild(new WidgetCaption
            {
                Text = banner,
                Left = 8,
                Top = 4,
                PhraseColor = tc,
                DatFont = datTypeface,
            });
        }

        foreach (var elem in trunk.Elements())
            AppendElem(board, elem, mapping, locate, datTypeface, glyphs);
        return board;
    }

    private static WidgetMarkupListColumn AssembleRosterColumn(
        XElement columnElem, object mapping, IMarkupIconPicker? glyphs, int ordinal, bool isPrevious)
    {
        string? kind = (string?)columnElem.Attribute("type");
        (float width, bool isAutoWidth) = DecodeColumnWidth(columnElem, ordinal, kind, isPrevious);
        switch (kind)
        {
            case "text":
                {
                    var phraseSrc = AttachStringRoster(
                        (string?)columnElem.Attribute("items"), mapping, ColumnCtx(ordinal, "text", "items"));
                    string? tintsAttr = (string?)columnElem.Attribute("colors");
                    Func<IReadOnlyList<uint>>? tintsSrc = tintsAttr is null
                        ? null
                        : AttachUintRoster(tintsAttr, mapping, ColumnCtx(ordinal, "text", "colors"));
                    string? phraseOnPressAttr = (string?)columnElem.Attribute("onclick");
                    var phraseOnPress = AttachIntAct(phraseOnPressAttr, mapping);
                    return phraseOnPressAttr is not null && phraseOnPress is null
                    ? throw new FormatException(
                        $"{ColumnCtx(ordinal, "text", "onclick")} didn't resolve to an "
                        + $"Action<int> property on {mapping.GetType().Name}")
                    : WidgetMarkupListColumn.Text(width, phraseSrc, tintsSrc, phraseOnPress, isAutoWidth);
                }
            case "check":
                {
                    var verifySrc = AttachBoolRoster(
                        (string?)columnElem.Attribute("values"), mapping, ColumnCtx(ordinal, "check", "values"));
                    Action<int> onEdit = AttachNeededIntAct(
                        (string?)columnElem.Attribute("onchange"), mapping, ColumnCtx(ordinal, "check", "onchange"));
                    return WidgetMarkupListColumn.Check(width, verifySrc, onEdit, isAutoWidth);
                }
            case "icon":
                {
                    var valsSrc = AttachNeededUintRoster(
                        (string?)columnElem.Attribute("values"), mapping, ColumnCtx(ordinal, "icon", "values"));
                    string? glyphSort = (string?)columnElem.Attribute("iconkind");
                    ValidateIconKind(glyphSort, ColumnCtx(ordinal, "icon", "iconkind"));
                    Action<int> onPress = AttachNeededIntAct(
                        (string?)columnElem.Attribute("onclick"), mapping, ColumnCtx(ordinal, "icon", "onclick"));
                    Func<uint, (uint, int, int)>? locate = glyphs is not null
                    ? AssembleRankGlyphLocate(glyphSort, glyphs)
                    : null;
                    return WidgetMarkupListColumn.Icon(width, valsSrc, locate, onPress, isAutoWidth);
                }
            default:
                throw new FormatException(
                    $"column[{ordinal}] has unrecognized type=\"{kind}\" (wanted text, check, or icon)");
        }
    }

    private static string ValidateIconKind(string? glyphSort, string ctx = "iconkind")
    {
        return (glyphSort ?? "did") switch
        {
            "did" or "spell" or "item" => glyphSort ?? "did",
            var another => throw new FormatException(
                $"{ctx} has to be did, spell, or item (got \"{another}\")"),
        };
    }

    private static bool VetArtStyling(string elemLabel, string? styling)
    {
        return styling switch
        {
            null or "plain" => false,
            "retail" => true,
            var another => throw new FormatException(
                $"<{elemLabel} style=\"{another}\"> has to be plain or retail"),
        };
    }

    private static uint ToUintOrZero(object val)
    {
        try
        {
            return Convert.ToUInt32(val, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return 0u;
        }
    }

    private static string ColumnCtx(int ordinal, string kind, string attr) =>
        $"column[{ordinal}] type=\"{kind}\" {attr}";

    private static (float width, bool isAutoWidth) DecodeColumnWidth(
        XElement columnElem, int ordinal, string? kind, bool isPrevious)
    {
        string? raw = (string?)columnElem.Attribute("width");
        if (raw == "*")
            return (0f, true);
        if (isPrevious)
            return (F(columnElem, "width"), false);
        return !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
            || width <= 0f
            ? throw new FormatException(
                $"{ColumnCtx(ordinal, kind ?? "(missing)", "width")} has to be a positive "
                + "number or \"*\", got " + (raw is null ? "(missing)" : $"\"{raw}\""))
            : ((float width, bool isAutoWidth))(width, false);
    }

    private static float F(XElement element, string attr)
    {
        return float.TryParse((string?)element.Attribute(attr), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    private static float FOr(XElement element, string attr, float backup)
    {
        return float.TryParse((string?)element.Attribute(attr), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float val) ? val : backup;
    }

    private static int I(XElement element, string attr, int backup)
    {
        return int.TryParse((string?)element.Attribute(attr), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int val) ? val : backup;
    }

    private static bool B(XElement element, string attr, bool backup)
    {
        return bool.TryParse((string?)element.Attribute(attr), out bool val)
                ? val
                : backup;
    }

    private static Vector4 Color(string? hex)
    {
        return hex is { Length: 9 } && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber,
                             CultureInfo.InvariantCulture, out uint argb)
            ? new Vector4(
                ((argb >> 16) & 0xFF) / 255f,
                ((argb >> 8) & 0xFF) / 255f,
                (argb & 0xFF) / 255f,
                ((argb >> 24) & 0xFF) / 255f)
            : Vector4.One;
    }

    private static PropertyInfo? Prop(string? expr, object mapping)
    {
        return expr is null || expr.Length < 3 || expr[0] != '{' || expr[^1] != '}' ? null : mapping.GetType().GetProperty(expr[1..^1]);
    }

    private static uint Hex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.Trim();
        if (t.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return uint.TryParse(t, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0u;
    }

    private static string ElemPersona(XElement src)
    {
        string? label = (string?)src.Attribute("name") ?? (string?)src.Attribute("id");
        return label is null
            ? $"<{src.Name.LocalName}>"
            : $"<{src.Name.LocalName} name=\"{label}\">";
    }

    private static uint DecodeUintLiteral(string phrase, string ctx)
    {
        string trimmed = phrase.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint hex))
                return hex;
        }
        else if (uint.TryParse(trimmed, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out uint dec))
        {
            return dec;
        }
        throw new FormatException($"{ctx}=\"{phrase}\" isn't a valid uint literal");
    }

    private static MooringRims DecodeMooring(string? tickets, XElement src)
    {
        if (string.IsNullOrWhiteSpace(tickets))
            return MooringRims.Left | MooringRims.Top;

        var rims = MooringRims.None;
        foreach (string ticket in tickets.Split(
            (char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            rims |= ticket.ToLowerInvariant() switch
            {
                "left" => MooringRims.Left,
                "top" => MooringRims.Top,
                "right" => MooringRims.Right,
                "bottom" => MooringRims.Bottom,
                _ => throw new FormatException(
                    $"{ElemPersona(src)} anchor=\"{tickets}\" has unrecognized token "
                    + $"\"{ticket}\" (wanted left, top, right, bottom)"),
            };
        }
        return rims;
    }

    private static bool IsMapping(string val) =>
        val.Length > 2 && val[0] == '{' && val[^1] == '}';

    private static void ImposeCommon(
        WidgetElem elem,
        XElement src,
        object mapping)
    {
        elem.Name = (string?)src.Attribute("name")
            ?? (string?)src.Attribute("id");

        elem.Moorings = DecodeMooring((string?)src.Attribute("anchor"), src);

        AttachBool((string?)src.Attribute("visible"), mapping,
            val => elem.Visible = val,
            srcReader => elem.ShownSrc = srcReader);
        AttachBool((string?)src.Attribute("enabled"), mapping,
            val => elem.Enabled = val,
            srcReader => elem.TurnedOnSrc = srcReader);

        string? hint = (string?)src.Attribute("tooltip");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            elem.CoreHintPhraseSrc = AttachString(hint, mapping);
            elem.AuthoredHintTrunkElemIdent = CoreHintTrunkElemIdent;
            elem.AuthoredHintArrangementDid = CoreHintArrangementDid;
            elem.AuthoredHintTurnedOn = true;
        }
    }
}

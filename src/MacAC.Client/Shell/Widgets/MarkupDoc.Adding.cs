using System.Numerics;
using System.Xml.Linq;

namespace MacAC.Client.Shell;

public static partial class MarkupDoc
{
    private static void AppendElem(
        WidgetElem ancestor,
        XElement elem,
        object mapping,
        Func<uint, (uint, int, int)> locate,
        WidgetDatFont? datTypeface,
        IMarkupIconPicker? glyphs)
    {
        switch (elem.Name.LocalName)
        {
            case "group":
                WidgetBoard cluster = new WidgetBoard
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    BackgroundColor = elem.Attribute("background") is null
                        ? Vector4.Zero
                        : Color((string?)elem.Attribute("background")),
                    BorderTint = elem.Attribute("border") is null
                        ? Vector4.Zero
                        : Color((string?)elem.Attribute("border")),
                    BorderThickness = elem.Attribute("border") is null ? 0f : 1f,
                    ClickThrough = true,
                };
                ImposeCommon(cluster, elem, mapping);
                ancestor.AddChild(cluster);
                foreach (XElement descendant in elem.Elements())
                    AppendElem(cluster, descendant, mapping, locate, datTypeface, glyphs);
                break;

            case "meter":
                Func<uint?> cur = AttachUint((string?)elem.Attribute("cur"), mapping);
                Func<uint?> upper = AttachUint((string?)elem.Attribute("max"), mapping);
                WidgetGauge gauge = new WidgetGauge
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    BarTint = Color((string?)elem.Attribute("color")),
                    Populate = AttachFloat((string?)elem.Attribute("fill"), mapping),
                    Label = () => (cur(), upper()) is (uint c, uint m) ? $"{c}/{m}" : null,
                    SpriteResolve = locate,
                    BackLeft = Hex((string?)elem.Attribute("backleft")),
                    BackTile = Hex((string?)elem.Attribute("backtile")),
                    BackRight = Hex((string?)elem.Attribute("backright")),
                    FrontLeft = Hex((string?)elem.Attribute("frontleft")),
                    FrontTile = Hex((string?)elem.Attribute("fronttile")),
                    FrontRight = Hex((string?)elem.Attribute("frontright")),
                };
                ImposeCommon(gauge, elem, mapping);
                ancestor.AddChild(gauge);
                break;

            case "label":
                // Text may be a literal or a {Binding}.
                WidgetCaption label = new WidgetCaption
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    PhraseSrc = AttachString((string?)elem.Attribute("text"), mapping),
                    DatFont = datTypeface,
                };
                if (elem.Attribute("color") is not null)
                    label.PhraseColor = Color((string?)elem.Attribute("color"));
                ImposeCommon(label, elem, mapping);
                ancestor.AddChild(label);
                break;

            case "button":
                string? pressLabel = (string?)elem.Attribute("onclick");
                Action? onPress = AttachAct(pressLabel, mapping);
                if (pressLabel is not null && onPress is null)
                {
                    throw new FormatException(
                        $"<button onclick=\"{pressLabel}\"> didn't resolve to an "
                        + $"Action property on {mapping.GetType().Name}");
                }
                WidgetSimpleButton btn = new WidgetSimpleButton
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    Text = (string?)elem.Attribute("text") ?? string.Empty,
                    DatFont = datTypeface,
                };
                string? legend = (string?)elem.Attribute("text");
                if (legend is not null && IsMapping(legend))
                    btn.WordingSrc = AttachString(legend, mapping);
                if (elem.Attribute("color") is not null)
                    btn.TextTint = Color((string?)elem.Attribute("color"));
                if (elem.Attribute("background") is not null)
                    btn.BackgroundColor = Color(
                        (string?)elem.Attribute("background"));
                if (elem.Attribute("border") is not null)
                    btn.BorderTint = Color(
                        (string?)elem.Attribute("border"));
                string? btnGlyph = (string?)elem.Attribute("icon");
                if (btnGlyph is not null)
                {
                    string? btnGlyphSort = (string?)elem.Attribute("iconkind");
                    ValidateIconKind(btnGlyphSort);
                    Func<uint> btnGlyphReader =
                        AttachUintLiteralOrMapping(btnGlyph, mapping, "button icon");
                    if (glyphs is not null)
                    {
                        btn.GlyphSrc = AssembleGlyphSrc(
                            btnGlyphSort,
                            btnGlyphReader,
                            glyphs);
                    }
                }
                ImposeCommon(btn, elem, mapping);
                if (onPress is not null)
                    btn.Click += onPress;
                ancestor.AddChild(btn);
                break;

            case "icon":
                {
                    if (elem.Attribute("iconkind") is not null)
                    {
                        throw new FormatException(
                            "iconkind applies to button and list; icon derives its kind from did/spell/item");
                    }

                    string? didAttr = (string?)elem.Attribute("did");
                    string? arcanumAttr = (string?)elem.Attribute("spell");
                    string? gearAttr = (string?)elem.Attribute("item");
                    int srcTally = (didAttr is not null ? 1 : 0)
                        + (arcanumAttr is not null ? 1 : 0)
                        + (gearAttr is not null ? 1 : 0);
                    if (srcTally is not 1)
                    {
                        throw new FormatException(
                            "<icon> needs precisely one of did/spell/item");
                    }

                    string glyphSort = didAttr is not null ? "did"
                        : arcanumAttr is not null ? "spell"
                        : "item";
                    string glyphExpression = didAttr ?? arcanumAttr ?? gearAttr!;
                    Func<uint> glyphReader = AttachUintLiteralOrMapping(
                        glyphExpression, mapping, $"icon {glyphSort}");
                    WidgetMarkupIcon glyph = new WidgetMarkupIcon
                    {
                        Left = F(elem, "x"),
                        Top = F(elem, "y"),
                        Width = FOr(elem, "w", 32f),
                        Height = FOr(elem, "h", 32f),
                        GlyphSource = AssembleGlyphSrc(glyphSort, glyphReader, glyphs),
                    };
                    ImposeCommon(glyph, elem, mapping);
                    string? glyphHint = (string?)elem.Attribute("tooltip");
                    if (!string.IsNullOrWhiteSpace(glyphHint))
                        glyph.ClickThrough = false;
                    ancestor.AddChild(glyph);
                    break;
                }

            case "tab":
                string? tabPressLabel = (string?)elem.Attribute("onclick");
                Action? tabPress = AttachAct(tabPressLabel, mapping);
                if (tabPressLabel is not null && tabPress is null)
                {
                    throw new FormatException(
                        $"<tab onclick=\"{tabPressLabel}\"> didn't resolve to an "
                        + $"Action property on {mapping.GetType().Name}");
                }

                WidgetMarkupTabButton tab = new WidgetMarkupTabButton
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    Text = (string?)elem.Attribute("text") ?? string.Empty,
                    DatFont = datTypeface,
                    ChosenSrc = AttachNeededBoolReader(
                        (string?)elem.Attribute("selected"),
                        mapping,
                        "tab selected"),
                };
                ImposeCommon(tab, elem, mapping);
                if (tabPress is not null)
                    tab.Click += tabPress;
                ancestor.AddChild(tab);
                break;

            case "toggle":
                string? flipPressLabel = (string?)elem.Attribute("onclick");
                Action? flipPress = AttachAct(flipPressLabel, mapping);
                if (flipPressLabel is not null && flipPress is null)
                {
                    throw new FormatException(
                        $"<toggle onclick=\"{flipPressLabel}\"> didn't resolve to an "
                        + $"Action property on {mapping.GetType().Name}");
                }

                string? flipLegend = (string?)elem.Attribute("text");
                WidgetMarkupToggle flip = new WidgetMarkupToggle
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    Text = flipLegend ?? string.Empty,
                    TextSrc = AttachString(flipLegend, mapping),
                    CheckedSrc = AttachNeededBoolReader(
                        (string?)elem.Attribute("checked"),
                        mapping,
                        "toggle checked"),
                    DatFont = datTypeface,
                    Toggle = flipPress,
                };
                if (elem.Attribute("color") is not null)
                    flip.TextColor = Color((string?)elem.Attribute("color"));
                ImposeCommon(flip, elem, mapping);
                ancestor.AddChild(flip);
                break;

            case "slider":
                string? editLabel = (string?)elem.Attribute("onchange");
                var altered = AttachFloatAct(editLabel, mapping);
                if (editLabel is not null && altered is null)
                {
                    throw new FormatException(
                        $"<slider onchange=\"{editLabel}\"> didn't resolve to an "
                        + $"Action<float> property on {mapping.GetType().Name}");
                }

                // KB 08 §3 gap: VVS's HudHSlider exposes an arbitrary Min/Max range (VTank's own Vitals sliders
                // are minimum="0" maximum="100"); macac's <slider> historically only ever bound a fixed 0.0-1.0
                // value.
                float dialLower = FOr(elem, "min", 0f);
                float dialUpper = FOr(elem, "max", 1f);
                if (dialUpper <= dialLower)
                {
                    throw new FormatException(
                        $"<slider min=\"{dialLower}\" max=\"{dialUpper}\"> must have max > min");
                }
                float dialSpan = dialUpper - dialLower;

                var dialValSrc = AttachFloat(
                    (string?)elem.Attribute("value"),
                    mapping);

                bool dialCanonArt = VetArtStyling("slider", (string?)elem.Attribute("style"));

                WidgetScroller dial = new WidgetScroller
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    Horizontal = true,
                    SpriteResolve = locate,
                    CanonArt = dialCanonArt,
                    ScalarLocusSrc = () =>
                        dialValSrc() is { } declaredVal
                            ? Math.Clamp(
                                (declaredVal - dialLower) / dialSpan, 0f, 1f)
                            : (float?)null,
                    ScalarAltered = altered is null
                        ? null
                        : normalized => altered(dialLower + normalized * dialSpan),
                };
                if (dialCanonArt)
                    CanonScrollbarChrome.ImposeHorizontal(dial);
                ImposeCommon(dial, elem, mapping);
                ancestor.AddChild(dial);
                break;

            case "field":
                string? fieldEditLabel = (string?)elem.Attribute("onchange");
                var fieldAltered = AttachStringAct(
                    fieldEditLabel,
                    mapping);
                if (fieldEditLabel is not null && fieldAltered is null)
                {
                    throw new FormatException(
                        $"<field onchange=\"{fieldEditLabel}\"> didn't resolve to an "
                        + $"Action<string> property on {mapping.GetType().Name}");
                }
                string? submitLabel = (string?)elem.Attribute("onsubmit");
                var submitted = AttachStringAct(submitLabel, mapping);
                if (submitLabel is not null && submitted is null)
                {
                    throw new FormatException(
                        $"<field onsubmit=\"{submitLabel}\"> didn't resolve to an "
                        + $"Action<string> property on {mapping.GetType().Name}");
                }

                WidgetField field = new WidgetField
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    DatTypeface = datTypeface,
                    BackgroundTint = elem.Attribute("background") is null
                        ? new Vector4(0f, 0f, 0f, 0.9f)
                        : Color((string?)elem.Attribute("background")),
                    PhraseTint = elem.Attribute("color") is null
                        ? new Vector4(0.91f, 0.87f, 0.76f, 1f)
                        : Color((string?)elem.Attribute("color")),
                    UpperToons = Math.Max(1, I(elem, "maxlength", 128)),
                    WipeOnSubmit = B(elem, "clearonsubmit", false),
                    CaptureHistory = false,
                    OnPhraseAltered = fieldAltered,
                    OnSubmit = submitted,
                };
                field.AssignPhrase(AttachString((string?)elem.Attribute("text"), mapping)());
                ImposeCommon(field, elem, mapping);
                ancestor.AddChild(field);
                break;

            case "menu":
                string? menuEditLabel = (string?)elem.Attribute("onchange");
                var menuAltered = AttachStringAct(
                    menuEditLabel,
                    mapping);
                if (menuEditLabel is not null && menuAltered is null)
                {
                    throw new FormatException(
                        $"<menu onchange=\"{menuEditLabel}\"> didn't resolve to an "
                        + $"Action<string> property on {mapping.GetType().Name}");
                }
                var menuGearList = AttachStringRoster(
                    (string?)elem.Attribute("items"),
                    mapping,
                    "menu items");
                var menuChosen = AttachString(
                    (string?)elem.Attribute("selected"),
                    mapping);
                bool menuCanonBtnArt = VetArtStyling("menu", (string?)elem.Attribute("style"));
                WidgetMenu menu = new WidgetMenu
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    DatFont = datTypeface,
                    SpriteResolve = locate,
                    RowsPerColumn = Math.Max(1, I(elem, "rows", 7)),
                    RowHeight = Math.Max(12f, FOr(elem, "rowheight", 18f)),
                    ColumnWidth = Math.Max(20f, F(elem, "w")),
                    OpenUpward = B(elem, "openupward", false),
                    RollFollowSprite = 0x06004C5Fu,
                    RollThumbSprite = 0x06004C63u,
                    RollUpSprite = CanonScrollbarChrome.UpNorm,
                    RollDownSprite = CanonScrollbarChrome.DownNorm,
                    PhraseIndent = 6f,
                    BtnPhraseIndent = 6f,
                    NormSprite = 0x06004D65u,
                    PressedSprite = 0x06004D66u,
                    PopupBgSprite = 0x0600124Cu,
                    GearNormSprite = 0x0600124Eu,
                    GearHighlightSprite = 0x0600124Du,
                    CanonBtnArt = menuCanonBtnArt,
                    Scrollable = true,
                    PopupScrollerConcealWhenDisabled = true,
                    BtnCaptionSupplier = () => menuChosen() ?? string.Empty,
                    OnSelect = cargo =>
                    {
                        if (cargo is string val)
                            menuAltered?.Invoke(val);
                    },
                };
                CanonScrollbarChrome.ImposeToMenuPopup(menu);
                void RenewMenu()
                {
                    menu.Items = menuGearList()
                        .Select(static value => new WidgetMenu.MenuGear(value, value))
                        .ToArray();
                    menu.Selected = menuChosen();
                }
                RenewMenu();
                menu.PriorOpen = RenewMenu;
                ImposeCommon(menu, elem, mapping);
                ancestor.AddChild(menu);
                break;

            case "list":
                string? rosterEditLabel = (string?)elem.Attribute("onchange");
                var rosterAltered = AttachIntAct(rosterEditLabel, mapping);
                if (rosterEditLabel is not null && rosterAltered is null)
                {
                    throw new FormatException(
                        $"<list onchange=\"{rosterEditLabel}\"> didn't resolve to an "
                        + $"Action<int> property on {mapping.GetType().Name}");
                }

                List<XElement> rosterDescendants = elem.Elements().ToList();
                foreach (var descendant in rosterDescendants)
                {
                    if (descendant.Name.LocalName != "column")
                    {
                        throw new FormatException(
                            $"<list> children must all be <column>, got <{descendant.Name.LocalName}>");
                    }
                }
                bool rosterUsesColumns = rosterDescendants.Count > 0;
                if (rosterUsesColumns
                    && (elem.Attribute("items") is not null
                        || elem.Attribute("icons") is not null
                        || elem.Attribute("colors") is not null))
                {
                    throw new FormatException(
                        "<list> with <column> children can't also use the "
                        + "items/icons/colors attributes (the single-column "
                        + "form) - express every row source as a <column> instead");
                }

                WidgetMarkupList roster = new WidgetMarkupList
                {
                    Left = F(elem, "x"),
                    Top = F(elem, "y"),
                    Width = F(elem, "w"),
                    Height = F(elem, "h"),
                    RowHeight = Math.Max(12f, FOr(elem, "rowheight", 18f)),
                    DatFont = datTypeface,
                    SpriteResolve = locate,
                    ChosenOrdinalSrc = AttachNeededIntReader(
                        (string?)elem.Attribute("selected"),
                        mapping,
                        "list selected"),
                    PickAltered = rosterAltered,
                    PickBandTurnedOn = B(elem, "selectionband", false),
                };

                if (rosterUsesColumns)
                {
                    int previousColumnOrdinal = rosterDescendants.Count - 1;
                    roster.Columns = rosterDescendants
                        .Select((columnElem, ordinal) => AssembleRosterColumn(
                            columnElem, mapping, glyphs, ordinal, ordinal == previousColumnOrdinal))
                        .ToList();
                }
                else
                {
                    roster.GearListSrc = AttachStringRoster(
                        (string?)elem.Attribute("items"),
                        mapping,
                        "list items");
                    roster.GearTintsSrc = AttachUintRoster(
                        (string?)elem.Attribute("colors"),
                        mapping,
                        "list colors");
                    string? rosterGlyphs = (string?)elem.Attribute("icons");
                    if (!string.IsNullOrWhiteSpace(rosterGlyphs))
                    {
                        string? rosterGlyphSort = (string?)elem.Attribute("iconkind");
                        ValidateIconKind(rosterGlyphSort);
                        var rosterGlyphIdentsReader =
                            AttachUintRoster(rosterGlyphs, mapping, "list icons");
                        if (glyphs is not null)
                        {
                            roster.GlyphIdentsSrc = rosterGlyphIdentsReader;
                            roster.GlyphLocate = AssembleRankGlyphLocate(rosterGlyphSort, glyphs);
                        }
                    }
                }
                ImposeCommon(roster, elem, mapping);
                ancestor.AddChild(roster);
                break;

            default:
                throw new FormatException($"unrecognized element <{elem.Name.LocalName}>");
        }
    }
}

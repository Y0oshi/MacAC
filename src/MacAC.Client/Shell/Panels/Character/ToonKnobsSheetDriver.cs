using System.Numerics;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public static class ToonKnobsSheetDriver
{
    public const uint RootElementId = 0x100001F9u;

    /// <summary>The row ListBox (dat Type 5) - <c>m_pOptionBox</c>.</summary>
    public const uint RosterBboxElemIdent = 0x100001FAu;

    public const uint ScrollbarElementId = 0x100001FBu;

    // Template-list index of the header row (Type 12 text)
    private const int PreambleBlueprintOrdinal = 0;

    // Template-list index of the separator row (Type 3 image)
    private const int SeparatorBlueprintOrdinal = 1;

    // Template-list index of the toggle-option row
    private const int FlipBlueprintOrdinal = 2;

    private const uint StringChartIdent = 0x23000003u;

    public readonly record struct RankSpec(CharacterOptionId Id, string RetailName, bool StoreOnly);

    /// <summary>One authored header group: its section string key plus authored-order rows.</summary>
    public readonly record struct ClusterSpec(string HeaderKey, RankSpec[] Rows);

    private const bool Live = false;
    private const bool VaultSole = true;

    public static readonly ClusterSpec[] Groups =
    [
        new("ID_CharacterOption_UIBehavior_Section",
        [
            new(CharacterOptionId.ViewCombatTarget, "ViewCombatTarget", Live), // Group C
            new(CharacterOptionId.SalvageMultiple, "SalvageMultiple", VaultSole), // Group D
            new(CharacterOptionId.MainPackPreferred, "MainPackPreferred", VaultSole), // Group B, unbound
        ]),
        new("ID_CharacterOption_UIDisplay_Section",
        [
            new(CharacterOptionId.VividTargetingIndicator, "VividTargetingIndicator", Live), // Group C
            new(CharacterOptionId.ShowTooltips, "ShowTooltips", VaultSole), // Group B, unbound
            new(CharacterOptionId.CoordinatesOnRadar, "CoordinatesOnRadar", Live), // Group C
            new(CharacterOptionId.SideBySideVitals, "SideBySideVitals", Live),
            new(CharacterOptionId.SpellDuration, "SpellDuration", VaultSole), // Group B, unbound
            new(CharacterOptionId.DisableMostWeatherEffects, "DisableMostWeatherEffects", VaultSole), // Group B, unbound
            new(CharacterOptionId.DisableDistanceFog, "DisableDistanceFog", Live), // Group B, bound (GameWindow.cs:657)
            new(CharacterOptionId.PersistentAtDay, "PersistentAtDay", VaultSole), // Group B, unbound
            new(CharacterOptionId.DisableHouseRestrictionEffects, "DisableHouseRestrictionEffects", VaultSole), // Group D
            new(CharacterOptionId.UseCraftSuccessDialog, "UseCraftSuccessDialog", VaultSole), // Group A
            new(CharacterOptionId.ConfirmVolatileRareUse, "ConfirmVolatileRareUse", VaultSole), // Group A
            new(CharacterOptionId.DisplayTimeStamps, "DisplayTimeStamps", Live), // Group B, bound (GameWindow.cs:664)
            new(CharacterOptionId.FilterLanguage, "FilterLanguage", VaultSole), // Group B, unbound
            new(CharacterOptionId.ShowHelm, "ShowHelm", VaultSole), // Group A
            new(CharacterOptionId.ShowCloak, "ShowCloak", VaultSole), // Group A
        ]),
        new("ID_CharacterOption_Grouping_Section",
        [
            new(CharacterOptionId.IgnoreAllegianceRequests, "IgnoreAllegianceRequests", VaultSole),
            new(CharacterOptionId.IgnoreFellowshipRequests, "IgnoreFellowshipRequests", VaultSole),
            new(CharacterOptionId.DisplayAllegianceLogonNotifications, "DisplayAllegianceLogonNotifications", VaultSole),
            new(CharacterOptionId.FellowshipShareXP, "FellowshipShareXP", Live),
            new(CharacterOptionId.FellowshipShareLoot, "FellowshipShareLoot", VaultSole),
            new(CharacterOptionId.FellowshipAutoAcceptRequests, "FellowshipAutoAcceptRequests", VaultSole),
        ]),
        new("ID_CharacterOption_OtherPlayers_Section",
        [
            new(CharacterOptionId.AcceptLootPermits, "AcceptLootPermits", VaultSole), // Group A (see ambiguity note)
            new(CharacterOptionId.UseDeception, "UseDeception", VaultSole), // Group A
            new(CharacterOptionId.AllowGive, "AllowGive", VaultSole), // Group A
            new(CharacterOptionId.IgnoreTradeRequests, "IgnoreTradeRequests", VaultSole), // Group A
            new(CharacterOptionId.DragItemOnPlayerOpensSecureTrade, "DragItemOnPlayerOpensSecureTrade", Live),
            new(CharacterOptionId.DisplayDateOfBirth, "DisplayDateOfBirth", VaultSole), // Group A
            new(CharacterOptionId.DisplayAge, "DisplayAge", VaultSole), // Group A
            new(CharacterOptionId.DisplayChessRank, "DisplayChessRank", VaultSole), // Group A
            new(CharacterOptionId.DisplayFishingSkill, "DisplayFishingSkill", VaultSole), // Group A
            new(CharacterOptionId.DisplayNumberDeaths, "DisplayNumberDeaths", VaultSole), // Group A
            new(CharacterOptionId.DisplayNumberCharacterTitles, "DisplayNumberCharacterTitles", VaultSole), // Group A
        ]),
        new("ID_CharacterOption_CharacterBehavior_Section",
        [
            new(CharacterOptionId.ToggleRun, "ToggleRun", Live), // Group B, bound (GameWindow.cs:680)
            new(CharacterOptionId.AdvancedCombatUI, "AdvancedCombatUI", VaultSole), // Group B, unbound
            new(CharacterOptionId.AutoTarget, "AutoTarget", Live), // Group C
            new(CharacterOptionId.AutoRepeatAttack, "AutoRepeatAttack", Live), // Group C
            new(CharacterOptionId.UseChargeAttack, "UseChargeAttack", VaultSole), // Group A
            new(CharacterOptionId.LeadMissileTargets, "LeadMissileTargets", VaultSole), // Group A
            new(CharacterOptionId.UseFastMissiles, "UseFastMissiles", VaultSole), // Group A
        ]),
        new("ID_CharacterOption_Chat_Section",
        [
            new(CharacterOptionId.StayInChatMode, "StayInChatMode", VaultSole), // Group B, unbound
            new(CharacterOptionId.ListenToAllegianceChat, "HearAllegianceChat", Live), // TurbineCommsMembershipTurnstile.cs:107-110
            new(CharacterOptionId.ListenToGeneralChat, "HearGeneralChat", Live), // TurbineCommsMembershipTurnstile.cs:111-114
            new(CharacterOptionId.ListenToTradeChat, "HearTradeChat", Live), // TurbineCommsMembershipTurnstile.cs:115-118
            new(CharacterOptionId.ListenToLFGChat, "HearLFGChat", Live), // TurbineCommsMembershipTurnstile.cs:119-122
            new(CharacterOptionId.ListenToRoleplayChat, "HearRoleplayChat", Live), // TurbineCommsMembershipTurnstile.cs:123-126
            new(CharacterOptionId.ListenToSocietyChat, "HearSocietyChat", Live), // TurbineCommsMembershipTurnstile.cs:127-130
            new(CharacterOptionId.HearPkDeathMessages, "HearPKDeaths", VaultSole), // Group D
        ]),
    ];

    public static int SumRankTally
    {
        get
        {
            int tally = 0;
            foreach (ClusterSpec cluster in Groups) tally += cluster.Rows.Length;
            return tally;
        }
    }

    public sealed record ClientBindings(
        Func<CharacterOptionId, bool> CurrentValue,
        Action<CharacterOptionId, bool> SetOption);

    public static bool Bind(
        ImportedArrangement arrangement,
        KnobPage sheet,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, uint, string?> locateString,
        ClientBindings mappings)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(locateString);
        ArgumentNullException.ThrowIfNull(mappings);

        if (arrangement.SeekElem(RosterBboxElemIdent) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: ListBox 0x{RosterBboxElemIdent:X8} "
                + "not found (or not a WidgetBlueprintRosterBbox) in the built Options panel tree - "
                + "the Character tab will have no rows");
            return false;
        }

        rosterBbox.TemplateResolver = blueprintLocator;

        if (arrangement.SeekElem(ScrollbarElementId) is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: scrollbar 0x{ScrollbarElementId:X8} "
                + "not found - the Character tab's row list will not scroll");

        for (int clusterOrdinal = 0; clusterOrdinal < Groups.Length; ++clusterOrdinal)
        {
            BindLoop(clusterOrdinal, rosterBbox, locateString, sheet, mappings);
        }

        return true;
    }

    private static void BindLoop(int clusterOrdinal, WidgetBlueprintRosterBbox rosterBbox, Func<uint, uint, string?> locateString, KnobPage sheet, ClientBindings mappings)
    {
        ClusterSpec cluster = Groups[clusterOrdinal];
        AssemblePreambleRank(rosterBbox, cluster.HeaderKey, locateString);
        foreach (RankSpec spec in cluster.Rows)
            AssembleFlipRank(rosterBbox, spec, sheet, locateString, mappings);
        AssembleSeparatorRank(rosterBbox);
    }

    private static void AssemblePreambleRank(
        WidgetBlueprintRosterBbox rosterBbox, string preambleTag, Func<uint, uint, string?> locateString)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(PreambleBlueprintOrdinal) is not WidgetPhrase preamble)
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: header template didn't build as "
                + $"WidgetPhrase for '{preambleTag}'.");
            return;
        }

        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(preambleTag));
        if (caption is null)
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: header string '{preambleTag}' didn't "
                + "resolve from the DAT string table - the row renders with no text rather "
                + "than an invented label");
            return;
        }

        preamble.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, preamble.DefaultTint) };
    }

    private static void AssembleSeparatorRank(WidgetBlueprintRosterBbox rosterBbox)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(SeparatorBlueprintOrdinal) is null)
        {
            Console.WriteLine(
                "[UI] ToonKnobsSheetDriver: separator template didn't build");
        }
    }

    private static void AssembleFlipRank(
        WidgetBlueprintRosterBbox rosterBbox,
        RankSpec spec,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        ClientBindings mappings)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(FlipBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: toggle template didn't build for "
                + $"{spec.RetailName} (0x{(uint)spec.Id:X2}).");
            return;
        }

        WidgetBtn? tickbox = SeekTickbox(rank);
        if (tickbox is null)
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: no checkbox child found in the "
                + $"toggle row for {spec.RetailName} (0x{(uint)spec.Id:X2}).");
            return;
        }

        string captionTag = $"ID_PlayerOption_{spec.RetailName}";
        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (caption is not null)
            tickbox.Label = caption;
        else
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: label '{captionTag}' didn't resolve - "
                + "row renders with no caption rather than invented English");

        tickbox.CaptionColor = spec.StoreOnly
            ? WidgetRenderScope.VaultSoleLegendTint
            : Vector4.One;

        string? hint = locateString(
            StringChartIdent, DatStringPicker.CalculateDigest(captionTag + "_Help"));
        if (hint is not null)
            tickbox.TooltipText = hint;

        if (!ToonOptionChart.TryGet(spec.Id, out ToonOptionChartEntry listing))
        {
            Console.WriteLine(
                $"[UI] ToonKnobsSheetDriver: {spec.RetailName} "
                + $"(0x{(uint)spec.Id:X2}) isn't in ToonOptionChart");
            return;
        }

        bool starting = mappings.CurrentValue(spec.Id);
        tickbox.Selected = starting;

        BoolKnobRow row_ = new BoolKnobRow(
            starting,
            listing.ClientDefault,
            enact: val =>
            {
                tickbox.Selected = val;
                mappings.SetOption(spec.Id, val);
            },
            scan: () => mappings.CurrentValue(spec.Id),
            renew: val => tickbox.Selected = val);
        sheet.Register(row_);

        tickbox.OnClick = () => row_.AssignLatestVal(tickbox.Selected);
    }

    private static WidgetBtn? SeekTickbox(WidgetElem trunk)
    {
        if (trunk is WidgetBtn straight) return straight;
        foreach (WidgetElem descendant in trunk.Children)
            if (descendant is WidgetBtn btn)
                return btn;
        return null;
    }
}

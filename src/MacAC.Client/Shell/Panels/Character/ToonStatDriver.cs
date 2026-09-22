using System.Globalization;
using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public static partial class ToonStatDriver
{
    public const uint MonikerIdent = 0x10000231u;   // m_pNameText

    public const uint LineageIdent = 0x10000232u;   // m_pHeritageText

    public const uint PkConditionIdent = 0x10000233u;   // m_pPKStatusText

    public const uint TierLegendIdent = 0x1000023Au;

    public const uint TierIdent = 0x1000023Bu;   // m_pLevelText  (right-side level area)

    public const uint SumXpCaptionIdent = 0x10000234u;

    public const uint SumXpIdent = 0x10000235u;   // m_pTotalXPText

    public const uint XpGaugeIdent = 0x10000236u;   // m_pXPToLevelMeter (UiMeter)

    public const uint XpUpcomingCaptionIdent = 0x10000237u;

    public const uint XpUpcomingValIdent = 0x10000238u;

    public const uint RosterBboxIdent = 0x1000023Du;

    public const uint RosterScrollerTag = 0x1000023Eu;   // m_pListBox vertical scrollbar gutter

    public const uint RosterDividerIdent = 0x1000023Fu;   // bottom divider above footer

    public const uint LuminanceCaptionIdent = 0x100005C5u;

    public const uint LuminanceValIdent = 0x100005C6u;

    private const string LuminanceLegend = "Luminance:";

    public const uint FooterPhaseAIdent = 0x10000240u;

    public const uint FooterPhaseBIdent = 0x10000241u;

    public const uint FooterPhaseCIdent = 0x10000247u;

    public const uint TabAttribIdent = 0x10000228u;  // Attributes tab group

    public const uint TabAptitudesIdent = 0x10000229u;  // Skills tab group

    public const uint TabBannersIdent = 0x10000538u;  // Titles tab group

    public const uint AttrsSheetIdent = 0x1000022Bu;

    public const uint AptitudesSheetIdent = 0x1000022Cu;

    public const uint BannersSheetIdent = 0x10000539u;

    public const uint FooterBannerIdent = 0x1000024eu;  // GetFooterTitleLabel

    public const uint FooterLine1Caption = 0x10000242u;  // GetFooterLineOneLabel

    public const uint FooterLine1Val = 0x10000243u;  // GetFooterLineOneValue

    public const uint FooterLine2Caption = 0x10000244u;  // GetFooterLineTwoLabel

    public const uint FooterLine2Val = 0x10000245u;  // GetFooterLineTwoValue

    public const uint EmitOneIdent = 0x10000246u;   // raise × 1

    public const uint EmitTenIdent = 0x100005EBu;   // raise × 10

    private static readonly Vector4 Body = new(0.92f, 0.90f, 0.82f, 1f);   // parchment-white body text

    private static readonly Vector4 HighlightBg = new(1f, 0.75f, 0.2f, 0.25f);

    private static readonly Vector4 CanonBuffGreen = new(0f, 1f, 0f, 1f);

    private static readonly Vector4 CanonDebuffRed = new(1f, 0f, 0f, 1f);

    private static readonly Vector4 CanonVitaeBlue = new(127f / 255f, 1f, 1f, 1f);

    private const float RankHeight = 20f;

    private const float RankGlyphX = 0f;

    private const float RankGlyphDims = 20f;

    private const float RankLabelX = 25f;

    private const float RankLabelW = 150f;

    private const float RankValX = 175f;

    private const float RankValW = 100f;

    private const float RankPadX = 4f;

    private const float AptitudePreambleHeight = 20f;

    private const float AptitudeSubstanceWidth = 282f;

    private const uint AptitudePreambleSpecializedSprite = 0x06000F90u;

    private const uint AptitudePreambleTrainedSprite = 0x06000F86u;

    private const uint AptitudePreambleUntrainedSprite = 0x06000F98u;

    private const uint AptitudePreambleUnusableSprite = 0x06000F89u;

    private const uint RankHighlightSprite = 0x06000F93u;

    private const uint RankNormSprite = 0x06004CC2u;

    private const uint AttrGlyphBucket = 0x10000002u;

    private const uint VitalGlyphBucket = 0x10000003u;

    public enum ToonStatTab
    {
        Attributes,
        Skills,
        Titles,
    }

    public sealed record Binding(
        Action Refresh,
        Action<ToonStatTab> ShowTab,
        Func<ToonStatTab> CurrentTab);

    public enum EmitMarkFlavor
    {
        Attribute,
        Vital,
        Skill,
        TrainSkill,
    }

    public readonly record struct RaiseAsk(
        EmitMarkFlavor Kind,
        uint StatId,
        long Cost,
        int Amount);

    public delegate void RaiseRequestHandler(RaiseAsk request, Action completed);

    private sealed record SkillRowWiring(WidgetClickablePane Panel, ToonSkill Skill);

    internal static readonly (string name, uint iconDid, uint statId)[] AttrRanks =
    [
        ("Strength",     0x060002C8u, 1u),
        ("Endurance",    0x060002C4u, 2u),
        ("Coordination", 0x060002C9u, 4u),
        ("Quickness",    0x060002C6u, 3u),
        ("Focus",        0x060002C5u, 5u),
        ("Self",         0x060002C7u, 6u),
    ];

    internal static readonly (string name, uint iconDid, uint maxStatId)[] VitalRanks =
    [
        ("Health",  0x06004C3Bu, 1u),
        ("Stamina", 0x06004C3Cu, 3u),
        ("Mana",    0x06004C3Du, 5u),
    ];

    private static readonly IReadOnlyDictionary<uint, string> AttrBlurbs =
        new Dictionary<uint, string>
        {
            [1u] = "Measures your character's muscular power.",       // Strength
            [2u] = "Measures how healthy your character is.",         // Endurance
            [3u] = "Measures how fast your character is.",            // Quickness
            [4u] = "Measures your character's reflexes",
            [5u] = "Measures your character's mind and senses.",      // Focus
            [6u] = "Measures your character's willpower.",            // Self
        };

    private static readonly IReadOnlyDictionary<uint, string> Attribute2ndBlurbs =
        new Dictionary<uint, string>
        {
            [1u] = "(Endurance/2)\nIf you run out of health, you will die!",   // Health
            [3u] = "(Endurance)\nAffects your actions and movement.",          // Stamina
            [5u] = "(Self)\nAffects how much magic you can cast.",             // Mana
        };

    public static Binding Bind(
        ImportedArrangement arrangement,
        Func<ToonSheet> blob,
        WidgetDatFont? datTypeface = null,
        WidgetDatFont? rankDatTypeface = null,
        Func<uint, (uint handle, int w, int h)>? spriteLocate = null,
        RaiseRequestHandler? onEmitReq = null,
        Action? onShut = null,
        Func<uint, uint, uint>? glyphDidLocate = null)
    {
        rankDatTypeface ??= datTypeface;
        WindowChromeDriver.WireShutBtn(arrangement, onShut);

        ToonStatTab[] engagedTab = new[] { ToonStatTab.Attributes };
        int[] attrSel = new[] { -1 };
        int[] aptitudeSel = new[] { -1 };
        List<WidgetElem> engagedRosterListings = new List<WidgetElem>();
        var latestAttrRanks = new List<WidgetClickablePane>();
        var latestAptitudeRanks = new List<SkillRowWiring>();
        WidgetElem? attrsTab = arrangement.SeekElem(TabAttribIdent);
        WidgetElem? aptitudesTab = arrangement.SeekElem(TabAptitudesIdent);
        WidgetElem? bannersTab = arrangement.SeekElem(TabBannersIdent);
        WidgetElem? substanceSheet = SeekStraightDescendantByIdent(arrangement.Root, AttrsSheetIdent);
        WidgetElem? bannersSheet = SeekStraightDescendantByIdent(arrangement.Root, BannersSheetIdent);

        CaptionAuthoredTint(arrangement, substanceSheet, MonikerIdent, null, () => blob().Name);
        CaptionAuthoredTint(arrangement, substanceSheet, LineageIdent, null, () => ToonIdentityText.StatPreambleStroke(blob()));
        CaptionAuthoredTint(arrangement, substanceSheet, PkConditionIdent, null, () => blob().PkCondition ?? string.Empty);

        CaptionTwoStroke(arrangement, substanceSheet, TierLegendIdent, null, Body, "Character", "Level");

        CaptionAuthoredTint(arrangement, substanceSheet, TierIdent, null,
            () => blob().Level is int lvl ? lvl.ToString(CultureInfo.InvariantCulture) : "???");

        CaptionLeft(arrangement, substanceSheet, SumXpCaptionIdent, null, Body, static () => "Total Experience (XP):");
        CaptionRight(arrangement, substanceSheet, SumXpIdent, null, Body, () => ComposeXp(blob().SumXp));

        if (SeekElemByDatIdent(arrangement, substanceSheet, XpGaugeIdent) is WidgetGauge gauge)
        {
            gauge.Populate = () => blob().XpRatio;

            if (SeekPhraseByDatIdent(arrangement, substanceSheet, XpUpcomingCaptionIdent) is WidgetPhrase xpCaption)
            {
                if (datTypeface is not null) xpCaption.DatFont = datTypeface;
                xpCaption.ClickThrough = true;
                xpCaption.Centered = false;
                xpCaption.RightAligned = false;
                xpCaption.Padding = 0f;

                if (SeekElemByDatIdent(arrangement, substanceSheet, SumXpCaptionIdent) is { } sumXpLbl)
                {
                    float xpUpcomingLeft = sumXpLbl.Left - gauge.Left;
                    xpCaption.Left = xpUpcomingLeft >= 0f ? xpUpcomingLeft : 0f;
                }

                xpCaption.StrokesSupplier = static () => new[] { new WidgetPhrase.Line("XP for next level:", Body) };
            }
            if (SeekPhraseByDatIdent(arrangement, substanceSheet, XpUpcomingValIdent) is WidgetPhrase xpVal)
            {
                if (datTypeface is not null) xpVal.DatFont = datTypeface;
                xpVal.ClickThrough = true;
                xpVal.RightAligned = true;
                xpVal.OneLine = true;
                xpVal.Padding = 0f;
                xpVal.StrokesSupplier = () => new[] { new WidgetPhrase.Line(ComposeXp(blob().XpToUpcomingTier), Body) };
            }
        }

        bool LuminanceShown(ToonSheet sheet) =>
            sheet.Level is int lvl && lvl >= 200 && sheet.MaximumLuminance is not 0;

        if (SeekPhraseByDatIdent(arrangement, substanceSheet, LuminanceCaptionIdent) is WidgetPhrase luminanceCaption)
        {
            luminanceCaption.StrokesSupplier = () => LuminanceShown(blob())
                ? new[] { new WidgetPhrase.Line(LuminanceLegend, luminanceCaption.DefaultTint) }
                : [];
        }
        if (SeekPhraseByDatIdent(arrangement, substanceSheet, LuminanceValIdent) is WidgetPhrase luminanceVal)
        {
            luminanceVal.StrokesSupplier = () =>
            {
                ToonSheet sheet = blob();
                if (!LuminanceShown(sheet)) return Array.Empty<WidgetPhrase.Line>();
                string phrase = $"{ComposeXp(sheet.AvailableLuminance)} / {ComposeXp(sheet.MaximumLuminance)}";
                return new[] { new WidgetPhrase.Line(phrase, luminanceVal.DefaultTint) };
            };
        }

        List<WidgetBtn> allRaise1 = new List<WidgetBtn>();
        List<WidgetBtn> allRaise10 = new List<WidgetBtn>();
        if (arrangement.Root is { } element)
        {
            GatherBtnsByIdent(element, EmitOneIdent, allRaise1, arrangement);
            GatherBtnsByIdent(element, EmitTenIdent, allRaise10, arrangement);
        }
        if (allRaise1.Count is 0 && arrangement.SeekElem(EmitOneIdent) is WidgetBtn button) allRaise1.Add(button);
        if (allRaise10.Count is 0 && arrangement.SeekElem(EmitTenIdent) is WidgetBtn b10) allRaise10.Add(b10);

        List<WidgetElem> footerDefaultClusters = new List<WidgetElem>();
        List<WidgetElem> footerChosenClusters = new List<WidgetElem>();
        List<WidgetElem> footerInactiveClusters = new List<WidgetElem>();
        if (substanceSheet is not null)
        {
            GatherElemsByDatIdent(substanceSheet, FooterPhaseAIdent, footerDefaultClusters);
            GatherElemsByDatIdent(substanceSheet, FooterPhaseBIdent, footerChosenClusters);
            GatherElemsByDatIdent(substanceSheet, FooterPhaseCIdent, footerInactiveClusters);
        }
        else if (arrangement.Root is not null)
        {
            GatherElemsByDatIdent(arrangement.Root, FooterPhaseAIdent, footerDefaultClusters);
            GatherElemsByDatIdent(arrangement.Root, FooterPhaseBIdent, footerChosenClusters);
            GatherElemsByDatIdent(arrangement.Root, FooterPhaseCIdent, footerInactiveClusters);
        }

        void AssignFooterChosen(bool chosen)
        {
            foreach (var g in footerDefaultClusters) g.Visible = !chosen;
            foreach (var g in footerChosenClusters) g.Visible = chosen;
            foreach (var g in footerInactiveClusters) g.Visible = false;
        }

        foreach (var b in allRaise1) b.Visible = false;
        foreach (var b in allRaise10) b.Visible = false;

        AssignFooterChosen(false);

        AttachFooterDynamic(arrangement, datTypeface, blob, engagedTab, attrSel, aptitudeSel, substanceSheet);
        AssignFooterChosen(false);

        WidgetElem? statRoster =
            (substanceSheet is not null
                ? SeekInSubtree(substanceSheet, static elem => HasDatElemIdent(elem, RosterBboxIdent))
                : null)
            ?? arrangement.SeekElem(RosterBboxIdent);
        if (statRoster is not null && statRoster.ArrangementRule is null)
            statRoster.Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom;

        if (arrangement.Root is { } stretchTrunk)
        {
            AssignCompatibilityMooringsAllByIdent(stretchTrunk, RosterScrollerTag, MooringRims.Left | MooringRims.Top | MooringRims.Bottom);
            AssignCompatibilityMooringsAllByIdent(stretchTrunk, RosterDividerIdent, MooringRims.Left | MooringRims.Bottom);
            AssignCompatibilityMooringsAllByIdent(stretchTrunk, FooterPhaseAIdent, MooringRims.Left | MooringRims.Bottom);
            AssignCompatibilityMooringsAllByIdent(stretchTrunk, FooterPhaseBIdent, MooringRims.Left | MooringRims.Bottom);
            AssignCompatibilityMooringsAllByIdent(stretchTrunk, FooterPhaseCIdent, MooringRims.Left | MooringRims.Bottom);
        }

        var aptitudeScroller = ReadyAptitudeScroller(arrangement, substanceSheet, statRoster, spriteLocate);
        WireEmitBtnPresses(allRaise1, allRaise10, blob, engagedTab, attrSel, aptitudeSel,
            () => latestAptitudeRanks, onEmitReq, RenewFollowingEmit);
        ReassembleEngagedRoster();

        CanonTabWiring.AssignPress(attrsTab, () => SwitchTab(ToonStatTab.Attributes));
        CanonTabWiring.AssignPress(aptitudesTab, () => SwitchTab(ToonStatTab.Skills));
        CanonTabWiring.AssignPress(bannersTab, () => SwitchTab(ToonStatTab.Titles));
        RefreshTabPhases();

        if (arrangement.Root is { } trunk)
        {
            foreach (var page in trunk.Children)
            {
                uint ident = DatElemIdent(page);
                if (ident is AttrsSheetIdent or AptitudesSheetIdent or BannersSheetIdent)
                    page.Visible = ident == AttrsSheetIdent;
            }
        }

        void SwitchTab(ToonStatTab tab)
        {
            if (engagedTab[0] == tab) return;
            engagedTab[0] = tab;
            attrSel[0] = -1;
            aptitudeSel[0] = -1;
            AssignFooterChosen(false);

            bool unhideBanners = tab == ToonStatTab.Titles;
            bannersSheet?.Visible = unhideBanners;
            substanceSheet?.Visible = !unhideBanners;

            if (unhideBanners)
            {
                foreach (var b in allRaise1) b.Visible = false;
                foreach (var b in allRaise10) b.Visible = false;
            }
            else
            {
                ReassembleEngagedRoster();
                RenewEngagedEmitBtns();
            }

            RefreshTabPhases();
            Console.WriteLine($"[CharacterStat] Tab click: {tab}");
        }

        void RefreshTabPhases()
        {
            CanonTabWiring.ApplyOpen(attrsTab, engagedTab[0] == ToonStatTab.Attributes);
            CanonTabWiring.ApplyOpen(aptitudesTab, engagedTab[0] == ToonStatTab.Skills);
            CanonTabWiring.ApplyOpen(bannersTab, engagedTab[0] == ToonStatTab.Titles);
        }

        void ReassembleEngagedRoster()
        {
            if (statRoster is null) return;

            int earlierRollY = engagedRosterListings
                .OfType<WidgetScrollablePane>()
                .FirstOrDefault()?.Scroll.RollY ?? 0;

            foreach (var listing in engagedRosterListings)
                statRoster.DropDescendant(listing);
            engagedRosterListings.Clear();
            latestAttrRanks.Clear();
            latestAptitudeRanks.Clear();

            bool isAptitudes = engagedTab[0] == ToonStatTab.Skills;
            float substanceW = isAptitudes
                ? AptitudeViewRectWidth(statRoster, aptitudeScroller)
                : RankSubstanceWidth(statRoster);
            WidgetScrollablePane viewRect = new WidgetScrollablePane
            {
                Left = 0f,
                Top = 0f,
                Width = substanceW,
                Height = statRoster.Height,
                LineHeight = (int)RankHeight,
                Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom,
            };
            statRoster.AddChild(viewRect);
            viewRect.GrabLatestMooringBaseline();
            engagedRosterListings.Add(viewRect);

            if (isAptitudes)
            {
                AssembleAptitudeRanks(viewRect, rankDatTypeface, spriteLocate, blob, aptitudeSel,
                    allRaise1, allRaise10, AssignFooterChosen, out latestAptitudeRanks);
            }
            else
            {
                latestAttrRanks = AssembleAttrRanks(viewRect, rankDatTypeface, spriteLocate, blob, attrSel,
                    allRaise1, allRaise10, AssignFooterChosen, glyphDidLocate);
            }

            if (earlierRollY > 0)
            {
                viewRect.ArrangementScrollableDescendants();
                viewRect.Scroll.AssignRollY(earlierRollY);
            }

            if (aptitudeScroller is not null)
            {
                aptitudeScroller.Model = viewRect.Scroll;
                aptitudeScroller.Visible = true;
            }
        }

        void RenewEngagedEmitBtns()
        {
            if (engagedTab[0] == ToonStatTab.Attributes)
            {
                RenewEmitBtns(attrSel[0], blob, allRaise1, allRaise10);
                return;
            }

            var sheet = blob();
            ToonSkill? chosenAptitude =
                aptitudeSel[0] >= 0 && aptitudeSel[0] < latestAptitudeRanks.Count
                    ? SeekAptitude(sheet, latestAptitudeRanks[aptitudeSel[0]].Skill.Id)
                    : null;
            RenewAptitudeEmitBtns(chosenAptitude, sheet, allRaise1, allRaise10);
        }

        void SoftRenewAptitudeRanks()
        {
            var sheet = blob();
            for (int idx = 0; idx < latestAptitudeRanks.Count; ++idx)
            {
                var rank = latestAptitudeRanks[idx];
                if (SeekAptitude(sheet, rank.Skill.Id) is { } online)
                    latestAptitudeRanks[idx] = rank with { Skill = online };
            }
        }

        void RenewFollowingEmit(uint? chosenAptitudeIdent)
        {
            if (engagedTab[0] == ToonStatTab.Skills)
            {
                if (chosenAptitudeIdent is null
                    && aptitudeSel[0] >= 0
                    && aptitudeSel[0] < latestAptitudeRanks.Count)
                {
                    chosenAptitudeIdent = latestAptitudeRanks[aptitudeSel[0]].Skill.Id;
                }

                if (!AptitudeArrangementFits(latestAptitudeRanks, blob()))
                {
                    ReassembleEngagedRoster();

                    aptitudeSel[0] = -1;
                    if (chosenAptitudeIdent is uint ident)
                    {
                        for (int idx = 0; idx < latestAptitudeRanks.Count; ++idx)
                        {
                            if (latestAptitudeRanks[idx].Skill.Id == ident)
                            {
                                aptitudeSel[0] = idx;
                                break;
                            }
                        }
                    }

                    ImposeAptitudePickVisuals(aptitudeSel[0], latestAptitudeRanks, spriteLocate);
                    AssignFooterChosen(aptitudeSel[0] >= 0);
                }
                else
                {
                    SoftRenewAptitudeRanks();
                }
            }

            RenewEngagedEmitBtns();
        }

        return new Binding(
            () => RenewFollowingEmit(null),
            SwitchTab,
            () => engagedTab[0]);
    }

    private static List<WidgetClickablePane> AssembleAttrRanks(
        WidgetElem roster,
        WidgetDatFont? datTypeface,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        Func<ToonSheet> blob,
        int[] sel,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10,
        Action<bool> setFooterChosen,
        Func<uint, uint, uint>? glyphDidLocate)
    {
        float rosterW = RankSubstanceWidth(roster);
        float y = 0f;
        var ranks = new List<WidgetClickablePane>();

        for (int idx = 0; idx < AttrRanks.Length; ++idx)
        {
            var (rankLabel, glyphDid, statIdent) = AttrRanks[idx];
            int rankOrdinal = idx;

            WidgetClickablePane rank = AppendRank(roster, datTypeface, spriteLocate,
                left: 0f, top: y, width: rosterW, height: RankHeight,
                glyphDid: LocateGlyphDid(glyphDidLocate, statIdent, AttrGlyphBucket, glyphDid),
                labelPhrase: rankLabel,
                valSupplier: () =>
                {
                    ToonSheet sheet = blob();
                    int v = rankOrdinal switch
                    {
                        0 => sheet.Strength,
                        1 => sheet.Endurance,
                        2 => sheet.Coordination,
                        3 => sheet.Quickness,
                        4 => sheet.Focus,
                        5 => sheet.Self,
                        _ => 0,
                    };
                    return v.ToString();
                },
                valTintSupplier: () => AttrValTint(blob(), rankOrdinal));
            rank.TooltipText = AttrBlurbs.GetValueOrDefault(statIdent);

            rank.OnClick = () =>
            {
                ProcessRankPress(rankOrdinal, sel, ranks, spriteLocate, blob, allRaise1, allRaise10);
                setFooterChosen(sel[0] >= 0);
            };
            ranks.Add(rank);
            y += RankHeight;
        }

        for (int idx = 0; idx < VitalRanks.Length; ++idx)
        {
            var (rankLabel, glyphDid, upperStatIdent) = VitalRanks[idx];
            int rankOrdinal = idx;
            int absOrdinal = AttrRanks.Length + idx;

            WidgetClickablePane rank = AppendRank(roster, datTypeface, spriteLocate,
                left: 0f, top: y, width: rosterW, height: RankHeight,
                glyphDid: LocateGlyphDid(glyphDidLocate, upperStatIdent, VitalGlyphBucket, glyphDid),
                labelPhrase: rankLabel,
                valSupplier: () =>
                {
                    ToonSheet sheet = blob();
                    return rankOrdinal switch
                    {
                        0 => $"{sheet.HealthCurrent}/{sheet.HealthMax}",
                        1 => $"{sheet.StaminaLatest}/{sheet.StaminaUpper}",
                        2 => $"{sheet.ManaCurrent}/{sheet.ManaMax}",
                        _ => string.Empty,
                    };
                },
                valTintSupplier: () => VitalValTint(blob(), rankOrdinal));
            rank.TooltipText = Attribute2ndBlurbs.GetValueOrDefault(upperStatIdent);

            rank.OnClick = () =>
            {
                ProcessRankPress(absOrdinal, sel, ranks, spriteLocate, blob, allRaise1, allRaise10);
                setFooterChosen(sel[0] >= 0);
            };
            ranks.Add(rank);
            y += RankHeight;
        }

        return ranks;
    }

    private static List<WidgetElem> AssembleAptitudeRanks(
        WidgetElem roster,
        WidgetDatFont? datTypeface,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        Func<ToonSheet> blob,
        int[] sel,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10,
        Action<bool> setFooterChosen,
        out List<SkillRowWiring> aptitudeRanks)
    {
        float rosterW = RankSubstanceWidth(roster);
        float y = 0f;
        List<WidgetElem> listings = new List<WidgetElem>();
        List<SkillRowWiring> mappings = new List<SkillRowWiring>();

        AppendBin("Specialized Skills", AptitudePreambleSpecializedSprite,
            SequencedAptitudes(blob(), ToonSkillAdvancementClass.Specialized, usableUntrained: null));
        AppendBin("Trained Skills", AptitudePreambleTrainedSprite,
            SequencedAptitudes(blob(), ToonSkillAdvancementClass.Trained, usableUntrained: null));
        AppendBin("Untrained Skills", AptitudePreambleUntrainedSprite,
            SequencedAptitudes(blob(), ToonSkillAdvancementClass.Untrained, usableUntrained: true));
        AppendBin("Unusable Skills", AptitudePreambleUnusableSprite,
            SequencedAptitudes(blob(), ToonSkillAdvancementClass.Untrained, usableUntrained: false));

        aptitudeRanks = mappings;
        return listings;

        void AppendBin(string banner, uint spriteIdent, IReadOnlyList<ToonSkill> aptitudes)
        {
            WidgetBoard preamble = AppendAptitudePreamble(roster, datTypeface, spriteLocate, 0f, y, rosterW, banner, spriteIdent);
            listings.Add(preamble);
            y += AptitudePreambleHeight;

            foreach (var aptitude in aptitudes)
            {
                int rankOrdinal = mappings.Count;
                ToonSkill OnlineAptitude() =>
                    SeekAptitude(blob(), aptitude.Id) ?? aptitude;
                WidgetClickablePane rank = AppendRank(roster, datTypeface, spriteLocate,
                    left: 0f, top: y, width: rosterW, height: RankHeight,
                    glyphDid: aptitude.IconDid,
                    labelPhrase: aptitude.Name,
                    valSupplier: () => OnlineAptitude().CurrentLevel.ToString(),
                    valTintSupplier: () => AptitudeValTint(OnlineAptitude()),
                    labelTint: Vector4.One);
                rank.TooltipText = aptitude.TooltipText;
                rank.OnClick = () =>
                {
                    ProcessAptitudeRankPress(rankOrdinal, sel, mappings, spriteLocate, blob, allRaise1, allRaise10);
                    setFooterChosen(sel[0] >= 0);
                };
                mappings.Add(new SkillRowWiring(rank, aptitude));
                listings.Add(rank);
                y += RankHeight;
            }
        }
    }

    private static WidgetBoard AppendAptitudePreamble(
        WidgetElem roster,
        WidgetDatFont? datTypeface,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        float left,
        float top,
        float width,
        string banner,
        uint spriteIdent)
    {
        WidgetBoard preamble = new WidgetBoard
        {
            Left = left,
            Top = top,
            Width = width,
            Height = AptitudePreambleHeight,
            BackgroundColor = spriteLocate is null ? new Vector4(0.12f, 0.12f, 0.14f, 0.65f) : Vector4.Zero,
            BackgroundSprite = spriteLocate is not null ? spriteIdent : 0u,
            SpriteResolve = spriteLocate is not null
                ? ident => { var (h, w, ht) = spriteLocate(ident); return (h, w, ht); }
            : null,
            BorderTint = Vector4.Zero,
            Moorings = MooringRims.Left | MooringRims.Top,
            ClickThrough = true,
        };

        WidgetPhrase caption = new WidgetPhrase
        {
            Left = RankPadX,
            Top = 0f,
            Width = MathF.Max(1f, width - RankPadX * 2f),
            Height = AptitudePreambleHeight,
            DatFont = datTypeface,
            ClickThrough = true,
            Centered = false,
            RightAligned = false,
            Padding = 1f,
            Moorings = MooringRims.Left | MooringRims.Top,
        };
        string grabbed = banner;
        caption.StrokesSupplier = () => new[] { new WidgetPhrase.Line(grabbed, Vector4.One) };
        preamble.AddChild(caption);
        roster.AddChild(preamble);
        return preamble;
    }

    private static void ProcessRankPress(
        int clickedOrdinal,
        int[] sel,
        List<WidgetClickablePane> ranks,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        Func<ToonSheet> blob,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10)
    {
        int newSel = (sel[0] == clickedOrdinal) ? -1 : clickedOrdinal;
        sel[0] = newSel;

        string rankLabel = FetchRankLabel(newSel);
        Console.WriteLine($"[CharacterStat] Row click: index={clickedOrdinal} → selected={newSel} ({rankLabel})");

        for (int idx = 0; idx < ranks.Count; ++idx)
        {
            WidgetClickablePane rank = ranks[idx];
            if (idx == newSel)
            {
                if (spriteLocate is not null)
                {
                    ProcessRankPressBranch3(rank, spriteLocate);
                }
                else
                {
                    ProcessRankPressBranch2(rank);
                }
            }
            else
            {
                ProcessRankPressBranch(rank, spriteLocate);
            }
        }

        RenewEmitBtns(newSel, blob, allRaise1, allRaise10);
    }

    private static void ProcessRankPressBranch(WidgetClickablePane rank, Func<uint, (uint handle, int w, int h)>? spriteLocate)
    {
        rank.BackgroundColor = Vector4.Zero;
        rank.BackgroundSprite = spriteLocate is not null ? RankNormSprite : 0u;
        rank.SpriteResolve = spriteLocate;
    }

    private static void ProcessRankPressBranch2(WidgetClickablePane rank)
    {
        rank.BackgroundColor = HighlightBg;
        rank.BackgroundSprite = 0u;
        rank.SpriteResolve = null;
    }

    private static void ProcessRankPressBranch3(WidgetClickablePane rank, Func<uint, (uint handle, int w, int h)> spriteLocate)
    {
        rank.BackgroundColor = Vector4.Zero;
        rank.BackgroundSprite = RankHighlightSprite;
        rank.SpriteResolve = spriteLocate;
    }

    private static void ProcessAptitudeRankPress(
        int clickedOrdinal,
        int[] sel,
        List<SkillRowWiring> ranks,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        Func<ToonSheet> blob,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10)
    {
        int newSel = (sel[0] == clickedOrdinal) ? -1 : clickedOrdinal;
        sel[0] = newSel;

        string rankLabel = newSel >= 0 && newSel < ranks.Count ? ranks[newSel].Skill.Name : string.Empty;
        Console.WriteLine($"[CharacterStat] Skill row click: index={clickedOrdinal} -> selected={newSel} ({rankLabel})");

        ProcessAptitudeRankPressRest(newSel, ranks, spriteLocate, blob, allRaise1, allRaise10);
    }

    private static void ProcessAptitudeRankPressRest(int newSel, List<SkillRowWiring> ranks, Func<uint, (uint handle, int w, int h)>? spriteLocate, Func<ToonSheet> blob, List<WidgetBtn> allRaise1, List<WidgetBtn> allRaise10)
    {
        ImposeAptitudePickVisuals(newSel, ranks, spriteLocate);
        var sheet = blob();
        ToonSkill? chosenAptitude =
                    newSel >= 0 && newSel < ranks.Count
                        ? SeekAptitude(sheet, ranks[newSel].Skill.Id)
                        : null;
        RenewAptitudeEmitBtns(chosenAptitude, sheet, allRaise1, allRaise10);
    }

    private static void WireEmitBtnPresses(
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10,
        Func<ToonSheet> blob,
        ToonStatTab[] engagedTab,
        int[] attrSel,
        int[] aptitudeSel,
        Func<IReadOnlyList<SkillRowWiring>> aptitudeRanks,
        RaiseRequestHandler? onEmitReq,
        Action<uint?>? followingEmitReq)
    {
        foreach (var btn in allRaise1)
        {
            WidgetBtn grabbed = btn;
            grabbed.OnClick = () => ProcessEmitBtnPress(
                quantity: 1, blob, engagedTab, attrSel, aptitudeSel, aptitudeRanks, onEmitReq, followingEmitReq);
        }

        foreach (var btn in allRaise10)
        {
            WidgetBtn grabbed = btn;
            grabbed.OnClick = () => ProcessEmitBtnPress(
                quantity: 10, blob, engagedTab, attrSel, aptitudeSel, aptitudeRanks, onEmitReq, followingEmitReq);
        }
    }

    private static void ProcessEmitBtnPress(
        int quantity,
        Func<ToonSheet> blob,
        ToonStatTab[] engagedTab,
        int[] attrSel,
        int[] aptitudeSel,
        Func<IReadOnlyList<SkillRowWiring>> aptitudeRanks,
        RaiseRequestHandler? onEmitReq,
        Action<uint?>? followingEmitReq)
    {
        if (onEmitReq is null) return;

        ToonSheet sheet = blob();
        RaiseAsk? req;
        uint? chosenAptitudeIdent = null;

        if (engagedTab[0] == ToonStatTab.Attributes)
        {
            req = TryAssembleAttrEmitReq(sheet, attrSel[0], quantity);
        }
        else
        {
            var ranks = aptitudeRanks();
            uint? chosenIdent =
                aptitudeSel[0] >= 0 && aptitudeSel[0] < ranks.Count
                    ? ranks[aptitudeSel[0]].Skill.Id
                    : null;
            ToonSkill? chosenAptitude =
                chosenIdent is uint ident ? SeekAptitude(sheet, ident) : null;
            chosenAptitudeIdent = chosenAptitude?.Id;
            req = TryAssembleAptitudeEmitReq(sheet, chosenAptitude, quantity);
        }

        if (req is not { } val) return;

        onEmitReq(val, () => followingEmitReq?.Invoke(chosenAptitudeIdent));
    }

    private static WidgetClickablePane AppendRank(
        WidgetElem roster,
        WidgetDatFont? datTypeface,
        Func<uint, (uint handle, int w, int h)>? spriteLocate,
        float left, float top, float width, float height,
        uint glyphDid,
        string labelPhrase,
        Func<string> valSupplier,
        Func<Vector4>? valTintSupplier = null,
        Vector4? labelTint = null)
    {
        WidgetClickablePane rank = new WidgetClickablePane
        {
            AuthoredHintTrunkElemIdent =
                CanonTooltipExhibitor.SharedPopupSkinTrunkElemIdent,
            AuthoredHintArrangementDid =
                CanonTooltipExhibitor.SharedPopupSkinArrangementDid,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            BackgroundColor = Vector4.Zero,
            BackgroundSprite = spriteLocate is not null ? RankNormSprite : 0u,
            SpriteResolve = spriteLocate,
            BorderTint = Vector4.Zero,
            Moorings = MooringRims.Left | MooringRims.Top,
        };

        WidgetPhrase glyphElem = new WidgetPhrase
        {
            Left = RankGlyphX,
            Top = 0f,
            Width = RankGlyphDims,
            Height = RankGlyphDims,
            ClickThrough = true,
            DatFont = null,
            BackgroundSprite = spriteLocate is not null ? glyphDid : 0u,
            SpriteResolve = spriteLocate is not null
                ? ident => { var (h, w, ht) = spriteLocate(ident); return (h, w, ht); }
            : null,
            StrokesSupplier = static () => Array.Empty<WidgetPhrase.Line>(),
            Moorings = MooringRims.Left | MooringRims.Top,
        };

        string grabbedLabel = labelPhrase;
        Vector4 grabbedLabelTint = labelTint ?? Body;
        WidgetPhrase labelElem = new WidgetPhrase
        {
            Left = RankLabelX,
            Top = 0f,
            Width = RankLabelW,
            Height = height,
            DatFont = datTypeface,
            ClickThrough = true,
            Centered = false,
            RightAligned = false,
            Padding = 0f,
            OneLine = true,
            Moorings = MooringRims.Left | MooringRims.Top,
            StrokesSupplier = () => new[] { new WidgetPhrase.Line(grabbedLabel, grabbedLabelTint) }
        };

        WidgetPhrase valElem = new WidgetPhrase
        {
            Left = RankValX,
            Top = 0f,
            Width = RankValW,
            Height = height,
            DatFont = datTypeface,
            ClickThrough = true,
            RightAligned = true,
            OneLine = true,
            Moorings = MooringRims.Left | MooringRims.Top,
        };
        Func<string> grabbedSupplier = valSupplier;
        valElem.StrokesSupplier = () => new[] { new WidgetPhrase.Line(grabbedSupplier(), valTintSupplier?.Invoke() ?? Body) };

        rank.AddChild(glyphElem);
        rank.AddChild(labelElem);
        rank.AddChild(valElem);
        roster.AddChild(rank);
        return rank;
    }

    private static void AttachFooterDynamic(
        ImportedArrangement arrangement,
        WidgetDatFont? datTypeface,
        Func<ToonSheet> blob,
        ToonStatTab[] engagedTab,
        int[] attrSel,
        int[] aptitudeSel,
        WidgetElem? substanceSheet = null)
    {
        WidgetElem? phaseA = substanceSheet is not null
            ? SeekInSubtree(substanceSheet, static elem => elem is WidgetDatElement element && element.ElementId == FooterPhaseAIdent)
            : null;
        WidgetElem? phaseB = substanceSheet is not null
            ? SeekInSubtree(substanceSheet, static elem => elem is WidgetDatElement element && element.ElementId == FooterPhaseBIdent)
            : null;
        // Fallback: layout._byId (test layouts with a single page)
        phaseA ??= arrangement.SeekElem(FooterPhaseAIdent);
        phaseB ??= arrangement.SeekElem(FooterPhaseBIdent);

        WidgetPhrase? BySpot(float top, float left, uint backupIdent)
        {
            if (phaseA is not null)
            {
                foreach (var c in phaseA.Children)
                    if (c is WidgetPhrase text
                        && Math.Abs(c.Top - top) < 1f
                        && Math.Abs(c.Left - left) < 1f)
                        return text;
            }
            return arrangement.SeekElem(backupIdent) as WidgetPhrase;
        }

        WidgetPhrase? bannerElem = BySpot(0f, 0f, FooterBannerIdent);
        if (bannerElem is not null)
        {
            bannerElem.BackgroundSprite = 0;
            bannerElem.VerticalJustify = ClientVJustify.Top;
            bannerElem.OneLine = true;
        }
        if (bannerElem is not null)
        {
            bannerElem.ClickThrough = true;
            bannerElem.ExecutionsSupplier = () => AssembleChosenBannerExecutions(
                bannerElem,
                engagedTab[0],
                blob,
                attrSel,
                aptitudeSel);
            bannerElem.StrokesSupplier = () =>
            {
                string banner = AssembleChosenBannerPhrase(engagedTab[0], blob, attrSel, aptitudeSel);
                bool nothingChosen = engagedTab[0] == ToonStatTab.Skills
                    ? AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]) is null
                    : attrSel[0] < 0;
                return new[] { new WidgetPhrase.Line(banner, nothingChosen ? Body : Vector4.One) };
            };
        }

        WidgetPhrase? l1L = BySpot(20f, 5f, FooterLine1Caption);
        CaptionSupplier(l1L, null, Body, () =>
        {
            if (engagedTab[0] == ToonStatTab.Skills)
            {
                ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
                return aptitude is null
                    ? "Skill Credits Available:"
                    : aptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained
                    ? "Experience To Raise:"
                    : "Skill Credits To Raise:";
            }

            return attrSel[0] < 0 ? "Skill Credits Available:" : "Experience To Raise:";
        });

        WidgetPhrase? l1V = BySpot(20f, 200f, FooterLine1Val);
        CaptionSupplier(l1V, null, Body, () =>
        {
            ToonSheet sheet = blob();
            if (engagedTab[0] == ToonStatTab.Skills)
            {
                ToonSkill? aptitude = AptitudeAtReadoutOrdinal(sheet, aptitudeSel[0]);
                if (aptitude is null) return sheet.AptitudeCredits.ToString();
                long aptitudePrice = aptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained
                    ? aptitude.RaiseCost
                    : aptitude.TrainedCost;
                return aptitudePrice > 0 ? ComposeXp(aptitudePrice) : "Infinity!";
            }

            if (attrSel[0] < 0) return sheet.AptitudeCredits.ToString();
            long price = FetchEmitPrice(sheet, attrSel[0]);
            return price > 0 ? ComposeXp(price) : "Infinity!";
        });

        // Line-2 elements: pass null → keep dat font
        WidgetPhrase? l2L = BySpot(37f, 5f, FooterLine2Caption);
        CaptionSupplier(l2L, null, Body, () =>
        {
            if (engagedTab[0] == ToonStatTab.Skills)
            {
                ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
                if (aptitude is not null && aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                    return "Skill Credits Available:";
            }
            return "Unassigned Experience:";
        });

        WidgetPhrase? l2V = BySpot(37f, 200f, FooterLine2Val);
        CaptionSupplier(l2V, null, Body, () =>
        {
            ToonSheet sheet = blob();
            if (engagedTab[0] == ToonStatTab.Skills)
            {
                ToonSkill? aptitude = AptitudeAtReadoutOrdinal(sheet, aptitudeSel[0]);
                if (aptitude is not null && aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                    return sheet.AptitudeCredits.ToString();
            }
            return ComposeXp(sheet.UnassignedXp);
        });

        AttachChosenFooterPhase(phaseB);

        WidgetPhrase? PhraseByIdent(WidgetElem? phase, uint ident)
            => phase is null
                ? null
                : SeekInSubtree(phase, elem => elem is WidgetPhrase text && text.ElementId == ident) as WidgetPhrase;

        void AttachChosenFooterPhase(WidgetElem? phase)
        {
            WidgetPhrase? banner = PhraseByIdent(phase, FooterBannerIdent);
            if (banner is not null)
            {
                banner.BackgroundSprite = 0;
                banner.VerticalJustify = ClientVJustify.Top;
                banner.ClickThrough = true;
                banner.StrokesSupplier = () =>
                {
                    string bannerPhrase = AssembleChosenBannerPhrase(engagedTab[0], blob, attrSel, aptitudeSel);
                    bool nothingChosen = engagedTab[0] == ToonStatTab.Skills
                        ? AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]) is null
                        : attrSel[0] < 0;
                    return new[] { new WidgetPhrase.Line(bannerPhrase, nothingChosen ? Body : Vector4.One) };
                };
            }

            CaptionSupplier(PhraseByIdent(phase, FooterLine1Caption), null, Body, () =>
            {
                if (engagedTab[0] == ToonStatTab.Skills)
                {
                    ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
                    return aptitude is null
                        ? "Skill Credits Available:"
                        : aptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained
                        ? "Experience To Raise:"
                        : "Skill Credits To Raise:";
                }

                return attrSel[0] < 0 ? "Skill Credits Available:" : "Experience To Raise:";
            });

            CaptionSupplier(PhraseByIdent(phase, FooterLine1Val), null, Body, () =>
            {
                ToonSheet sheet = blob();
                if (engagedTab[0] == ToonStatTab.Skills)
                {
                    ToonSkill? aptitude = AptitudeAtReadoutOrdinal(sheet, aptitudeSel[0]);
                    if (aptitude is null) return sheet.AptitudeCredits.ToString();
                    long aptitudePrice = aptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained
                        ? aptitude.RaiseCost
                        : aptitude.TrainedCost;
                    return aptitudePrice > 0 ? ComposeXp(aptitudePrice) : "Infinity!";
                }

                if (attrSel[0] < 0) return sheet.AptitudeCredits.ToString();
                long price = FetchEmitPrice(sheet, attrSel[0]);
                return price > 0 ? ComposeXp(price) : "Infinity!";
            });

            CaptionSupplier(PhraseByIdent(phase, FooterLine2Caption), null, Body, () =>
            {
                if (engagedTab[0] == ToonStatTab.Skills)
                {
                    ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
                    if (aptitude is not null && aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                        return "Skill Credits Available:";
                }
                return "Unassigned Experience:";
            });

            CaptionSupplier(PhraseByIdent(phase, FooterLine2Val), null, Body, () =>
            {
                ToonSheet sheet = blob();
                if (engagedTab[0] == ToonStatTab.Skills)
                {
                    ToonSkill? aptitude = AptitudeAtReadoutOrdinal(sheet, aptitudeSel[0]);
                    if (aptitude is not null && aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                        return sheet.AptitudeCredits.ToString();
                }
                return ComposeXp(sheet.UnassignedXp);
            });
        }
    }
}

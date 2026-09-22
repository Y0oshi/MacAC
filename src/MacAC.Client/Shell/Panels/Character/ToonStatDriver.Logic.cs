using System.Globalization;
using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public static partial class ToonStatDriver
{

    internal static Vector4 AptitudeValTint(ToonSkill aptitude)
    {
        int withoutVitae = aptitude.CurrentLevel - aptitude.VitaeModifier;
        return withoutVitae > aptitude.BaseLevel ? CanonBuffGreen
         : withoutVitae < aptitude.BaseLevel ? CanonDebuffRed
         : Vector4.One;
    }

    internal static Vector4 AttrValTint(
        ToonSheet sheet,
        int rankOrdinal)
    {
        int diff = FetchAttrDiff(sheet, rankOrdinal);
        return diff > 0 ? CanonBuffGreen
            : diff < 0 ? CanonDebuffRed
            : Vector4.One;
    }

    internal static Vector4 VitalValTint(
        ToonSheet sheet,
        int vitalOrdinal)
    {
        if ((uint)vitalOrdinal >= 3u
            || vitalOrdinal >= sheet.VitalBaseUpperVals.Length
            || vitalOrdinal >= sheet.VitalVitaeModifiers.Length)

            return Vector4.One;

        int net = vitalOrdinal switch
        {
            0 => sheet.HealthMax,
            1 => sheet.StaminaUpper,
            2 => sheet.ManaMax,
            _ => 0,
        };
        int withoutVitae = net - sheet.VitalVitaeModifiers[vitalOrdinal];
        int baseline = sheet.VitalBaseUpperVals[vitalOrdinal];
        return withoutVitae > baseline ? CanonBuffGreen
            : withoutVitae < baseline ? CanonDebuffRed
            : Vector4.One;
    }

    internal static long FetchEmitPrice(ToonSheet sheet, int rankOrdinal)
        => FetchEmitPrice(sheet, rankOrdinal, quantity: 1);

    internal static long FetchEmitPrice(ToonSheet sheet, int rankOrdinal, int quantity)
    {
        long[] prices = quantity is 10 ? sheet.AttrRaise10Prices : sheet.AttrEmitPrices;
        return prices is null || rankOrdinal < 0 || rankOrdinal >= prices.Length ? 0L : prices[rankOrdinal];
    }

    // Return the display name for the row at ordinal, or an empty string if the index is out of range
    internal static string FetchRankLabel(int ordinal)
    {
        if (ordinal < 0) return string.Empty;
        if (ordinal < AttrRanks.Length) return AttrRanks[ordinal].name;
        int vi = ordinal - AttrRanks.Length;
        return vi < VitalRanks.Length ? VitalRanks[vi].name : string.Empty;
    }

    // Return the numeric value for the row at ordinal
    internal static string FetchRankValString(ToonSheet sheet, int ordinal)
    {
        return ordinal switch
        {
            0 => sheet.Strength.ToString(),
            1 => sheet.Endurance.ToString(),
            2 => sheet.Coordination.ToString(),
            3 => sheet.Quickness.ToString(),
            4 => sheet.Focus.ToString(),
            5 => sheet.Self.ToString(),
            6 => $"{sheet.HealthCurrent}/{sheet.HealthMax}",
            7 => $"{sheet.StaminaLatest}/{sheet.StaminaUpper}",
            8 => $"{sheet.ManaCurrent}/{sheet.ManaMax}",
            _ => string.Empty,
        };
    }

    internal static int FetchAttrDiff(ToonSheet sheet, int ordinal)
    {
        if ((uint)ordinal >= (uint)AttrRanks.Length) return 0;
        int[] baseVals = sheet.AttrBaseVals;
        return baseVals is null || ordinal >= baseVals.Length ? 0 : FetchRankNetAttrVal(sheet, ordinal) - baseVals[ordinal];
    }

    internal static int FetchAptitudeBuffSoleDiff(ToonSkill aptitude) =>
        (aptitude.CurrentLevel - aptitude.VitaeModifier) - aptitude.BaseLevel;
    private static bool AptitudeArrangementFits(
        IReadOnlyList<SkillRowWiring> ranks,
        ToonSheet sheet)
    {
        int anticipatedTally = 0;
        int rankOrdinal = 0;
        foreach (ToonSkill aptitude in IterateReadoutAptitudes(sheet))
        {
            ++anticipatedTally;
            if (rankOrdinal >= ranks.Count)
                return false;

            ToonSkill tied = ranks[rankOrdinal].Skill;
            if (tied.Id != aptitude.Id
                || tied.AdvancementClass != aptitude.AdvancementClass
                || tied.UsableUntrained != aptitude.UsableUntrained)

                return false;

            ++rankOrdinal;
        }

        return anticipatedTally == ranks.Count;
    }

    private static WidgetScroller? ReadyAptitudeScroller(
        ImportedArrangement arrangement,
        WidgetElem? substanceSheet,
        WidgetElem? statRoster,
        Func<uint, (uint handle, int w, int h)>? spriteLocate)
    {
        if (spriteLocate is null)
            return null;

        WidgetElem? src = statRoster?.Ancestor?.Children.FirstOrDefault(
                static elem => HasDatElemIdent(elem, RosterScrollerTag))
            ?? (substanceSheet is not null
                ? SeekInSubtree(substanceSheet, static elem => HasDatElemIdent(elem, RosterScrollerTag))
                : null)
            ?? arrangement.SeekElem(RosterScrollerTag);

        if (src is WidgetScroller extantBar)
        {
            extantBar.SpriteResolve ??= ident =>
            {
                var (hnd, width, height) = spriteLocate(ident);
                return (hnd, width, height);
            };
            extantBar.Visible = false;
            return extantBar;
        }

        WidgetElem? ancestor = src?.Ancestor ?? statRoster?.Ancestor;
        if (ancestor is null || statRoster is null)
            return null;

        float left = src is not null
            ? src.Left
            : statRoster.Left + MathF.Min(statRoster.Width, AptitudeSubstanceWidth);
        float top = src?.Top ?? statRoster.Top;
        float width = src?.Width > 0f ? src.Width : 16f;
        float height = src?.Height > 0f ? src.Height : statRoster.Height;
        int z = (src?.ZOrder ?? statRoster.ZOrder) + 1;

        src?.Visible = false;

        WidgetScroller bar = new WidgetScroller
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ZOrder = z,
            Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom,
        };
        ConfigureAptitudeScroller(bar, spriteLocate);
        bar.Visible = false;
        ancestor.AddChild(bar);
        return bar;
    }

    private static void ConfigureAptitudeScroller(
        WidgetScroller bar,
        Func<uint, (uint handle, int w, int h)> spriteLocate)
    {
        bar.SpriteResolve = ident => { var (h, w, ht) = spriteLocate(ident); return (h, w, ht); };
        CanonScrollbarChrome.ImposeVertical(bar);
    }

    private static float AptitudeViewRectWidth(WidgetElem statRoster, WidgetScroller? bar)
    {
        if (bar is not null && ReferenceEquals(bar.Ancestor, statRoster.Ancestor))
        {
            float widthToGutter = bar.Left - statRoster.Left;
            if (widthToGutter > 32f)
                return widthToGutter;
        }

        return statRoster.Width > 0f
            ? MathF.Min(statRoster.Width, AptitudeSubstanceWidth)
            : AptitudeSubstanceWidth;
    }

    private static float RankSubstanceWidth(WidgetElem roster)
    {
        return roster.Width > 0f ? MathF.Min(roster.Width, AptitudeSubstanceWidth) : AptitudeSubstanceWidth;
    }

    private static uint LocateGlyphDid(
        Func<uint, uint, uint>? locator,
        uint enumVal,
        uint bucket,
        uint backup)
    {
        if (locator is null) return backup;
        uint settled = locator(enumVal, bucket);
        return settled is not 0u ? settled : backup;
    }

    private static IReadOnlyList<ToonSkill> SequencedAptitudes(
        ToonSheet sheet,
        ToonSkillAdvancementClass advancement,
        bool? usableUntrained)
    {
        List<ToonSkill> outcome = new List<ToonSkill>();
        foreach (var aptitude in sheet.Skills)
        {
            if (aptitude.AdvancementClass != advancement) continue;
            if (usableUntrained is not null && aptitude.UsableUntrained != usableUntrained.Value) continue;
            outcome.Add(aptitude);
        }
        outcome.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return outcome;
    }

    private static ToonSkill? SeekAptitude(ToonSheet sheet, uint aptitudeIdent)
    {
        var aptitudes = sheet.Skills;
        for (int idx = 0; idx < aptitudes.Count; ++idx)
        {
            ToonSkill aptitude = aptitudes[idx];
            if (aptitude.Id == aptitudeIdent)
                return aptitude;
        }
        return null;
    }

    private static IEnumerable<ToonSkill> IterateReadoutAptitudes(ToonSheet sheet)
    {
        foreach (var aptitude in SequencedAptitudes(sheet, ToonSkillAdvancementClass.Specialized, usableUntrained: null))
            yield return aptitude;
        foreach (var aptitude in SequencedAptitudes(sheet, ToonSkillAdvancementClass.Trained, usableUntrained: null))
            yield return aptitude;
        foreach (var aptitude in SequencedAptitudes(sheet, ToonSkillAdvancementClass.Untrained, usableUntrained: true))
            yield return aptitude;
        foreach (var aptitude in SequencedAptitudes(sheet, ToonSkillAdvancementClass.Untrained, usableUntrained: false))
            yield return aptitude;
    }

    private static ToonSkill? AptitudeAtReadoutOrdinal(ToonSheet sheet, int ordinal)
    {
        if (ordinal < 0) return null;
        int num = 0;
        foreach (var aptitude in IterateReadoutAptitudes(sheet))
        {
            if (num == ordinal) return aptitude;
            ++num;
        }
        return null;
    }

    private static void ImposeAptitudePickVisuals(
        int chosenOrdinal,
        IReadOnlyList<SkillRowWiring> ranks,
        Func<uint, (uint handle, int w, int h)>? spriteLocate)
    {
        for (int idx = 0; idx < ranks.Count; ++idx)
        {
            WidgetClickablePane rank = ranks[idx].Panel;
            if (idx == chosenOrdinal)
            {
                if (spriteLocate is not null)
                {
                    rank.BackgroundColor = Vector4.Zero;
                    rank.BackgroundSprite = RankHighlightSprite;
                    rank.SpriteResolve = spriteLocate;
                }
                else
                {
                    rank.BackgroundColor = HighlightBg;
                    rank.BackgroundSprite = 0u;
                    rank.SpriteResolve = null;
                }
            }
            else
            {
                rank.BackgroundColor = Vector4.Zero;
                rank.BackgroundSprite = spriteLocate is not null ? RankNormSprite : 0u;
                rank.SpriteResolve = spriteLocate;
            }
        }
    }

    private static void RenewEmitBtns(
        int chosenOrdinal,
        Func<ToonSheet> blob,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10)
    {
        if (allRaise1.Count is 0 && allRaise10.Count is 0) return;

        if (chosenOrdinal < 0)
        {
            foreach (var button in allRaise1) button.Visible = false;
            foreach (var button in allRaise10) button.Visible = false;
            return;
        }

        ToonSheet sheet = blob();
        RenewEmitBtnsRest(allRaise1, allRaise10, chosenOrdinal, sheet);
    }

    private static void RenewEmitBtnsRest(List<WidgetBtn> allRaise1, List<WidgetBtn> allRaise10, int chosenOrdinal, ToonSheet sheet)
    {
        long cost1 = FetchEmitPrice(sheet, chosenOrdinal, quantity: 1);
        long cost10 = FetchEmitPrice(sheet, chosenOrdinal, quantity: 10);
        RenewEmitBtnsTail(allRaise1, allRaise10, sheet, cost1, cost10);
    }

    private static void RenewEmitBtnsTail(List<WidgetBtn> allRaise1, List<WidgetBtn> allRaise10, ToonSheet sheet, long cost1, long cost10)
    {
        bool affordable1 = !sheet.ExpectingEmit && cost1 > 0 && sheet.UnassignedXp >= cost1;
        bool affordable10 = !sheet.ExpectingEmit && cost10 > 0 && sheet.UnassignedXp >= cost10;
        foreach (var button in allRaise1)
        {
            button.Visible = true;
            button.TrySetCanonPhase(affordable1
                ? WidgetButtonStateMachine.Normal
                : WidgetButtonStateMachine.Ghosted);
        }
        foreach (var button in allRaise10)
        {
            button.Visible = true;
            button.TrySetCanonPhase(affordable10
                ? WidgetButtonStateMachine.Normal
                : WidgetButtonStateMachine.Ghosted);
        }
    }

    private static void RenewAptitudeEmitBtns(
        ToonSkill? chosenAptitude,
        ToonSheet sheet,
        List<WidgetBtn> allRaise1,
        List<WidgetBtn> allRaise10)
    {
        if (chosenAptitude is null)
        {
            foreach (var button in allRaise1) button.Visible = false;
            foreach (var button in allRaise10) button.Visible = false;
            return;
        }

        bool trained = chosenAptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained;
        long price = trained ? chosenAptitude.RaiseCost : chosenAptitude.TrainedCost;
        RenewAptitudeEmitBtnsRest(chosenAptitude, allRaise1, allRaise10, trained, price, sheet);
    }

    private static void RenewAptitudeEmitBtnsRest(ToonSkill chosenAptitude, List<WidgetBtn> allRaise1, List<WidgetBtn> allRaise10, bool trained, long price, ToonSheet sheet)
    {
        bool affordable = !sheet.ExpectingEmit && (trained
                ? price > 0 && sheet.UnassignedXp >= price
                : price > 0 && sheet.AptitudeCredits >= price);
        foreach (var button in allRaise1)
        {
            button.Visible = true;
            button.TrySetCanonPhase(affordable
                ? WidgetButtonStateMachine.Normal
                : WidgetButtonStateMachine.Ghosted);
        }
        foreach (var button in allRaise10)
        {
            button.Visible = trained;
            if (trained)
            {
                long cost10 = chosenAptitude.Raise10Cost;
                bool affordable10 = !sheet.ExpectingEmit && cost10 > 0 && sheet.UnassignedXp >= cost10;
                button.TrySetCanonPhase(affordable10
                    ? WidgetButtonStateMachine.Normal
                    : WidgetButtonStateMachine.Ghosted);
            }
        }
    }

    private static RaiseAsk? TryAssembleAttrEmitReq(
        ToonSheet sheet,
        int chosenOrdinal,
        int quantity)
    {
        if (chosenOrdinal < 0) return null;

        long price = FetchEmitPrice(sheet, chosenOrdinal, quantity);
        if (price <= 0 || sheet.UnassignedXp < price) return null;

        if (chosenOrdinal < AttrRanks.Length)
            return new RaiseAsk(EmitMarkFlavor.Attribute, AttrRanks[chosenOrdinal].statId, price, quantity);

        int vitalOrdinal = chosenOrdinal - AttrRanks.Length;
        return vitalOrdinal >= 0 && vitalOrdinal < VitalRanks.Length
            ? new RaiseAsk(EmitMarkFlavor.Vital, VitalRanks[vitalOrdinal].maxStatId, price, quantity)
            : null;
    }

    private static RaiseAsk? TryAssembleAptitudeEmitReq(
        ToonSheet sheet,
        ToonSkill? chosenAptitude,
        int quantity)
    {
        if (chosenAptitude is null) return null;

        bool trained = chosenAptitude.AdvancementClass >= ToonSkillAdvancementClass.Trained;
        if (!trained)
        {
            if (quantity is not 1) return null;
            long trainPrice = chosenAptitude.TrainedCost;
            return trainPrice > 0 && sheet.AptitudeCredits >= trainPrice
                ? new RaiseAsk(EmitMarkFlavor.TrainSkill, chosenAptitude.Id, trainPrice, quantity)
                : null;
        }

        long emitPrice = quantity is 10 ? chosenAptitude.Raise10Cost : chosenAptitude.RaiseCost;
        return emitPrice > 0 && sheet.UnassignedXp >= emitPrice
            ? new RaiseAsk(EmitMarkFlavor.Skill, chosenAptitude.Id, emitPrice, quantity is 10 ? 10 : 1)
            : null;
    }

    private static int FetchRankNetAttrVal(ToonSheet sheet, int ordinal)
    {
        return ordinal switch
        {
            0 => sheet.Strength,
            1 => sheet.Endurance,
            2 => sheet.Coordination,
            3 => sheet.Quickness,
            4 => sheet.Focus,
            5 => sheet.Self,
            _ => 0,
        };
    }

    private static string ComposeBuffDiff(int diff)
    {
        return diff switch
        {
            0 => string.Empty,
            > 0 => string.Create(CultureInfo.InvariantCulture, $" (+{diff})"),
            _ => string.Create(CultureInfo.InvariantCulture, $" ({diff})"),
        };
    }

    private static string ComposeVitaeDiff(int vitaeModifier)
    {
        return vitaeModifier < 0
            ? string.Create(CultureInfo.InvariantCulture, $" ({vitaeModifier})")
            : string.Empty;
    }

    private static string AssembleChosenBannerPhrase(
        ToonStatTab tab,
        Func<ToonSheet> blob,
        int[] attrSel,
        int[] aptitudeSel)
    {
        if (tab == ToonStatTab.Skills)
        {
            ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
            if (aptitude is null) return "Select a Skill to Improve";
            if (aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                return aptitude.Name;

            string vitaeSuffix = ComposeVitaeDiff(aptitude.VitaeModifier);
            string buffSuffix = ComposeBuffDiff(FetchAptitudeBuffSoleDiff(aptitude));
            return $"{aptitude.Name}: {aptitude.CurrentLevel}{vitaeSuffix}{buffSuffix}";
        }

        if (attrSel[0] < 0) return "Select an Attribute to Improve";
        var sheet = blob();
        string label = FetchRankLabel(attrSel[0]);
        string val = FetchRankValString(sheet, attrSel[0]);
        string diff = ComposeBuffDiff(FetchAttrDiff(sheet, attrSel[0]));
        return $"{label}: {val}{diff}";
    }

    private static IReadOnlyList<WidgetPhrase.PhraseExec> AssembleChosenBannerExecutions(
        WidgetPhrase mark,
        ToonStatTab tab,
        Func<ToonSheet> blob,
        int[] attrSel,
        int[] aptitudeSel)
    {
        Vector4 Tint(int ordinal) =>
            ordinal >= 0 && ordinal < mark.TypefaceTintSwatch.Count
                ? mark.TypefaceTintSwatch[ordinal]
                : ordinal switch
                {
                    1 => CanonBuffGreen,
                    2 => CanonDebuffRed,
                    3 => CanonVitaeBlue,
                    _ => Vector4.One,
                };

        if (tab == ToonStatTab.Skills)
        {
            ToonSkill? aptitude = AptitudeAtReadoutOrdinal(blob(), aptitudeSel[0]);
            if (aptitude is null)
                return [new("Select a Skill to Improve", Body)];
            if (aptitude.AdvancementClass < ToonSkillAdvancementClass.Trained)
                return [new(aptitude.Name, Tint(0))];

            List<WidgetPhrase.PhraseExec> executions = new List<WidgetPhrase.PhraseExec>
            {
                new($"{aptitude.Name}: {aptitude.CurrentLevel}", Tint(0)),
            };
            if (aptitude.VitaeModifier < 0)
                executions.Add(new(ComposeVitaeDiff(aptitude.VitaeModifier), Tint(3)));
            int buffDiff = FetchAptitudeBuffSoleDiff(aptitude);
            if (buffDiff is not 0)
                executions.Add(new(
                    ComposeBuffDiff(buffDiff),
                    Tint(buffDiff > 0 ? 1 : 2)));
            return executions;
        }

        if (attrSel[0] < 0)
            return [new("Select an Attribute to Improve", Body)];

        var sheet = blob();
        List<WidgetPhrase.PhraseExec> attrExecutions = new List<WidgetPhrase.PhraseExec>
        {
            new(
                $"{FetchRankLabel(attrSel[0])}: {FetchRankValString(sheet, attrSel[0])}",
                Tint(0)),
        };
        int diff = FetchAttrDiff(sheet, attrSel[0]);
        if (diff is not 0)
            attrExecutions.Add(new(
                ComposeBuffDiff(diff),
                Tint(diff > 0 ? 1 : 2)));
        return attrExecutions;
    }

    private static void AssignCompatibilityMooringsAllByIdent(
        WidgetElem joint,
        uint markIdent,
        MooringRims moorings)
    {
        if (joint is WidgetDatElement element
            && element.ElementId == markIdent
            && joint.ArrangementRule is null)

            joint.Moorings = moorings;
        foreach (var descendant in joint.Children)
            AssignCompatibilityMooringsAllByIdent(descendant, markIdent, moorings);
    }

    // Depth-first search of joint and its descendants
    private static WidgetElem? SeekInSubtree(WidgetElem joint, Func<WidgetElem, bool> predicate)
    {
        if (predicate(joint)) return joint;
        foreach (var descendant in joint.Children)
        {
            WidgetElem? located = SeekInSubtree(descendant, predicate);
            if (located is not null) return located;
        }
        return null;
    }

    private static bool HasDatElemIdent(WidgetElem elem, uint ident)
        => DatElemIdent(elem) == ident;

    private static uint DatElemIdent(WidgetElem elem)
    {
        return elem.DatElemIdent is not 0u
            ? elem.DatElemIdent
            : elem switch
            {
                WidgetDatElement datElem => datElem.ElementId,
                WidgetBtn btn => btn.ElementId,
                WidgetGauge gauge => gauge.ElementId,
                WidgetPhrase phrase => phrase.ElementId,
                _ => 0u,
            };
    }

    private static WidgetElem? SeekElemByDatIdent(ImportedArrangement arrangement, WidgetElem? ambit, uint ident)
    {
        if (ambit is not null)
        {
            WidgetElem? scoped = SeekInSubtree(ambit, elem => HasDatElemIdent(elem, ident));
            if (scoped is not null)
                return scoped;
        }

        return arrangement.SeekElem(ident);
    }

    private static WidgetPhrase? SeekPhraseByDatIdent(ImportedArrangement arrangement, WidgetElem? ambit, uint ident)
        => SeekElemByDatIdent(arrangement, ambit, ident) as WidgetPhrase;

    private static WidgetElem? SeekStraightDescendantByIdent(WidgetElem? trunk, uint ident)
    {
        if (trunk is null) return null;
        foreach (var descendant in trunk.Children)
        {
            if (DatElemIdent(descendant) == ident)
                return descendant;
        }
        return null;
    }

    private static void GatherElemsByDatIdent(WidgetElem joint, uint ident, List<WidgetElem> outcome)
    {
        if (DatElemIdent(joint) == ident)
            outcome.Add(joint);
        foreach (var descendant in joint.Children)
            GatherElemsByDatIdent(descendant, ident, outcome);
    }

    private static void Label(ImportedArrangement arrangement, uint ident, WidgetDatFont? datTypeface, Vector4 tint, Func<string> phrase)
        => Label(arrangement, null, ident, datTypeface, tint, phrase);

    private static void Label(ImportedArrangement arrangement, WidgetElem? ambit, uint ident, WidgetDatFont? datTypeface, Vector4 tint, Func<string> phrase)
    {
        if (SeekPhraseByDatIdent(arrangement, ambit, ident) is WidgetPhrase t)
        {
            if (datTypeface is not null) t.DatFont = datTypeface;
            t.Centered = true;
            t.OneLine = true;
            t.ClickThrough = true;
            t.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase(), tint) };
        }
    }

    private static string ComposeXp(long val) => val.ToString("N0", CultureInfo.InvariantCulture);

    private static void CaptionAuthoredTint(ImportedArrangement arrangement, WidgetElem? ambit, uint ident, WidgetDatFont? datTypeface, Func<string> phrase)
    {
        if (SeekPhraseByDatIdent(arrangement, ambit, ident) is WidgetPhrase t)
        {
            if (datTypeface is not null) t.DatFont = datTypeface;
            t.Centered = true;
            t.OneLine = true;
            t.ClickThrough = true;
            t.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase(), t.DefaultTint) };
        }
    }

    private static void CaptionTwoStroke(ImportedArrangement arrangement, uint ident, WidgetDatFont? datTypeface, Vector4 tint,
        string line1, string line2)
    {
        CaptionTwoStroke(arrangement, null, ident, datTypeface, tint, line1, line2);
    }

    private static void CaptionTwoStroke(ImportedArrangement arrangement, WidgetElem? ambit, uint ident, WidgetDatFont? datTypeface, Vector4 tint,
        string line1, string line2)
    {
        if (SeekPhraseByDatIdent(arrangement, ambit, ident) is WidgetPhrase text)
        {
            if (datTypeface is not null) text.DatFont = datTypeface;
            text.Centered = false;
            text.RightAligned = false;
            text.ClickThrough = true;
            text.Padding = 1f;
            text.StrokesSupplier = () => new[]
            {
                new WidgetPhrase.Line(line1, tint),
                new WidgetPhrase.Line(line2, tint),
            };
        }
    }

    private static void CaptionLeft(ImportedArrangement arrangement, uint ident, WidgetDatFont? datTypeface, Vector4 tint, Func<string> phrase)
        => CaptionLeft(arrangement, null, ident, datTypeface, tint, phrase);

    private static void CaptionLeft(ImportedArrangement arrangement, WidgetElem? ambit, uint ident, WidgetDatFont? datTypeface, Vector4 tint, Func<string> phrase)
    {
        if (SeekPhraseByDatIdent(arrangement, ambit, ident) is WidgetPhrase t)
        {
            if (datTypeface is not null) t.DatFont = datTypeface;
            t.Centered = false;
            t.RightAligned = false;
            t.ClickThrough = true;
            t.Padding = 0f;
            t.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase(), tint) };
        }
    }

    private static void CaptionRight(
        ImportedArrangement arrangement,
        WidgetElem? ambit,
        uint ident,
        WidgetDatFont? datTypeface,
        Vector4 tint,
        Func<string> phrase)
    {
        if (SeekPhraseByDatIdent(arrangement, ambit, ident) is WidgetPhrase t)
        {
            if (datTypeface is not null) t.DatFont = datTypeface;
            t.Centered = false;
            t.RightAligned = true;
            t.OneLine = true;
            t.ClickThrough = true;
            t.Padding = 0f;
            t.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase(), tint) };
        }
    }

    private static void CaptionSupplier(WidgetPhrase? t, WidgetDatFont? datTypeface, Vector4 tint, Func<string> phrase)
    {
        if (t is null) return;
        if (datTypeface is not null) t.DatFont = datTypeface;
        t.Centered = false;
        t.RightAligned = false;
        t.ClickThrough = true;
        t.Padding = 0f;
        t.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase(), tint) };
    }

    private static void GatherBtnsByIdent(
        WidgetElem joint,
        uint markIdent,
        List<WidgetBtn> outcome,
        ImportedArrangement arrangement)
    {
        _ = arrangement;

        HashSet<WidgetBtn> observed = new HashSet<WidgetBtn>(ReferenceEqualityComparer.Instance);
        GatherMatchingBtns(joint, markIdent, observed, outcome);
    }

    private static void GatherMatchingBtns(
        WidgetElem joint,
        uint markIdent,
        HashSet<WidgetBtn> observed,
        List<WidgetBtn> outcome)
    {
        if (joint is WidgetBtn btn && btn.ElementId == markIdent && observed.Add(btn))

            outcome.Add(btn);
        foreach (var descendant in joint.Children)
            GatherMatchingBtns(descendant, markIdent, observed, outcome);
    }
}

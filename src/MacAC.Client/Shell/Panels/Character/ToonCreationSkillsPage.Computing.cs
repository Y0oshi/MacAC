using System.Globalization;
using System.Numerics;
using System.Text;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationSkillsPage
{

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        foreach (AptitudeRank rank in _ranks)
        {
            DisposeLoop(rank);
        }
        _ranks.Clear();
        _roster?.Flush();
        _roster?.TemplateResolver = null;
    }

    private void DisposeLoop(AptitudeRank rank)
    {
        rank.UpButton?.OnClick = null;
        rank.DownButton?.OnClick = null;
        if (rank.Root is WidgetDatElement datRank) datRank.OnClick = null;
    }

    internal ToonCreationSkillsPage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings,
        Func<uint, uint, WidgetElem?> blueprintLocator)
    {
        _bindings = mappings;
        _roster = WidgetElem.SeekDescendant(sheetTrunk, 0x100003F7u) as WidgetBlueprintRosterBbox;
        _roster?.TemplateResolver = blueprintLocator;

        if (_roster is not null
            && _roster.ScrollbarElementId is not 0
            && WidgetElem.SeekDescendant(sheetTrunk, _roster.ScrollbarElementId) is WidgetScroller scroller)

            scroller.Model = _roster.Scroll;

        _credits = WidgetElem.SeekDescendant(sheetTrunk, 0x100003F9u) as WidgetBtn;
        _detailsBanner = WidgetElem.SeekDescendant(sheetTrunk, 0x100003FBu) as WidgetPhrase;
        _detailsPhrase = WidgetElem.SeekDescendant(sheetTrunk, 0x100003FCu) as WidgetPhrase;

        if (_detailsPhrase is { } clampedDetailsPhrase
            && WidgetElem.SeekDescendant(sheetTrunk, DetailsBboxCycleElemIdent) is { } cycle)
        {
            float cycleBottom = cycle.Top + cycle.Height;
            float paneBottom = clampedDetailsPhrase.Top + clampedDetailsPhrase.Height;
            if (cycleBottom < paneBottom)
                clampedDetailsPhrase.Height = cycleBottom - clampedDetailsPhrase.Top;
        }
    }

    internal void Refresh(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        bool lineageAltered = !_ranksBuilt || _previousLineageIdent != capture.HeritageId;
        bool binsAltered = !lineageAltered && AnyRankBinAltered(lens);
        if (lineageAltered || binsAltered)
        {
            uint? preservedAptitudeIdent = lineageAltered ? null : _chosenAptitudeIdent;
            ReassembleRanks(lens, capture.HeritageId);
            _previousLineageIdent = capture.HeritageId;
            _ranksBuilt = true;
            if (preservedAptitudeIdent is { } aptitudeIdent)
            {
                foreach (AptitudeRank contender in _ranks)
                {
                    if (contender.SkillId != aptitudeIdent)
                        continue;
                    _chosenAptitudeIdent = aptitudeIdent;
                    ImposePickHighlight();
                    break;
                }
            }
        }

        foreach (AptitudeRank rank in _ranks)
            RenewRankVals(rank, lens, capture);

        RenewDetailsBbox(lens, capture);

        if (_credits is { } credits)
            credits.ValCaption = capture.RemainingSkillCredits.ToString(CultureInfo.InvariantCulture);
    }
    private static AptitudeBin CalculateBin(GenesisSkillTrack tier, uint lowerTier)
    {
        return tier switch
        {
            GenesisSkillTrack.Specialized => AptitudeBin.Specialized,
            GenesisSkillTrack.Trained => AptitudeBin.Trained,
            _ => lowerTier <= 1 ? AptitudeBin.UseableUntrained : AptitudeBin.UnuseableUntrained,
        };
    }

    private bool AnyRankBinAltered(ISimToonGenesisLens lens)
    {
        foreach (AptitudeRank rank in _ranks)
        {
            var tier = lens.GetSkillLevel(rank.SkillId);
            uint lowerTier = lens.Options.TryFetchAptitudeSpecifics(rank.SkillId, out GenesisSkillDetail specifics)
                ? specifics.MinLevel
                : 1u; // Unknown detail (missing global SkillTable entry) defaults to useable - the least surprising fallback.
            if (CalculateBin(tier, lowerTier) != rank.Bucket)
                return true;
        }
        return false;
    }

    private void ReassembleRanks(ISimToonGenesisLens lens, uint lineageIdent)
    {
        foreach (AptitudeRank rank in _ranks)
        {
            ReassembleRanksLoop(rank);
        }
        _ranks.Clear();
        _roster?.Flush();

        _chosenAptitudeIdent = null;
        WipeDetailsBbox();

        if (_roster is null
            || _roster.Templates.Count < 2
            || _roster.TemplateResolver is null
            || !lens.Options.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage))

            return;

        var byBin = new Dictionary<AptitudeBin, List<(uint SkillId, string Name)>>(4)
        {
            [AptitudeBin.Specialized] = [],
            [AptitudeBin.Trained] = [],
            [AptitudeBin.UseableUntrained] = [],
            [AptitudeBin.UnuseableUntrained] = [],
        };
        for (uint aptitudeIdent = 1; aptitudeIdent < GenesisSkillTrackSet.SlotCount; ++aptitudeIdent)
        {
            if (!IsCostable(lineage, lens.Options, aptitudeIdent))
                continue;
            var tier = lens.GetSkillLevel(aptitudeIdent);
            uint lowerTier = lens.Options.TryFetchAptitudeSpecifics(aptitudeIdent, out GenesisSkillDetail specifics)
                ? specifics.MinLevel
                : 1u;
            string label = GearAssayTextComposer.AptitudeLabel((int)aptitudeIdent);
            byBin[CalculateBin(tier, lowerTier)].Add((aptitudeIdent, label));
        }
        foreach (List<(uint SkillId, string Name)> binAptitudes in byBin.Values)
            binAptitudes.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        if (_roster.Templates.Count < 1)
            return;
        var preambleBlueprint = _roster.Templates[0];
        var rankBlueprint = _roster.Templates[1];

        foreach ((AptitudeBin bin, string stringTag) in BinOrdering)
        {
            AssemblePreambleRank(preambleBlueprint, stringTag);
            foreach ((uint aptitudeIdent, _) in byBin[bin])
                AssembleAptitudeRank(rankBlueprint, aptitudeIdent, bin);
        }
    }

    private void ReassembleRanksLoop(AptitudeRank rank)
    {
        rank.UpButton?.OnClick = null;
        rank.DownButton?.OnClick = null;
        if (rank.Root is WidgetDatElement datTrunk) datTrunk.OnClick = null;
    }

    private void AssemblePreambleRank(WidgetTemplateListEntry blueprint, string stringTag)
    {
        if (_roster!.TemplateResolver!(blueprint.TemplateLayoutId, blueprint.TemplateElementId) is not { } preambleTrunk)
            return;
        _roster.AppendPrebuiltRank(preambleTrunk);
        if (WidgetElem.SeekDescendant(preambleTrunk, PreambleLegendElemIdent) is WidgetBtn legend
            && _bindings.ResolveText?.Invoke(stringTag) is { } phrase)

            legend.Label = phrase;
    }

    private void AssembleAptitudeRank(WidgetTemplateListEntry blueprint, uint aptitudeIdent, AptitudeBin bin)
    {
        if (_roster!.TemplateResolver!(blueprint.TemplateLayoutId, blueprint.TemplateElementId) is not { } rankTrunk)
            return;

        _roster.AppendPrebuiltRank(rankTrunk);

        WidgetPhrase? labelPhrase = WidgetElem.SeekDescendant(rankTrunk, RankLabelPhraseIdent) as WidgetPhrase;
        if (labelPhrase is not null)
            AssignStroke(labelPhrase, GearAssayTextComposer.AptitudeLabel((int)aptitudeIdent));
        Vector4 unselectedTint = labelPhrase?.DefaultTint ?? Vector4.One;
        WidgetPhrase? tierPhrase = WidgetElem.SeekDescendant(rankTrunk, RankTierPhraseIdent) as WidgetPhrase;
        WidgetPhrase? upPricePhrase = WidgetElem.SeekDescendant(rankTrunk, RankUpPricePhraseIdent) as WidgetPhrase;
        WidgetPhrase? downPricePhrase = WidgetElem.SeekDescendant(rankTrunk, RankDownPricePhraseIdent) as WidgetPhrase;
        WidgetBtn? upBtn = WidgetElem.SeekDescendant(rankTrunk, RankUpBtnIdent) as WidgetBtn;
        WidgetBtn? downBtn = WidgetElem.SeekDescendant(rankTrunk, RankDownBtnIdent) as WidgetBtn;

        uint grabbedAptitudeIdent = aptitudeIdent;
        upBtn?.OnClick = () => { Advance(grabbedAptitudeIdent); SelectRow(grabbedAptitudeIdent); };
        downBtn?.OnClick = () => { Retreat(grabbedAptitudeIdent); SelectRow(grabbedAptitudeIdent); };

        if (rankTrunk is WidgetDatElement datRank)
        {
            datRank.ClickThrough = false;
            datRank.OnClick = () => SelectRow(grabbedAptitudeIdent);
        }

        _ranks.Add(new AptitudeRank(
            rankTrunk, aptitudeIdent, bin, labelPhrase, tierPhrase, upPricePhrase, downPricePhrase,
            upBtn, downBtn, unselectedTint));
    }

    private void RenewRankVals(
        AptitudeRank rank,
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        var tier = lens.GetSkillLevel(rank.SkillId);
        (int trainedPrice, int specializedPrice) = FetchPrices(lens, capture.HeritageId, rank.SkillId);
        uint score = _bindings.GetSkillScore?.Invoke(rank.SkillId, capture.Attributes, tier) ?? 0u;

        if (rank.LevelText is { } tierPhrase)
            AssignStroke(tierPhrase, score.ToString(CultureInfo.InvariantCulture));

        string upPricePhrase;
        string downPricePhrase;
        bool upTurnedOn;
        bool downTurnedOn;
        switch (tier)
        {
            case GenesisSkillTrack.Specialized:
                upPricePhrase = "0";
                downPricePhrase = (specializedPrice - trainedPrice).ToString(CultureInfo.InvariantCulture);
                upTurnedOn = false;
                downTurnedOn = specializedPrice != 0;
                break;
            case GenesisSkillTrack.Trained:
                upPricePhrase = ComposeGatedPrice(specializedPrice - trainedPrice);
                downPricePhrase = trainedPrice.ToString(CultureInfo.InvariantCulture);
                upTurnedOn = capture.RemainingSkillCredits >= specializedPrice - trainedPrice;
                downTurnedOn = trainedPrice != 0;
                break;
            default:
                upPricePhrase = ComposeGatedPrice(trainedPrice);
                downPricePhrase = "0";
                upTurnedOn = capture.RemainingSkillCredits >= trainedPrice;
                downTurnedOn = false;
                break;
        }

        RenewRankValsRest(rank, upPricePhrase, downPricePhrase, upTurnedOn, downTurnedOn);
    }

    private void RenewRankValsRest(AptitudeRank rank, string upPricePhrase, string downPricePhrase, bool upTurnedOn, bool downTurnedOn)
    {
        if (rank.UpCostText is { } upPricePhraseWidget)
            AssignStroke(upPricePhraseWidget, upPricePhrase);
        if (rank.DownCostText is { } downPricePhraseWidget)
            AssignStroke(downPricePhraseWidget, downPricePhrase);
        rank.UpButton?.TrySetCanonPhase(upTurnedOn ? ArrowTurnedOnPhaseIdent : ArrowGhostedPhaseIdent);
        rank.DownButton?.TrySetCanonPhase(downTurnedOn ? ArrowTurnedOnPhaseIdent : ArrowGhostedPhaseIdent);
    }

    private static string ComposeGatedPrice(int price)
    {
        return price < 999 ? price.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static void AssignStroke(WidgetPhrase phrase, string substance)
    {
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(substance, phrase.DefaultTint)];
    }

    // Same dictionary-presence gate as SimToonGenesisLedger.TryGetSkillCost - heritage list first,
    // global SkillTable fallback
    private static bool IsCostable(
        GenesisHeritageOptions lineage,
        GenesisOptions knobs,
        uint aptitudeIdent)
    {
        return lineage.SkillCostsBySkillId.ContainsKey(aptitudeIdent)
        || knobs.GlobalSkillCostsBySkillId.ContainsKey(aptitudeIdent);
    }

    private static (int Trained, int Specialized) FetchPrices(
        ISimToonGenesisLens lens,
        uint lineageIdent,
        uint aptitudeIdent)
    {
        if (lens.Options.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage))
        {
            if (lineage.SkillCostsBySkillId.TryGetValue(aptitudeIdent, out GenesisSkillPrice price))
                return (price.NormalCost, price.PrimaryCost);
        }
        if (lens.Options.GlobalSkillCostsBySkillId.TryGetValue(aptitudeIdent, out GenesisSkillPrice global))
            return (global.NormalCost, global.PrimaryCost);
        return (0, 0);
    }

    private void Advance(uint aptitudeIdent)
    {
        if (_destroyed)
            return;
        GenesisSkillTrack tier = _bindings.View()?.GetSkillLevel(aptitudeIdent)
            ?? GenesisSkillTrack.Inactive;
        if (tier is GenesisSkillTrack.Inactive or GenesisSkillTrack.Untrained)
            _bindings.TrainSkill(aptitudeIdent);
        else if (tier == GenesisSkillTrack.Trained)
            _bindings.SpecializeSkill(aptitudeIdent);
    }

    private void Retreat(uint aptitudeIdent)
    {
        if (_destroyed)
            return;
        GenesisSkillTrack tier = _bindings.View()?.GetSkillLevel(aptitudeIdent)
            ?? GenesisSkillTrack.Inactive;
        if (tier == GenesisSkillTrack.Specialized)
            _bindings.TrainSkill(aptitudeIdent);
        else if (tier == GenesisSkillTrack.Trained)
            _bindings.UntrainSkill(aptitudeIdent);
    }

    private void SelectRow(uint aptitudeIdent)
    {
        if (_destroyed)
            return;
        _chosenAptitudeIdent = aptitudeIdent;
        ImposePickHighlight();
        if (_bindings.View() is { } lens)
            RenewDetailsBbox(lens, lens.Snapshot);
    }

    private void ImposePickHighlight()
    {
        foreach (AptitudeRank rank in _ranks)
        {
            if (rank.NameText is { } labelPhrase)
                labelPhrase.DefaultTint = rank.SkillId == _chosenAptitudeIdent ? ChosenLabelTint : rank.UnselectedNameColor;
        }
    }

    private void RenewDetailsBbox(ISimToonGenesisLens lens, SimToonGenesisCapture capture)
    {
        if (_chosenAptitudeIdent is not { } aptitudeIdent)
        {
            WipeDetailsBbox();
            return;
        }

        var tier = lens.GetSkillLevel(aptitudeIdent);
        uint score = _bindings.GetSkillScore?.Invoke(aptitudeIdent, capture.Attributes, tier) ?? 0u;
        string label = GearAssayTextComposer.AptitudeLabel((int)aptitudeIdent);

        if (_detailsBanner is { } banner)
            AssignStroke(banner, $"{label} ({score.ToString(CultureInfo.InvariantCulture)})");

        if (_detailsPhrase is { } phrase)
        {
            string bonus = tier switch
            {
                GenesisSkillTrack.Trained => "Training Bonus  +5",
                GenesisSkillTrack.Specialized => "Specialization Bonus  +10",
                _ => string.Empty,
            };

            bool hasSpecifics = lens.Options.TryFetchAptitudeSpecifics(aptitudeIdent, out GenesisSkillDetail specifics);
            List<WidgetPhrase.Line> strokes = new List<WidgetPhrase.Line>();
            if (hasSpecifics && !string.IsNullOrEmpty(specifics.Description))
            {
                strokes.AddRange(DatRichPhrase.Compose(
                    phrase, [new DatRichPhrase.Piece(specifics.Description, phrase.DefaultTint)]));
            }
            if (bonus.Length > 0)
                strokes.Add(new WidgetPhrase.Line(bonus, phrase.DefaultTint));
            if (hasSpecifics)
                strokes.Add(new WidgetPhrase.Line(ConstructEquation(specifics.Formula), phrase.DefaultTint));

            phrase.StrokesSupplier = () => strokes;
        }
    }

    private static string ConstructEquation(GenesisSkillFormula equation)
    {
        bool attribute1Engaged = equation.Attribute1Multiplier >= 1 && equation.Attribute1 is not 0;
        bool attribute2Engaged = equation.Attribute2Multiplier >= 1 && equation.Attribute2 is not 0;

        StringBuilder builder = new StringBuilder("Formula : ");
        if (attribute1Engaged)
        {
            AffixAttrTerm(builder, equation.Attribute1Multiplier, equation.Attribute1);
            if (attribute2Engaged)
                builder.Append(" + ");
        }
        if (attribute2Engaged)
            AffixAttrTerm(builder, equation.Attribute2Multiplier, equation.Attribute2);

        if (equation.Divisor is not 1)
            builder.Append(CultureInfo.InvariantCulture, $" / {equation.Divisor}");
        if (equation.AdditiveBonus is not 0)
            builder.Append(CultureInfo.InvariantCulture, $" +{equation.AdditiveBonus}");
        return builder.ToString();
    }

    private static void AffixAttrTerm(StringBuilder builder, int multiplier, uint attrIdent)
    {
        string label = AttrLabel((GenesisTraitId)attrIdent);
        if (multiplier > 1)
            builder.Append(CultureInfo.InvariantCulture, $"({multiplier} x {label})");
        else
            builder.Append(label);
    }

    private static string AttrLabel(GenesisTraitId ident)
    {
        return ident switch
        {
            GenesisTraitId.Strength => "Strength",
            GenesisTraitId.Endurance => "Endurance",
            GenesisTraitId.Quickness => "Quickness",
            GenesisTraitId.Coordination => "Coordination",
            GenesisTraitId.Focus => "Focus",
            GenesisTraitId.Self => "Self",
            _ => string.Empty,
        };
    }

    private void WipeDetailsBbox()
    {
        if (_detailsBanner is { } banner) AssignStroke(banner, string.Empty);
        if (_detailsPhrase is { } phrase) AssignStroke(phrase, string.Empty);
    }
}

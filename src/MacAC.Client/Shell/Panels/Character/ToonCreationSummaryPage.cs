using System.Globalization;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed class ToonCreationSummaryPage : IDisposable
{
    internal const uint RosterBboxIdent = 0x10000400u;
    internal const uint RollIdent = 0x10000401u;
    internal const uint LabelPhraseIdent = 0x10000402u;
    internal const uint HowToPhraseIdent = 0x10000404u;
    internal const uint ViewRectIdent = 0x10000406u;

    private const uint SingleStrokePhraseIdent = 0x100002F9u;
    private const uint PreamblePhraseIdent = 0x100000FEu;
    private const uint TagPhraseIdent = 0x100002FCu;
    private const uint ValPhraseIdent = 0x100002FDu;

    private const int UpperLabelLen = 32;

    private const uint HowToRollRelativeIdent = 0x100002E7u;

    private static readonly IReadOnlyDictionary<uint, (string Male, string Female)> LabelSuggestionTagsByLineage =
        new Dictionary<uint, (string, string)>
        {
            [(uint)GenesisHeritage.Aluvian] = ("ID_CharGen_AluMaleNames", "ID_CharGen_AluFemaleNames"),
            [(uint)GenesisHeritage.Gharundim] = ("ID_CharGen_GharuMaleNames", "ID_CharGen_GharuFemaleNames"),
            [(uint)GenesisHeritage.Sho] = ("ID_CharGen_ShoMaleNames", "ID_CharGen_ShoFemaleNames"),
            [(uint)GenesisHeritage.Viamontian] = ("ID_CharGen_ViaMaleNames", "ID_CharGen_ViaFemaleNames"),
        };

    private readonly ToonCreationEngineWiring _bindings;
    private readonly CanonPromptMint _popups;
    private readonly string _labelTooLongMsg;
    private readonly WidgetBlueprintRosterBbox? _roster;
    private readonly WidgetField? _labelField;
    private readonly WidgetPhrase? _howToPhrase;
    private string _previousSealedLabel = string.Empty;
    private uint _labelTooLongPopupCtx;
    private bool _destroyed;

    internal IClientChargenPreviewControl? PreviewControl { get; set; }

    internal WidgetViewport? Viewport { get; }

    internal ToonCreationSummaryPage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings,
        CanonPromptMint popups,
        string labelTooLongMsg,
        Func<uint, uint, WidgetElem?> blueprintLocator)
    {
        _bindings = mappings;
        _popups = popups;
        _labelTooLongMsg = labelTooLongMsg;

        _roster = WidgetElem.SeekDescendant(sheetTrunk, RosterBboxIdent) as WidgetBlueprintRosterBbox;
        _roster?.TemplateResolver = blueprintLocator;

        if (_roster is not null)
        {
            uint scrollerElemIdent = _roster.ScrollbarElementId;
            if (scrollerElemIdent is not 0
                && WidgetElem.SeekDescendant(sheetTrunk, scrollerElemIdent) is WidgetScroller overviewRoll)

                overviewRoll.Model = _roster.Scroll;
        }

        _labelField = WidgetElem.SeekDescendant(sheetTrunk, LabelPhraseIdent) as WidgetField;
        if (_labelField is not null)
        {
            _labelField.ToonSift = LabelFeedSift;
            _labelField.OnFocusLost = SealLabelFromField;
            _labelField.OnSubmit = SealLabelFromField;
            _labelField.WipeOnSubmit = false;
            _labelField.CaptureHistory = false;
        }

        Viewport = WidgetElem.SeekDescendant(sheetTrunk, ViewRectIdent) as WidgetViewport;

        _howToPhrase = WidgetElem.SeekDescendant(sheetTrunk, HowToPhraseIdent) as WidgetPhrase;
        if (_howToPhrase is not null
            && WidgetElem.SeekDescendant(_howToPhrase, HowToRollRelativeIdent) is WidgetScroller howToRoll)

            howToRoll.Model = _howToPhrase.Scroll;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        if (_labelField is not null)
        {
            _labelField.OnFocusLost = null;
            _labelField.OnSubmit = null;
        }
        if (_labelTooLongPopupCtx is not 0u)
        {
            uint closing = _labelTooLongPopupCtx;
            _labelTooLongPopupCtx = 0u;
            _popups.ShutPopup(closing);
        }
        _roster?.TemplateResolver = null;
        _roster?.Flush();
        PreviewControl = null;
    }

    internal void Refresh(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        if (_destroyed)
            return;

        if (_labelField is { IsFocused: false } field && field.Text != capture.Name)
        {
            field.AssignPhrase(capture.Name);
            _previousSealedLabel = capture.Name;
        }

        ReassembleListbox(lens, capture);
        ReassemblePreview(lens, capture);
        RenewHowToPhrase(capture);
    }

    private void RenewHowToPhrase(SimToonGenesisCapture capture)
    {
        if (_howToPhrase is null)
            return;

        var locatePhrase = _bindings.ResolveText;
        if (locatePhrase is null)
            return;

        var builder = new System.Text.StringBuilder();
        if (locatePhrase("ID_CharGen_SummaryHowTo") is { } howTo)
            builder.Append(howTo);
        if (LabelSuggestionTagsByLineage.TryGetValue(capture.HeritageId, out (string Male, string Female) tags))
        {
            string tag = capture.GenderKey is 2u ? tags.Female : tags.Male;
            if (locatePhrase(tag) is { } labelTickets)
                builder.Append(labelTickets);
        }
        if (locatePhrase("ID_CharGen_SummaryHowToEnd") is { } howToFinish)
            builder.Append(howToFinish);

        if (builder.Length is 0)
            return;

        string composedPhrase = builder.ToString();
        DatRichPhrase.Piece[] segments = new[] { new DatRichPhrase.Piece(composedPhrase, _howToPhrase.DefaultTint) };
        var composedStrokes = DatRichPhrase.Compose(_howToPhrase, segments);
        _howToPhrase.StrokesSupplier = () => composedStrokes;
    }

    private void SealLabelFromField(string phrase)
    {
        if (_destroyed)
            return;

        if (phrase.Length > UpperLabelLen)
        {
            _labelField?.AssignPhrase(_previousSealedLabel);
            RevealLabelTooLongPopup();
            return;
        }

        _previousSealedLabel = phrase;
        _bindings.SetName?.Invoke(phrase);
    }

    private void RevealLabelTooLongPopup()
    {
        if (_labelTooLongPopupCtx is not 0u)
            return;
        _labelTooLongPopupCtx = _popups.CraftMsg(
            _labelTooLongMsg,
            blob =>
            {
                _ = blob;
                _labelTooLongPopupCtx = 0u;
            });
    }

    private static bool LabelFeedSift(char c) =>
        (c < 0x100 && char.IsAsciiLetter(c)) || c is ' ' or '\'' or '-';

    private void ReassembleListbox(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        if (_roster is null || _roster.Templates.Count < 3)
            return;

        _roster.Flush();

        if (!lens.Options.TryFetchLineage(capture.HeritageId, out GenesisHeritageOptions? lineage))
            return;

        var strokeBlueprint = _roster.Templates[0];
        var preambleBlueprint = _roster.Templates[1];
        var duoBlueprint = _roster.Templates[2];

        AppendStroke(strokeBlueprint, "Profession: " + ProfessionLabel(lineage, capture.Template));
        AppendStroke(strokeBlueprint, "Gender: " + GenderLabel(lineage, capture.GenderKey));
        AppendStroke(strokeBlueprint, "Heritage: " + lineage.Name);
        AppendStroke(strokeBlueprint, "Starting Town: " + StarterAreaLabel(lens.Options, capture.StartArea));

        AppendPreamble(preambleBlueprint, "Attributes");
        var spread = capture.Attributes;
        AppendDuo(duoBlueprint, "Strength", spread.Strength);
        AppendDuo(duoBlueprint, "Endurance", spread.Endurance);
        AppendDuo(duoBlueprint, "Coordination", spread.Coordination);
        AppendDuo(duoBlueprint, "Quickness", spread.Quickness);
        AppendDuo(duoBlueprint, "Focus", spread.Focus);
        AppendDuo(duoBlueprint, "Self", spread.Self);
        AppendDuo(duoBlueprint, "Health", spread.Endurance / 2);
        AppendDuo(duoBlueprint, "Stamina", spread.Endurance);
        AppendDuo(duoBlueprint, "Mana", spread.Self);
        AppendDuo(duoBlueprint, "Skill Credits", capture.RemainingSkillCredits);

        AppendAptitudeBin(preambleBlueprint, duoBlueprint, lens, capture, "Specialized Skills", GenesisSkillTrack.Specialized);
        AppendAptitudeBin(preambleBlueprint, duoBlueprint, lens, capture, "Trained Skills", GenesisSkillTrack.Trained);
    }

    private void AppendStroke(WidgetTemplateListEntry blueprint, string phrase)
    {
        if (LocateBlueprintDescendant(blueprint, SingleStrokePhraseIdent) is { } descendant)
            AssignStroke(descendant, phrase);
    }

    private void AppendPreamble(WidgetTemplateListEntry blueprint, string phrase)
    {
        if (LocateBlueprintDescendant(blueprint, PreamblePhraseIdent) is { } descendant)
            AssignStroke(descendant, phrase);
    }

    private void AppendDuo(WidgetTemplateListEntry blueprint, string tag, int val)
    {
        WidgetElem? rank = LocateBlueprintRank(blueprint);
        if (rank is null)
            return;
        if (WidgetElem.SeekDescendant(rank, TagPhraseIdent) is WidgetPhrase tagPhrase)
            AssignStroke(tagPhrase, tag);
        if (WidgetElem.SeekDescendant(rank, ValPhraseIdent) is WidgetPhrase valPhrase)
            AssignStroke(valPhrase, val.ToString(CultureInfo.InvariantCulture));
    }

    private static void AssignStroke(WidgetPhrase phrase, string substance)
    {
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(substance, phrase.DefaultTint)];
    }

    private WidgetElem? LocateBlueprintRank(WidgetTemplateListEntry blueprint)
    {
        if (_roster is null || _roster.TemplateResolver is null)
            return null;
        WidgetElem? rank = _roster.TemplateResolver(blueprint.TemplateLayoutId, blueprint.TemplateElementId);
        if (rank is null)
            return null;
        _roster.AppendPrebuiltRank(rank);
        return rank;
    }

    private WidgetPhrase? LocateBlueprintDescendant(WidgetTemplateListEntry blueprint, uint descendantIdent)
    {
        WidgetElem? rank = LocateBlueprintRank(blueprint);
        return rank is null ? null : WidgetElem.SeekDescendant(rank, descendantIdent) as WidgetPhrase;
    }

    private void AppendAptitudeBin(
        WidgetTemplateListEntry preambleBlueprint,
        WidgetTemplateListEntry duoBlueprint,
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture,
        string preamble,
        GenesisSkillTrack markClass)
    {
        AppendPreamble(preambleBlueprint, preamble);
        for (uint aptitudeIdent = 1; aptitudeIdent < GenesisSkillTrackSet.SlotCount; ++aptitudeIdent)
        {
            if (lens.GetSkillLevel(aptitudeIdent) != markClass)
                continue;
            uint score = _bindings.GetSkillScore?.Invoke(aptitudeIdent, capture.Attributes, markClass) ?? 0u;
            AppendDuo(duoBlueprint, GearAssayTextComposer.AptitudeLabel((int)aptitudeIdent), (int)score);
        }
    }

    private static string ProfessionLabel(GenesisHeritageOptions lineage, uint blueprint)
    {
        return blueprint != SimToonGenesisCapture.BlueprintUnset
            && blueprint < (uint)lineage.Templates.Count
            ? lineage.Templates[(int)blueprint].Name
            : "None";
    }

    private static string GenderLabel(GenesisHeritageOptions lineage, uint genderTag)
    {
        return lineage.GendersByKey.TryGetValue((int)genderTag, out GenesisSexOptions? gender)
            ? gender.Name
            : "None";
    }

    private static string StarterAreaLabel(GenesisOptions knobs, int beginArea)
    {
        return beginArea >= 0 && beginArea < knobs.StarterAreas.Count
            ? knobs.StarterAreas[beginArea].Name
            : "None";
    }

    private void ReassemblePreview(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        if (PreviewControl is null
            || capture.HeritageId is 0u
            || capture.GenderKey is 0u)

            return;

        var appearance = capture.Appearance;
        GenesisLookChoice pick = new GenesisLookChoice(
            appearance.EyesStrip, appearance.NoseStrip, appearance.MouthStrip,
            appearance.HairStyle, appearance.HairColor, appearance.EyeColor,
            appearance.HeadgearStyle, appearance.HeadgearColor,
            appearance.ShirtStyle, appearance.ShirtColor,
            appearance.TrousersStyle, appearance.TrousersColor,
            appearance.FootwearStyle, appearance.FootwearColor,
            appearance.SkinShade, appearance.HairShade, appearance.HeadgearShade,
            appearance.ShirtShade, appearance.TrousersShade, appearance.FootwearShade);

        PreviewControl.Rebuild(lens.Options, capture.HeritageId, (int)capture.GenderKey, pick);
    }
}

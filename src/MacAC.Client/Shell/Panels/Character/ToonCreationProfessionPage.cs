using System.Globalization;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed class ToonCreationProfessionPage : IDisposable
{
    private static readonly IReadOnlyDictionary<uint, uint> BlueprintByBtnIdent =
        new Dictionary<uint, uint>
        {
            [0x100003D9u] = 0u,
            [0x100003DAu] = 1u, // Bow Hunter
            [0x100003DFu] = 2u, // Swashbuckler
            [0x100003DBu] = 3u,
            [0x100003DCu] = 4u,
            [0x100003DDu] = 5u, // Wayfarer
            [0x100003DEu] = 6u, // Soldier
        };

    private static readonly IReadOnlyDictionary<GenesisTraitId, uint> DialVesselByAttr =
        new Dictionary<GenesisTraitId, uint>
        {
            [GenesisTraitId.Strength] = 0x100003E6u,
            [GenesisTraitId.Endurance] = 0x100003E7u,
            [GenesisTraitId.Coordination] = 0x100003E8u,
            [GenesisTraitId.Quickness] = 0x100003E9u,
            [GenesisTraitId.Focus] = 0x100003EAu,
            [GenesisTraitId.Self] = 0x100003EBu,
        };

    private const uint DialMutexRelativeIdent = 0x100002ECu;
    private const uint DialControlRelativeIdent = 0x100002EEu;
    private const uint DialValRelativeIdent = 0x100002EFu;

    private sealed record DialWidgets(WidgetBtn? Lock, WidgetScroller? Slider, WidgetField? Value);

    private const uint DialLabelRelativeIdent = 0x100002EDu;

    private const uint BlurbPhraseIdent = 0x100003E0u;

    private static readonly IReadOnlyDictionary<uint, uint> BackdropPhaseByBlueprint =
        new Dictionary<uint, uint>
        {
            [0u] = 0x1000002Bu,
            [1u] = 0x1000002Cu, // Bow Hunter
            [2u] = 0x10000031u, // Swashbuckler
            [3u] = 0x1000002Du,
            [4u] = 0x1000002Eu,
            [5u] = 0x1000002Fu, // Wayfarer
            [6u] = 0x10000030u, // Soldier
        };

    private static readonly IReadOnlyDictionary<uint, string> BlurbTagByBlueprint =
        new Dictionary<uint, string>
        {
            [0u] = "ID_CharGen_CustomText",
            [1u] = "ID_CharGen_BowText",
            [2u] = "ID_CharGen_SwashText",
            [3u] = "ID_CharGen_LifeText",
            [4u] = "ID_CharGen_WarText",
            [5u] = "ID_CharGen_WayText",
            [6u] = "ID_CharGen_SoldierText",
        };

    private readonly ToonCreationEngineWiring _bindings;
    private readonly Dictionary<WidgetBtn, uint> _blueprintBtns = [];
    private readonly Dictionary<GenesisTraitId, DialWidgets> _dials = [];
    private readonly WidgetBtn? _onHandVal;
    private readonly WidgetBtn? _healthVal;
    private readonly WidgetBtn? _staminaVal;
    private readonly WidgetBtn? _manaVal;
    private readonly WidgetPhrase? _blurb;
    private readonly WidgetElem? _backdrop;
    private bool _destroyed;

    internal ToonCreationProfessionPage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings)
    {
        _bindings = mappings;

        foreach ((uint btnIdent, uint blueprintOrdinal) in BlueprintByBtnIdent)
        {
            if (WidgetElem.SeekDescendant(sheetTrunk, btnIdent) is not WidgetBtn btn)
                continue;
            _blueprintBtns[btn] = blueprintOrdinal;
            btn.OnClick = () => PickBlueprint(blueprintOrdinal);
        }

        foreach ((GenesisTraitId attr, uint vesselIdent) in DialVesselByAttr)
        {
            if (WidgetElem.SeekDescendant(sheetTrunk, vesselIdent) is not { } vessel)
                continue;

            WidgetBtn? mutexBtn = WidgetElem.SeekDescendant(vessel, DialMutexRelativeIdent) as WidgetBtn;
            WidgetScroller? dial = WidgetElem.SeekDescendant(vessel, DialControlRelativeIdent) as WidgetScroller;
            WidgetField? val = WidgetElem.SeekDescendant(vessel, DialValRelativeIdent) as WidgetField;

            var grabbedAttr = attr;
            mutexBtn?.OnClick = () => FlipMutex(grabbedAttr);
            if (dial is not null)
            {
                dial.Horizontal = true;
                dial.ScalarAltered = scalar => AssignAttrFromScalar(grabbedAttr, scalar);
            }
            if (val is not null)
            {
                val.Editable = true;
                val.ToonSift = char.IsAsciiDigit;
                val.OnSubmit = phrase => AssignAttrFromPhrase(grabbedAttr, phrase);
            }

            if (WidgetElem.SeekDescendant(vessel, DialLabelRelativeIdent) is WidgetBtn labelCaption)
                labelCaption.Label = AttrLabel(attr);

            _dials[attr] = new DialWidgets(mutexBtn, dial, val);
        }

        _onHandVal = WidgetElem.SeekDescendant(sheetTrunk, 0x100003E2u) as WidgetBtn;
        _healthVal = WidgetElem.SeekDescendant(sheetTrunk, 0x100003E3u) as WidgetBtn;
        _staminaVal = WidgetElem.SeekDescendant(sheetTrunk, 0x100003E4u) as WidgetBtn;
        _manaVal = WidgetElem.SeekDescendant(sheetTrunk, 0x100003E5u) as WidgetBtn;

        _blurb = WidgetElem.SeekDescendant(sheetTrunk, BlurbPhraseIdent) as WidgetPhrase;
        _backdrop = WidgetElem.SeekDescendant(sheetTrunk, 0x100003D8u);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        foreach (WidgetBtn btn in _blueprintBtns.Keys)
            btn.OnClick = null;
        _blueprintBtns.Clear();
        foreach (DialWidgets widgets in _dials.Values)
        {
            DisposeLoop(widgets);
        }
        _dials.Clear();
    }

    private void DisposeLoop(DialWidgets widgets)
    {
        if (widgets.Lock is { } mutexBtn)
            mutexBtn.OnClick = null;
        if (widgets.Slider is { } dial)
            dial.ScalarAltered = null;
        if (widgets.Value is { } valField)
            valField.OnSubmit = null;
    }

    internal void Refresh(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        _ = lens;
        foreach ((WidgetBtn btn, uint blueprintOrdinal) in _blueprintBtns)
            btn.Selected = blueprintOrdinal == capture.Template;

        foreach ((GenesisTraitId attr, DialWidgets widgets) in _dials)
        {
            int val = FetchAttr(capture.Attributes, attr);
            float scalar = val / 100f;
            widgets.Slider?.AssignScalarLocus(scalar);
            widgets.Value?.AssignPhrase(val.ToString(CultureInfo.InvariantCulture));
            if (widgets.Lock is { } mutexBtn)
                mutexBtn.Selected = capture.IsAttrBolted(attr);
        }

        AssignReadout(_onHandVal, capture.RemainingAttributeCredits);
        int endurance = capture.Attributes.Endurance;
        AssignReadout(_healthVal, endurance / 2);
        AssignReadout(_staminaVal, endurance);
        AssignReadout(_manaVal, capture.Attributes.Self);

        if (_backdrop is IWidgetDatStateful backdropStateful
            && BackdropPhaseByBlueprint.TryGetValue(capture.Template, out uint backdropPhase))

            backdropStateful.TrySetCanonPhase(backdropPhase);

        if (_blurb is not null
            && BlurbTagByBlueprint.TryGetValue(capture.Template, out string? tag))
        {
            string? phrase = _bindings.ResolveText?.Invoke(tag);
            DatRichPhrase.Piece[] segments = new[] { new DatRichPhrase.Piece(phrase, _blurb.DefaultTint) };
            var composed = DatRichPhrase.Compose(_blurb, segments);
            _blurb.StrokesSupplier = () => composed;
        }
    }

    internal void Randomize(SimToonGenesisCapture capture)
    {
        var lens = _bindings.View();
        if (lens is null
            || !lens.Options.TryFetchLineage(capture.HeritageId, out GenesisHeritageOptions? lineage)
            || lineage.Templates.Count is 0)

            return;
        PickBlueprint((uint)Random.Shared.Next(lineage.Templates.Count));
    }

    private static int FetchAttr(GenesisAttributeSpread vals, GenesisTraitId ident)
    {
        return ident switch
        {
            GenesisTraitId.Strength => vals.Strength,
            GenesisTraitId.Endurance => vals.Endurance,
            GenesisTraitId.Quickness => vals.Quickness,
            GenesisTraitId.Coordination => vals.Coordination,
            GenesisTraitId.Focus => vals.Focus,
            GenesisTraitId.Self => vals.Self,
            _ => 0,
        };
    }

    private static void AssignReadout(WidgetBtn? readout, int val)
    {
        if (readout is null)
            return;
        readout.ValCaption = val.ToString(CultureInfo.InvariantCulture);
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

    private void PickBlueprint(uint blueprintOrdinal)
    {
        if (_destroyed)
            return;
        _bindings.SelectTemplate(blueprintOrdinal);
    }

    private void AssignAttrFromScalar(GenesisTraitId attr, float scalar)
    {
        if (_destroyed)
            return;
        int val = Math.Max(GenesisAttributeRules.AttrLower, (int)(scalar * 100f));
        _bindings.SetAttribute(attr, val);
    }

    private void AssignAttrFromPhrase(GenesisTraitId attr, string phrase)
    {
        if (_destroyed)
            return;
        if (int.TryParse(phrase, NumberStyles.None, CultureInfo.InvariantCulture, out int val))
            _bindings.SetAttribute(attr, val);
    }

    private void FlipMutex(GenesisTraitId attr)
    {
        if (_destroyed)
            return;
        var capture = _bindings.View()?.Snapshot;
        bool currentlyBolted = capture?.IsAttrBolted(attr) ?? false;
        _bindings.SetAttributeLock(attr, !currentlyBolted);
    }
}

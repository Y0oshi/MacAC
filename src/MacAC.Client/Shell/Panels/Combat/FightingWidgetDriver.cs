using MacAC.Mechanics.Fighting;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed class FightingWidgetDriver : IRetainedPaneDriver
{
    public sealed record BindingsUnit(
        Func<CharacterOptionId, bool> CurrentValue,
        Action<CharacterOptionId, bool> SetOption);

    public const uint LayoutId = 0x21000073u;
    public const uint BasicBoardIdent = 0x1000005Cu;
    public const uint SpellcastingBoardIdent = 0x10000061u;
    public const uint AdvancedBoardIdent = SpellcastingBoardIdent;
    public const uint StrengthControlIdent = 0x1000004Fu;
    public const uint PaceCaptionIdent = 0x10000051u;
    public const uint StrengthCaptionIdent = 0x10000052u;
    public const uint RepeatAssaultsIdent = 0x10000053u;
    public const uint AutoMarkIdent = 0x10000054u;
    public const uint KeepInLensIdent = 0x10000055u;
    public const uint HiBtnIdent = 0x10000057u;
    public const uint MediumBtnIdent = 0x10000058u;
    public const uint LoBtnIdent = 0x10000059u;

    public const uint MeleePhase = 0x10000003u;
    public const uint MissilePhase = 0x10000004u;

    private readonly WidgetElem _trunk;
    private readonly WidgetElem _basicBoard;
    private readonly WidgetElem _spellcastingBoard;
    private readonly WidgetScroller _strengthControl;
    private readonly WidgetBtn _hi;
    private readonly WidgetBtn _medium;
    private readonly WidgetBtn _lo;
    private readonly WidgetBtn _repeatAssaults;
    private readonly WidgetBtn _autoMark;
    private readonly WidgetBtn _keepInLens;
    private readonly FightingPhase _fighting;
    private readonly SimFightingAttackLedger _assaults;
    private readonly BindingsUnit _bindings;
    private readonly FightingWidgetLabels _captions;
    private readonly WidgetPhrase? _strengthCaption;
    private readonly Action<bool> _setPaneShown;
    private bool _destroyed;

    private FightingWidgetDriver(
        ImportedArrangement arrangement,
        WidgetElem basicBoard,
        WidgetElem spellcastingBoard,
        WidgetScroller strengthControl,
        WidgetBtn hi,
        WidgetBtn medium,
        WidgetBtn lo,
        WidgetBtn repeatAssaults,
        WidgetBtn autoMark,
        WidgetBtn keepInLens,
        FightingPhase fighting,
        SimFightingAttackLedger assaults,
        BindingsUnit mappings,
        FightingWidgetLabels captions,
        Action<bool> setPaneShown)
    {
        _trunk = arrangement.Root;
        _basicBoard = basicBoard;
        _spellcastingBoard = spellcastingBoard;
        _strengthControl = strengthControl;
        _hi = hi;
        _medium = medium;
        _lo = lo;
        _repeatAssaults = repeatAssaults;
        _autoMark = autoMark;
        _keepInLens = keepInLens;
        _fighting = fighting;
        _assaults = assaults;
        _bindings = mappings;
        _captions = captions;
        _setPaneShown = setPaneShown;

        _basicBoard.Visible = true;
        _spellcastingBoard.Visible = false;

        _strengthControl.AssignScalarLocus(_assaults.WantedStrength);
        _strengthControl.ScalarAltered = _assaults.AssignWantedStrength;
        _strengthControl.ScalarPopulate = () => _assaults.StrengthBarTier;

        AttachAssaultBtn(_hi, AssaultElevation.High);
        AttachAssaultBtn(_medium, AssaultElevation.Medium);
        AttachAssaultBtn(_lo, AssaultElevation.Low);

        _hi.Label = captions.High;
        _medium.Label = captions.Medium;
        _lo.Label = captions.Low;
        _repeatAssaults.Label = captions.RepeatAttacks;
        _autoMark.Label = captions.AutoTarget;
        _keepInLens.Label = captions.KeepInView;
        WidgetPhrase? paceCaption = arrangement.SeekElem(PaceCaptionIdent) as WidgetPhrase;
        _strengthCaption = arrangement.SeekElem(StrengthCaptionIdent) as WidgetPhrase;
        AssignStaticPhrase(paceCaption, captions.Speed, rightAligned: false);
        AssignStaticPhrase(_strengthCaption, captions.Power, rightAligned: true);

        _repeatAssaults.OnClick = () =>
            _bindings.SetOption(CharacterOptionId.AutoRepeatAttack, _repeatAssaults.Selected);
        _autoMark.OnClick = () =>
            _bindings.SetOption(CharacterOptionId.AutoTarget, _autoMark.Selected);
        _keepInLens.OnClick = () =>
            _bindings.SetOption(CharacterOptionId.ViewCombatTarget, _keepInLens.Selected);

        _fighting.CombatModeChanged += OnFightingMannerAltered;
        _assaults.StateChanged += OnAssaultPhaseAltered;
        SynchronizeControls();
    }

    public static FightingWidgetDriver? Bind(
        ImportedArrangement arrangement,
        FightingPhase fighting,
        SimFightingAttackLedger assaults,
        BindingsUnit mappings,
        FightingWidgetLabels captions,
        Action<bool> setPaneShown)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(fighting);
        ArgumentNullException.ThrowIfNull(assaults);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(captions);
        ArgumentNullException.ThrowIfNull(setPaneShown);

        return arrangement.SeekElem(BasicBoardIdent) is not { } basic
            || arrangement.SeekElem(SpellcastingBoardIdent) is not { } spellcasting
            || arrangement.SeekElem(StrengthControlIdent) is not WidgetScroller strength
            || arrangement.SeekElem(HiBtnIdent) is not WidgetBtn hi
            || arrangement.SeekElem(MediumBtnIdent) is not WidgetBtn medium
            || arrangement.SeekElem(LoBtnIdent) is not WidgetBtn lo
            || arrangement.SeekElem(RepeatAssaultsIdent) is not WidgetBtn repeatAssaults
            || arrangement.SeekElem(AutoMarkIdent) is not WidgetBtn autoMark
            || arrangement.SeekElem(KeepInLensIdent) is not WidgetBtn keepInLens
            ? null
            : new FightingWidgetDriver(
            arrangement, basic, spellcasting, strength, hi, medium, lo,
            repeatAssaults, autoMark, keepInLens,
            fighting, assaults, mappings, captions, setPaneShown);
    }

    public void SynchronizeVis() => OnFightingMannerAltered(_fighting.LatestMode);

    public void OnSrvKnobsSeeded() => SynchronizeControls();

    public void OnShown() => SynchronizeControls();

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _fighting.CombatModeChanged -= OnFightingMannerAltered;
        _assaults.StateChanged -= OnAssaultPhaseAltered;
        _strengthControl.ScalarAltered = null;
        _strengthControl.ScalarPopulate = () => null;
        _hi.OnPressed = null;
        _hi.OnReleased = null;
        _medium.OnPressed = null;
        _medium.OnReleased = null;
        _lo.OnPressed = null;
        _lo.OnReleased = null;
        _repeatAssaults.OnClick = null;
        _autoMark.OnClick = null;
        _keepInLens.OnClick = null;
    }

    private void AttachAssaultBtn(WidgetBtn btn, AssaultElevation height)
    {
        btn.OnPressed = () => _assaults.PressAssault(height);
        btn.OnReleased = _assaults.FreeAssault;
    }

    private void OnFightingMannerAltered(FightingManner manner)
    {
        bool shown = manner is FightingManner.Melee or FightingManner.Missile or FightingManner.Magic;
        _basicBoard.Visible = manner is FightingManner.Melee or FightingManner.Missile;
        _spellcastingBoard.Visible = manner == FightingManner.Magic;
        if (_basicBoard is IWidgetDatStateful stateful)
        {
            if (manner == FightingManner.Melee)
                stateful.TrySetCanonPhase(MeleePhase);
            else if (manner == FightingManner.Missile)
                stateful.TrySetCanonPhase(MissilePhase);
        }
        AssignStaticPhrase(
            _strengthCaption,
            manner == FightingManner.Missile ? _captions.Accuracy : _captions.Power,
            rightAligned: true);
        _setPaneShown(shown);
        SynchronizeControls();
    }

    private void OnAssaultPhaseAltered() => SynchronizeControls();

    private void SynchronizeControls()
    {
        _strengthControl.AssignScalarLocus(_assaults.WantedStrength);
        _hi.Selected = _assaults.AskedHeight == AssaultElevation.High;
        _medium.Selected = _assaults.AskedHeight == AssaultElevation.Medium;
        _lo.Selected = _assaults.AskedHeight == AssaultElevation.Low;
        _repeatAssaults.Selected = _bindings.CurrentValue(CharacterOptionId.AutoRepeatAttack);
        _autoMark.Selected = _bindings.CurrentValue(CharacterOptionId.AutoTarget);
        _keepInLens.Selected = _bindings.CurrentValue(CharacterOptionId.ViewCombatTarget);
    }

    private static void AssignStaticPhrase(WidgetPhrase? phrase, string val, bool rightAligned)
    {
        if (phrase is null) return;
        phrase.OneLine = true;
        phrase.Padding = 0f;
        phrase.Centered = false;
        phrase.RightAligned = rightAligned;
        WidgetPhrase.Line[] stroke = [new WidgetPhrase.Line(val, phrase.DefaultTint)];
        phrase.StrokesSupplier = () => stroke;
    }
}

public sealed record FightingWidgetLabels(
    string Speed,
    string Power,
    string Accuracy,
    string RepeatAttacks,
    string AutoTarget,
    string KeepInView,
    string High,
    string Medium,
    string Low)
{
    private const uint WidgetStringChart = 0x23000001u;

    public static FightingWidgetLabels Resolve(ElemDetails trunk, DatStringPicker texts)
    {
        ArgumentNullException.ThrowIfNull(trunk);
        ArgumentNullException.ThrowIfNull(texts);

        return new FightingWidgetLabels(
            ElemString(FightingWidgetDriver.PaceCaptionIdent, WidgetStateInfo.StraightPhaseIdent, "Speed"),
            ElemString(FightingWidgetDriver.StrengthCaptionIdent, FightingWidgetDriver.MeleePhase, "Power"),
            ElemString(FightingWidgetDriver.StrengthCaptionIdent, FightingWidgetDriver.MissilePhase, "Accuracy"),
            CoreString("ID_CombatPanelOption_AutoRepeatAttack", "Repeat Attacks"),
            CoreString("ID_CombatPanelOption_AutoTarget", "Auto Target"),
            CoreString("ID_CombatPanelOption_ViewCombatTarget", "Keep in View"),
            ElemString(FightingWidgetDriver.HiBtnIdent, WidgetStateInfo.StraightPhaseIdent, "High"),
            ElemString(FightingWidgetDriver.MediumBtnIdent, WidgetStateInfo.StraightPhaseIdent, "Medium"),
            ElemString(FightingWidgetDriver.LoBtnIdent, WidgetStateInfo.StraightPhaseIdent, "Low"));

        string CoreString(string ident, string backup)
            => texts.Resolve(WidgetStringChart, DatStringPicker.CalculateDigest(ident)) ?? backup;

        string ElemString(uint elemIdent, uint phaseIdent, string backup)
        {
            var elem = Find(trunk, elemIdent);
            return elem is null
                || !elem.TryFetchNetProp(0x17u, out var prop, phaseIdent)
                || prop.Kind != WidgetPropertyKind.StringInfo
                ? backup
                : texts.Resolve(prop.StringInfoValue) ?? backup;
        }
    }

    private static ElemDetails? Find(ElemDetails elem, uint ident)
    {
        if (elem.Id == ident) return elem;
        foreach (ElemDetails descendant in elem.Children)
            if (Find(descendant, ident) is { } located)
                return located;
        return null;
    }
}

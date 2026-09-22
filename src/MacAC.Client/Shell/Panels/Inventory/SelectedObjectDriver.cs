using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed class SelectedObjectDriver : IRetainedPaneDriver
{
    public const uint VesselIdent = 0x1000019E;
    public const uint LabelTag = 0x1000019F;
    public const uint TopLayerIdent = 0x100001A0;
    public const uint HealthGaugeIdent = 0x100001A1;
    public const uint ManaGaugeIdent = 0x100001A2;
    public const uint PileDimsListingIdent = 0x100001A3;
    public const uint PileDimsDialIdent = 0x100001A4;

    private const double FlashSecs = 0.25;

    private const int LabelZOrderingOnTop = 1_000_000;

    private const int TopLayerZOrdering = LabelZOrderingOnTop - 1;

    private const float LabelBandHeight = 15f;

    private readonly WidgetElem? _label;
    private readonly WidgetDatElement? _topLayer;
    private readonly WidgetGauge? _healthGauge;
    private readonly WidgetGauge? _manaGauge;
    private readonly WidgetField? _pileDimsListing;
    private readonly WidgetScroller? _pileDimsDial;

    private readonly Func<uint, bool> _isHealthMark;
    private readonly Func<uint, bool> _isPossessedByAvatar;
    private readonly Func<uint, string?> _locateLabel;
    private readonly Func<uint, float> _healthPct;
    private readonly Func<uint, bool> _hasHealth;
    private readonly Func<uint, uint> _pileDims;
    private readonly Action<uint> _transmitAskHealth;
    private readonly Func<uint, float> _manaPct;
    private readonly Action<uint> _transmitAskGearMana;
    private readonly StackSplitGauge _divideQty;
    private readonly PickPhase _pick;
    private readonly Func<uint, bool> _isMerchantDivideExempt;
    private readonly Func<uint, bool> _isCoinstack;
    private readonly Func<int> _coinSum;
    private readonly Action<Action<uint, float>> _delistHealthAltered;
    private readonly Action<Action<uint, float, bool>> _delistGearManaAltered;
    private readonly Action<Action<ClientThing>> _delistObjectUpdated;

    private uint? _latest;
    private string? _latestLabel;
    private double _flashLeftover;   // > 0 while the selection overlay is flashing
    private bool _changingDivideFromDial;
    private bool _destroyed;

    private static readonly Vector4 LabelTint = new(1f, 1f, 1f, 1f);

    private SelectedObjectDriver(
        ImportedArrangement arrangement,
        PickPhase selection,
        Action<Action<uint, float>> enlistHealthAltered,
        Action<Action<uint, float>> delistHealthAltered,
        Action<Action<uint, float, bool>> enlistGearManaAltered,
        Action<Action<uint, float, bool>> delistGearManaAltered,
        Func<uint, bool> isHealthMark,
        Func<uint, bool> isPossessedByAvatar,
        Func<uint, string?> moniker,
        Func<uint, float> healthPct,
        Func<uint, bool> hasHealth,
        Func<uint, uint> pileDims,
        Action<uint> transmitAskHealth,
        Func<uint, float> manaPct,
        Action<uint> transmitAskGearMana,
        WidgetDatFont? datTypeface,
        StackSplitGauge splitQuantity,
        Action<Action<ClientThing>> enlistObjectUpdated,
        Action<Action<ClientThing>> delistObjectUpdated,
        Func<uint, bool> isVendorSplitExempt,
        Func<uint, bool>? isCoinstack,
        Func<int>? coinSum)
    {
        _isHealthMark = isHealthMark;
        _isPossessedByAvatar = isPossessedByAvatar;
        _locateLabel = moniker;
        _healthPct = healthPct;
        _hasHealth = hasHealth;
        _pileDims = pileDims;
        _transmitAskHealth = transmitAskHealth;
        _manaPct = manaPct;
        _transmitAskGearMana = transmitAskGearMana;
        _divideQty = splitQuantity ?? throw new ArgumentNullException(nameof(splitQuantity));
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _isMerchantDivideExempt = isVendorSplitExempt
            ?? throw new ArgumentNullException(nameof(isVendorSplitExempt));
        _isCoinstack = isCoinstack ?? (_ => false);
        _coinSum = coinSum ?? (() => 0);
        _delistHealthAltered = delistHealthAltered;
        _delistGearManaAltered = delistGearManaAltered;
        _delistObjectUpdated = delistObjectUpdated;

        // Find elements - silently skip absent ones (partial/test layouts).
        _label = arrangement.SeekElem(LabelTag);
        _topLayer = arrangement.SeekElem(TopLayerIdent) as WidgetDatElement;
        _healthGauge = arrangement.SeekElem(HealthGaugeIdent) as WidgetGauge;
        _manaGauge = arrangement.SeekElem(ManaGaugeIdent) as WidgetGauge;
        _pileDimsListing = arrangement.SeekElem(PileDimsListingIdent) as WidgetField;
        _pileDimsDial = arrangement.SeekElem(PileDimsDialIdent) as WidgetScroller;

        _topLayer?.ZOrder = TopLayerZOrdering;

        if (_healthGauge is not null)
        {
            _healthGauge.Visible = false;
            _healthGauge.Populate = () => _latest is uint g ? _healthPct(g) : (float?)0f;
        }
        if (_manaGauge is not null)
        {
            _manaGauge.Visible = false;
            _manaGauge.Populate = () => _latest is uint g ? _manaPct(g) : (float?)0f;
        }

        if (_pileDimsListing is not null)
        {
            _pileDimsListing.Visible = false;
            _pileDimsListing.OneStroke = true;
            _pileDimsListing.RightAligned = true;
            _pileDimsListing.Selectable = true;
            _pileDimsListing.WipeOnSubmit = false;
            _pileDimsListing.CaptureHistory = false;
            _pileDimsListing.ToonSift = static c => c is >= '0' and <= '9';
            _pileDimsListing.PickAllOnFocus = true;
            _pileDimsListing.OnSubmit = SealPileListing;
            _pileDimsListing.OnFocusLost = SealPileListing;
        }
        if (_pileDimsDial is not null)
        {
            _pileDimsDial.Visible = false;
            _pileDimsDial.Horizontal = true;
            _pileDimsDial.AssignScalarLocus(_divideQty.Ratio);
            _pileDimsDial.ScalarAltered = OnPileDialAltered;
        }

        if (_label is not null)
        {
            _label.ZOrder = LabelZOrderingOnTop;
            float labelWidth = _label.Width;
            WidgetDatFont? encloseTypeface = datTypeface;
            WidgetPhrase.Line[] strokeFor(int ordinal)
            {
                string? num = _latestLabel;
                if (string.IsNullOrEmpty(num))
                    return [];
                (string lead, string second) = EncloseLabelTwoStrokes(num, labelWidth, encloseTypeface);
                string phrase = ordinal is 0 ? lead : second;
                return phrase.Length is 0
                    ? []
                    : [new WidgetPhrase.Line(phrase, LabelTint)];
            }
            for (int strokeOrdinal = 0; strokeOrdinal < 2; ++strokeOrdinal)
            {
                int grabbed = strokeOrdinal;
                WidgetPhrase caption = new WidgetPhrase
                {
                    Left = 0f,
                    Top = grabbed * LabelBandHeight,
                    Width = _label.Width,
                    Height = LabelBandHeight,
                    Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right,
                    Centered = true,
                    OneLine = true,
                    DatFont = datTypeface,
                    ClickThrough = true,
                    AcceptsFocus = false,
                    IsEditControl = false,
                    CapturesPointerDrag = false,
                    StrokesSupplier = () => strokeFor(grabbed),
                };
                _label.AddChild(caption);
            }
        }

        _pick.Changed += OnPickChangeover;
        _divideQty.Changed += OnDivideQtyAltered;
        enlistHealthAltered(OnHealthAltered);
        enlistGearManaAltered(OnGearManaAltered);
        enlistObjectUpdated(OnObjectUpdated);
        if (_pick.ChosenObjectTag is { } starting)
            ApplySelection(starting);
    }

    public static SelectedObjectDriver Bind(
        ImportedArrangement arrangement,
        PickPhase pick,
        Action<Action<uint, float>> enlistHealthAltered,
        Action<Action<uint, float>> delistHealthAltered,
        Action<Action<uint, float, bool>> enlistGearManaAltered,
        Action<Action<uint, float, bool>> delistGearManaAltered,
        Func<uint, bool> isHealthMark,
        Func<uint, bool> isPossessedByAvatar,
        Func<uint, string?> label,
        Func<uint, float> healthPct,
        Func<uint, bool> hasHealth,
        Func<uint, uint> pileDims,
        Action<uint> transmitAskHealth,
        Func<uint, float> manaPct,
        Action<uint> transmitAskGearMana,
        WidgetDatFont? datTypeface,
        StackSplitGauge divideQty,
        Action<Action<ClientThing>> enlistObjectUpdated,
        Action<Action<ClientThing>> delistObjectUpdated,
        Func<uint, bool> isMerchantDivideExempt,
        Func<uint, bool>? isCoinstack = null,
        Func<int>? coinSum = null)
    {
        return new(
                arrangement, pick,
                enlistHealthAltered, delistHealthAltered,
                enlistGearManaAltered, delistGearManaAltered,
                isHealthMark, isPossessedByAvatar, label, healthPct, hasHealth, pileDims,
                transmitAskHealth, manaPct, transmitAskGearMana, datTypeface,
                divideQty, enlistObjectUpdated, delistObjectUpdated,
                isMerchantDivideExempt, isCoinstack, coinSum);
    }

    public void OnHealthAltered(uint oid, float pct)
    {
        if (_latest is uint c && c == oid && _isHealthMark(oid) && _healthGauge is not null)
            _healthGauge.Visible = true;
    }

    public void Tick(double diffSecs)
    {
        if (_flashLeftover <= 0) return;
        _flashLeftover -= diffSecs;
        if (_flashLeftover <= 0)
            AssignTopLayerPhase(WidgetStateInfo.StraightPhaseIdent);   // flash done → overlay back to blank
    }

    public void OnGearManaAltered(uint oid, float pct, bool valid)
    {
        if (_latest != oid)
            return;

        if (!valid)
        {
            _transmitAskGearMana(0);
            return;
        }

        _manaGauge?.Visible = true;
    }

    public bool FocusDividePileListing(uint objectIdent)
    {
        if (_latest != objectIdent
            || _pileDims(objectIdent) <= 1u
            || _pileDimsListing is null
            || !_pileDimsListing.Visible)

            return false;

        _pileDimsListing.SeekTrunk()?.AssignKeyboardFocus(_pileDimsListing);
        _pileDimsListing.PickAllPhrase();
        return true;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _pick.Changed -= OnPickChangeover;
        _divideQty.Changed -= OnDivideQtyAltered;
        _delistHealthAltered(OnHealthAltered);
        _delistGearManaAltered(OnGearManaAltered);
        _delistObjectUpdated(OnObjectUpdated);
    }

    internal static (string First, string Second) EncloseLabelTwoStrokes(
        string label,
        float width,
        WidgetDatFont? typeface)
    {
        if (typeface is null || typeface.MeasureWidth(label) <= width)
            return (label, string.Empty);

        int breakAt = -1;
        for (int idx = 0; idx < label.Length; ++idx)
        {
            if (label[idx] != ' ')
                continue;
            if (typeface.MeasureWidth(label[..idx]) <= width)
                breakAt = idx;
            else
                break;
        }

        if (breakAt <= 0)
            return (label, string.Empty);
        return (label[..breakAt], label[(breakAt + 1)..].TrimStart());
    }

    private void ApplySelection(uint? oid)
    {
        bool pickAltered = _latest != oid;

        if (pickAltered)
        {
            if (_healthGauge?.Visible == true)
                _transmitAskHealth(0);
            if (_manaGauge?.Visible == true)
                _transmitAskGearMana(0);
        }

        if (pickAltered)
        {
            _healthGauge?.Visible = false;
            _manaGauge?.Visible = false;
        }
        _pileDimsListing?.Visible = false;
        _pileDimsDial?.Visible = false;
        _divideQty.Reset(1u);
        _latestLabel = null;
        _latest = oid;

        if (oid is null)
        {
            AssignTopLayerPhase(WidgetStateInfo.StraightPhaseIdent);
            _flashLeftover = 0;
            return;
        }

        uint g = oid.Value;

        uint pileDims = _pileDims(g);
        RenewLabel(g, pileDims);

        AssignTopLayerPhase(pileDims > 1u
            ? CanonWidgetStateIds.StackedItemSelected
            : CanonWidgetStateIds.ObjectSelected);
        _flashLeftover = FlashSecs;

        if (pileDims > 1u)
        {
            bool merchantDivideExempt = _isMerchantDivideExempt(g);
            uint seed = merchantDivideExempt ? 1u : pileDims;
            _divideQty.Reset(pileDims, startingVal: seed);
            _pileDimsListing?.Visible = true;
            _pileDimsDial?.Visible = true;
        }

        if (pileDims <= 1u && _isHealthMark(g))
        {
            if (pickAltered)
                _transmitAskHealth(g);
            if (_hasHealth(g) && _healthGauge is not null)
                _healthGauge.Visible = true;
        }
        else if (pileDims <= 1u && _isPossessedByAvatar(g) && pickAltered)
            _transmitAskGearMana(g);
    }

    private void AssignTopLayerPhase(uint phase) => _topLayer?.TrySetCanonPhase(phase);

    private void SealPileListing(string phrase)
        => _divideQty.AssignFromPhrase(phrase);

    private void OnDivideQtyAltered()
    {
        _pileDimsListing?.AssignPhrase(_divideQty.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!_changingDivideFromDial)
            _pileDimsDial?.AssignScalarLocus(_divideQty.Ratio);
    }

    private void OnPileDialAltered(float locus)
    {
        _changingDivideFromDial = true;
        try
        {
            _divideQty.AssignFromDialRatio(locus);
        }
        finally
        {
            _changingDivideFromDial = false;
        }
    }

    private void OnObjectUpdated(ClientThing updated)
    {
        if (_latest != updated.ObjectId)
            return;
        uint pileDims = _pileDims(updated.ObjectId);
        if (pileDims != _divideQty.Ceiling)
            ApplySelection(updated.ObjectId);
        else
            RenewLabel(updated.ObjectId, pileDims);
    }

    private void RenewLabel(uint oid, uint pileDims)
    {
        string? objectLabel = _locateLabel(oid);
        _latestLabel = _isCoinstack(oid) && _isPossessedByAvatar(oid)
            ? $"{pileDims} {objectLabel} (of {_coinSum()})"
            : pileDims > 1u && !string.IsNullOrEmpty(objectLabel)
                ? $"{pileDims} {objectLabel}"
                : objectLabel;
    }

    private void OnPickChangeover(PickShift changeover)
    {
        _ = changeover;
        ApplySelection(_pick.ChosenObjectTag);
    }
}

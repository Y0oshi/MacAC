using System.Numerics;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed class SecureBarterWidgetDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x2100000Du;
    public const uint RootId = 0x1000007Au;
    public const uint PartnerLabelIdent = 0x1000007Eu;
    public const uint PartnerConditionIdent = 0x1000007Fu;
    public const uint PartnerTallyIdent = 0x10000080u;
    public const uint PartnerRosterIdent = 0x10000081u;
    public const uint SelfLabelIdent = 0x10000085u;
    public const uint BarterBtnIdent = 0x10000086u;
    public const uint SelfTallyIdent = 0x10000087u;
    public const uint SelfRosterIdent = 0x10000088u;
    public const uint WipeAllBtnIdent = 0x1000008Au;
    public const uint ShutButtonId = 0x1000008Bu;

    private const string ApprovedPhase = "Highlight";

    private const uint BarterTopLayerSpriteIdent = 0x06001DAEu;

    public sealed record Bindings(
        ISimBarterLens Trade,
        ClientThingChart Objects,
        Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
        Action<uint> OpenTrade,
        Action CloseTrade,
        Action<uint> AddToTrade,
        Action<bool /*selfAccepted*/, bool /*partnerAccepted*/, uint /*partner*/> AcceptTrade,
        Action DeclineTrade,
        Action ResetTrade,
        Action<bool> SetWindowVisible,
        uint SelfEmptySlotSprite = 0u,
        uint PartnerEmptySlotSprite = 0u,
        Func<int, string>? FormatTotalItems = null);

    private readonly Bindings _bindings;
    private readonly WidgetPhrase? _partnerLabel;
    private readonly WidgetElem? _partnerCondition;
    private readonly WidgetPhrase? _partnerTally;
    private readonly WidgetGearRoster? _partnerRoster;
    private readonly WidgetPhrase? _selfTally;
    private readonly WidgetGearRoster? _selfRoster;
    private readonly WidgetBtn? _barterBtn;

    private long _previousRev = long.MinValue;
    private bool _wasOpen;
    private uint _queuedPartner;
    private uint _queuedJunctureGear;
    private bool _destroyed;

    private SecureBarterWidgetDriver(
        ImportedArrangement arrangement,
        Bindings mappings)
    {
        _bindings = mappings;
        _partnerLabel = arrangement.SeekElem(PartnerLabelIdent) as WidgetPhrase;
        _partnerCondition = arrangement.SeekElem(PartnerConditionIdent);
        _partnerTally = arrangement.SeekElem(PartnerTallyIdent) as WidgetPhrase;
        _partnerRoster = arrangement.SeekElem(PartnerRosterIdent) as WidgetGearRoster;
        _selfTally = arrangement.SeekElem(SelfTallyIdent) as WidgetPhrase;
        _selfRoster = arrangement.SeekElem(SelfRosterIdent) as WidgetGearRoster;
        _barterBtn = arrangement.SeekElem(BarterBtnIdent) as WidgetBtn;

        if (_barterBtn is not null)
        {
            _barterBtn.SuppressSelfFlip = true;
            _barterBtn.OnClick = () =>
            {
                var capture = _bindings.Trade.Snapshot;
                if (!capture.IsOpen) return;
                if (capture.SelfAccepted)
                    _bindings.DeclineTrade();
                else
                    _bindings.AcceptTrade(
                        true, capture.PartnerAccepted, capture.PartnerGuid);
            };
        }
        if (arrangement.SeekElem(WipeAllBtnIdent) is WidgetBtn wipeAll)
            wipeAll.OnClick = () =>
            {
                if (_bindings.Trade.Snapshot.IsOpen) _bindings.ResetTrade();
            };
        if (arrangement.SeekElem(ShutButtonId) is WidgetBtn shut)
            shut.OnClick = () =>
            {
                if (_bindings.Trade.Snapshot.IsOpen) _bindings.CloseTrade();
            };

        _selfRoster?.EnrollPullHandler(new SelfGridDiscardProcessor(this));

        ConfigureGrid(_selfRoster, mappings.SelfEmptySlotSprite);
        ConfigureGrid(_partnerRoster, mappings.PartnerEmptySlotSprite);

        _bindings.SetWindowVisible(false);
    }

    public static SecureBarterWidgetDriver? Bind(
        ImportedArrangement arrangement, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(mappings);
        return arrangement.SeekElem(SelfRosterIdent) is not WidgetGearRoster
            || arrangement.SeekElem(PartnerRosterIdent) is not WidgetGearRoster
            ? null
            : new SecureBarterWidgetDriver(arrangement, mappings);
    }

    public void ReqSecureBarter(uint partnerOid, uint gearOid)
    {
        if (_destroyed || partnerOid is 0u) return;
        var capture = _bindings.Trade.Snapshot;
        if (capture.IsOpen && capture.PartnerGuid == partnerOid)
        {
            if (gearOid is not 0u) _bindings.AddToTrade(gearOid);
            return;
        }
        _queuedPartner = partnerOid;
        _queuedJunctureGear = gearOid;
        _bindings.OpenTrade(partnerOid);
    }

    public void Tick()
    {
        if (_destroyed) return;
        var capture = _bindings.Trade.Snapshot;

        if (capture.IsOpen && !_wasOpen)
        {
            _wasOpen = true;
            _bindings.SetWindowVisible(true);
            if (_queuedJunctureGear is not 0u
                && (_queuedPartner is 0u
                    || capture.PartnerGuid == _queuedPartner))

                _bindings.AddToTrade(_queuedJunctureGear);
            _queuedJunctureGear = 0u;
            _queuedPartner = 0u;
        }
        else if (!capture.IsOpen && _wasOpen)
        {
            _wasOpen = false;
            _bindings.SetWindowVisible(false);
        }

        if (capture.Revision == _previousRev) return;
        _previousRev = capture.Revision;

        if (_partnerLabel is not null)
        {
            string label = _bindings.Objects.Get(capture.PartnerGuid)
                ?.FetchAppropriateLabel() ?? string.Empty;
            _partnerLabel.StrokesSupplier =
                () => [new WidgetPhrase.Line(label, Vector4.One)];
        }
        if (_partnerCondition is WidgetDatElement condition)
            condition.EngagedPhase = capture.PartnerAccepted ? ApprovedPhase : "";
        _barterBtn?.Selected = capture.SelfAccepted;

        AssignTally(_selfTally, capture.SelfItemCount);
        AssignTally(_partnerTally, capture.PartnerItemCount);
        Fill(_selfRoster, SimBarterSide.Self);
        Fill(_partnerRoster, SimBarterSide.Partner);
    }

    public void SynchronizeVisibility()
    {
        _wasOpen = !_bindings.Trade.Snapshot.IsOpen; // force re-evaluate
        Tick();
    }

    public void OnShown() => Tick();

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _barterBtn?.OnClick = null;
    }

    private static void ConfigureGrid(WidgetGearRoster? roster, uint vacantSocketSprite)
    {
        if (roster is null) return;
        roster.Columns = 1;
        roster.SingleRank = true;
        roster.HorizontalRoll = true;
        roster.ChamberWidth = 32f;
        roster.ChamberHeight = 32f;
        roster.PopulateShownVacantSockets = true;
        if (vacantSocketSprite is not 0u)
            roster.ChamberVacantSprite = vacantSocketSprite;
        roster.VacantSocketMaker = () => new WidgetGearSlot
        {
            SpriteResolve = roster.SpriteResolve,
            AllowPullSrc = false,
        };
    }

    private void AssignTally(WidgetPhrase? phrase, int tally)
    {
        if (phrase is null) return;
        string stroke = _bindings.FormatTotalItems?.Invoke(tally)
            ?? tally.ToString();
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(stroke, Vector4.One)];
    }

    private void Fill(WidgetGearRoster? roster, SimBarterSide flank)
    {
        if (roster is null) return;
        using (roster.DeferArrangement())
        {
            roster.Flush();
            foreach (uint oid in _bindings.Trade.FetchGearList(flank))
            {
                var gear = _bindings.Objects.Get(oid);
                uint glyph = gear is null ? 0u : _bindings.ResolveIcon(
                    gear.Type,
                    gear.IconId,
                    gear.GlyphUnderlayIdent,
                    gear.GlyphTopLayerIdent,
                    gear.Effects);
                WidgetGearSlot chamber = new WidgetGearSlot
                {
                    SpriteResolve = roster.SpriteResolve,
                    SocketIdx = roster.FetchCountWIDGETGearList(),
                    AllowPullSrc = false,
                    UnhideBarterTopLayer = flank == SimBarterSide.Self,
                    BarterTopLayerSprite = BarterTopLayerSpriteIdent,
                    HintPhraseLocate = g => _bindings.Objects.Get(g)?.FetchHintReadoutLabel(),
                };
                chamber.AssignGear(oid, glyph);
                roster.AddItem(chamber);
            }
        }
    }

    private sealed class SelfGridDiscardProcessor(SecureBarterWidgetDriver holder)
        : IGearListDragHandler
    {
        public void OnPullLift(
            WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
        {
        }

        public GearDragAcceptance OnPullOver(
            WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
        {
            return cargo.SourceKind == GearDragSource.Inventory
                        ? GearDragAcceptance.Accept
                        : GearDragAcceptance.Reject;
        }

        public void ProcessDiscardFree(
            WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
        {
            if (cargo.SourceKind != GearDragSource.Inventory) return;
            if (holder._bindings.Trade.Snapshot.IsOpen)
                holder._bindings.AddToTrade(cargo.ObjId);
        }
    }
}

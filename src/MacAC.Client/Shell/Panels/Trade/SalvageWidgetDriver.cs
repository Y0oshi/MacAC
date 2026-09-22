using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed class SalvageWidgetDriver : IRetainedPaneDriver, IGearListDragHandler
{
    public const uint LayoutId = 0x2100000Cu;
    public const uint RootId = 0x10000073u;
    public const uint GearRosterTag = 0x10000074u;
    public const uint ScrollerIdent = 0x10000075u;
    public const uint SalvageBtnIdent = 0x10000076u;
    public const uint ShutBtnTag = 0x10000078u;
    private const uint BarterTopLayerSprite = 0x06001DAEu;

    public sealed record Bindings(
        ClientThingChart Objects,
        Func<uint, bool> IsOwned,
        Func<uint, IReadOnlyList<uint>, bool> SendSalvage,
        Func<bool> AllowMultipleMaterials,
        Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
        Action<bool> SetWindowVisible,
        Action<string> Report,
        uint EmptySlotSprite = 0u);

    private readonly Bindings _bindings;
    private readonly WidgetGearRoster _roster;
    private readonly WidgetBtn _salvageBtn;
    private readonly WidgetBtn? _shutBtn;
    private readonly List<ClientThing> _gearList = [];
    private uint _matl;
    private bool _destroyed;
    private bool _sending;

    private SalvageWidgetDriver(ImportedArrangement arrangement, Bindings mappings)
    {
        _bindings = mappings;
        _roster = (WidgetGearRoster)arrangement.SeekElem(GearRosterTag)!;
        _salvageBtn = (WidgetBtn)arrangement.SeekElem(SalvageBtnIdent)!;
        _shutBtn = arrangement.SeekElem(ShutBtnTag) as WidgetBtn;
        _roster.Columns = 1;
        _roster.SingleRank = true;
        _roster.HorizontalRoll = true;
        _roster.ChamberWidth = 32f;
        _roster.ChamberHeight = 32f;
        _roster.PopulateShownVacantSockets = true;
        _roster.ChamberVacantSprite = mappings.EmptySlotSprite;
        _roster.VacantSocketMaker = () => new WidgetGearSlot
        {
            SpriteResolve = _roster.SpriteResolve,
            AllowPullSrc = false,
        };
        _roster.EnrollPullHandler(this);
        if (arrangement.SeekElem(ScrollerIdent) is WidgetScroller scroller)
        {
            scroller.Model = _roster.Scroll;
            scroller.Horizontal = true;
        }
        _roster.ExamineGearAsked = DropGear;
        _salvageBtn.SuppressSelfFlip = true;
        _salvageBtn.OnClick = () => Salvage();
        _shutBtn?.OnClick = Close;
        mappings.Objects.ObjectRemoved += OnObjectRemoved;
        mappings.Objects.ObjectUpdated += OnObjectAltered;
        mappings.Objects.ObjectMoved += OnObjectMoved;
        mappings.Objects.ContainerContentsReplaced += OnVesselInsidesReplaced;
        mappings.Objects.Cleared += Close;
        Refresh();
    }

    public static SalvageWidgetDriver? Bind(ImportedArrangement arrangement, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(mappings);
        return arrangement.SeekElem(GearRosterTag) is WidgetGearRoster
            && arrangement.SeekElem(SalvageBtnIdent) is WidgetBtn
                ? new SalvageWidgetDriver(arrangement, mappings) : null;
    }

    public int GearTally => _gearList.Count;
    public uint ToolIdent { get; private set; }

    public void ProcessRuleAct(ItemRulingAction act)
    {
        if (act.Kind == ItemRulingActionKind.OpenSalvage)
            Open(act.ObjectId);
    }

    public void Open(uint toolIdent)
    {
        if (_destroyed || !IsToolOnHand(toolIdent)) return;
        WipeGearList();
        ToolIdent = toolIdent;
        Refresh();
        _bindings.SetWindowVisible(true);
    }

    public bool CanAppend(uint gearIdent)
    {
        return _destroyed || _sending || !IsToolOnHand(ToolIdent)
            || gearIdent == ToolIdent || !_bindings.IsOwned(gearIdent)
            || _gearList.Any(gear => gear.ObjectId == gearIdent)
            || _bindings.Objects.Get(gearIdent) is not { } item
            ? false
            : _bindings.Objects.FetchInsides(gearIdent).Count > 0
            || SalvageRules.IsSuitable(item, _matl, _bindings.AllowMultipleMaterials());
    }

    public bool AddItem(uint gearIdent)
    {
        if (!CanAppend(gearIdent)) return false;
        HashSet<uint> visited = new HashSet<uint>();
        AppendRecursive(gearIdent, visited);
        Refresh();
        return true;
    }

    public void DropGear(uint gearIdent)
    {
        int ordinal = _gearList.FindIndex(gear => gear.ObjectId == gearIdent);
        if (ordinal < 0) return;
        _gearList[ordinal].BarterPhase = 0;
        _gearList.RemoveAt(ordinal);
        if (_gearList.Count is 0) _matl = 0;
        Refresh();
    }

    public bool Salvage()
    {
        if (_destroyed || _sending || _gearList.Count is 0 || !IsToolOnHand(ToolIdent)) return false;
        if (_gearList.Any(gear => !_bindings.IsOwned(gear.ObjectId)
            || !ReferenceEquals(gear, _bindings.Objects.Get(gear.ObjectId))
            || !SalvageRules.IsSuitable(gear)))
        {
            _bindings.Report("The list of items you are attempting to salvage is invalid.");
            return false;
        }
        uint[] gearIdents = [.. _gearList.AsEnumerable().Reverse().Select(gear => gear.ObjectId)];
        _sending = true;
        try
        {
            foreach (ClientThing item in _gearList) item.BarterPhase = 0;
            if (!_bindings.SendSalvage(ToolIdent, gearIdents))
            {
                foreach (ClientThing item in _gearList) item.BarterPhase = 1;
                _bindings.Report("You cannot salvage those items right now.");
                return false;
            }
            WipeGearList();
            Refresh();
            return true;
        }
        finally { _sending = false; }
    }

    public void Close()
    {
        OnConcealed();
        _bindings.SetWindowVisible(false);
    }

    public void OnConcealed()
    {
        ToolIdent = 0;
        WipeGearList();
        Refresh();
    }

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (ReferenceEquals(srcRoster, _roster)) DropGear(cargo.ObjId);
    }

    public GearDragAcceptance OnPullOver(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        return ReferenceEquals(markRoster, _roster) && cargo.SourceKind == GearDragSource.Inventory && CanAppend(cargo.ObjId)
                ? GearDragAcceptance.Accept : GearDragAcceptance.Reject;
    }

    public void ProcessDiscardFree(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        if (OnPullOver(markRoster, markChamber, cargo) == GearDragAcceptance.Accept) AddItem(cargo.ObjId);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _bindings.Objects.ObjectRemoved -= OnObjectRemoved;
        _bindings.Objects.ObjectUpdated -= OnObjectAltered;
        _bindings.Objects.ObjectMoved -= OnObjectMoved;
        _bindings.Objects.ContainerContentsReplaced -= OnVesselInsidesReplaced;
        _bindings.Objects.Cleared -= Close;
        OnConcealed();
        _salvageBtn.OnClick = null;
        _shutBtn?.OnClick = null;
        _roster.ExamineGearAsked = null;
    }

    private bool IsToolOnHand(uint toolIdent)
    {
        return toolIdent is not 0u
        && _bindings.IsOwned(toolIdent)
        && _bindings.Objects.Get(toolIdent) is { } tool
        && (tool.Type & GearKind.TinkeringTool) != 0;
    }

    private void AppendRecursive(uint gearIdent, HashSet<uint> visited)
    {
        if (!visited.Add(gearIdent) || !CanAppend(gearIdent)) return;
        var insides = _bindings.Objects.FetchInsides(gearIdent);
        if (insides.Count > 0)
        {
            _bindings.Report($"Adding contents of {_bindings.Objects.Get(gearIdent)!.FetchAppropriateLabel()}");
            foreach (uint descendant in insides)
                AppendRecursive(descendant, visited);
            return;
        }
        var gear = _bindings.Objects.Get(gearIdent)!;
        gear.BarterPhase = 1;
        _gearList.Add(gear);
        if (_gearList.Count is 1) _matl = gear.MaterialType ?? 0u;
    }

    private void WipeGearList()
    {
        foreach (ClientThing gear in _gearList) gear.BarterPhase = 0;
        _gearList.Clear();
        _matl = 0;
    }

    private void Refresh()
    {
        using (_roster.DeferArrangement())
        {
            _roster.Flush();
            foreach (ClientThing gear in _gearList)
            {
                WidgetGearSlot socket = new WidgetGearSlot
                {
                    SocketIdx = _roster.FetchCountWIDGETGearList(),
                    SpriteResolve = _roster.SpriteResolve,
                    AllowPullSrc = true,
                    SrcSort = GearDragSource.Inventory,
                    UnhideBarterTopLayer = true,
                    BarterTopLayerSprite = BarterTopLayerSprite,
                    HintPhraseLocate = ident => _bindings.Objects.Get(ident)?.FetchHintReadoutLabel(),
                };
                socket.AssignGear(gear.ObjectId, _bindings.ResolveIcon(
                    gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects));
                socket.AssignStructure(gear.Structure, gear.MaxStructure);
                _roster.AddItem(socket);
            }
        }
        _salvageBtn.Enabled = _gearList.Count > 0;
    }

    private void OnObjectRemoved(ClientThing gear)
    {
        if (gear.ObjectId == ToolIdent) Close();
        else DropGear(gear.ObjectId);
    }

    private void OnObjectAltered(ClientThing gear)
    {
        if (gear.ObjectId == ToolIdent && !IsToolOnHand(ToolIdent)) { Close(); return; }
        if (!_gearList.Any(lined => lined.ObjectId == gear.ObjectId)) return;
        if (!_bindings.IsOwned(gear.ObjectId) || !SalvageRules.IsSuitable(gear)
            || !_gearList.Any(lined => ReferenceEquals(lined, gear))) DropGear(gear.ObjectId);
        else Refresh();
    }

    private void OnObjectMoved(ObjectRelocation relocate) => RevalidateOwnership();

    private void OnVesselInsidesReplaced(uint vesselIdent) => RevalidateOwnership();

    private void RevalidateOwnership()
    {
        if (ToolIdent is 0) return;
        if (!IsToolOnHand(ToolIdent)) { Close(); return; }
        for (int ordinal = _gearList.Count - 1; ordinal >= 0; --ordinal)
        {
            var gear = _gearList[ordinal];
            if (!_bindings.IsOwned(gear.ObjectId)
                || !ReferenceEquals(gear, _bindings.Objects.Get(gear.ObjectId)))
            {
                gear.BarterPhase = 0;
                _gearList.RemoveAt(ordinal);
            }
        }
        if (_gearList.Count is 0) _matl = 0;
        Refresh();
    }
}

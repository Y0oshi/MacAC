namespace MacAC.Client.Shell;

public enum WidgetGearListFlow
{
    RowMajor,

    ColumnMajor,
}

public sealed class WidgetGearRoster : WidgetElem
{
    private readonly List<WidgetGearSlot> _chambers = [];
    private int _arrangementDeferralZDepth;

    public WidgetScrollable Scroll { get; }

    public WidgetGearRoster(
        Func<uint, (uint tex, int w, int h)>? spriteLocate = null,
        WidgetScrollable? roll = null)
    {
        Scroll = roll ?? new WidgetScrollable();
        SpriteResolve = spriteLocate;
        AddItem(new WidgetGearSlot { SpriteResolve = spriteLocate });
    }

    public override bool ConsumesDatChildren => true;

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public IReadOnlyList<uint>? CooldownSprites
    {
        get;
        set
        {
            field = value;
            foreach (WidgetGearSlot chamber in _chambers)
                chamber.CooldownSprites = value;
        }
    }

    public Func<uint, int>? CooldownHopSupplier
    {
        get;
        set
        {
            field = value;
            foreach (WidgetGearSlot chamber in _chambers)
                chamber.CooldownHopProvider = value;
        }
    }

    public Action<object, int, int>? RegistryDropped { get; set; }

    public Func<uint, bool>? PrimaryGearPressed { get; set; }

    public Action<uint>? ExamineGearAsked { get; set; }

    public Action<uint>? PrimaryRegistryListingPressed { get; set; }

    public Action<uint>? ExamineRegistryListingAsked { get; set; }

    public uint ChamberVacantSprite
    {
        get;
        set
        {
            field = value;
            if (value is not 0)
                foreach (var slot in _chambers) slot.VacantSprite = value;
        }
    }

    public IGearListDragHandler? PullHandler { get; private set; }

    public void EnrollPullHandler(IGearListDragHandler handler) => PullHandler = handler;

    public WidgetGearSlot Cell
    {
        get
        {
            return _chambers.Count > 0
        ? _chambers[0]
        : throw new InvalidOperationException("WidgetGearRoster has no cells; call AddItem first or use GetItem(index)");
        }
    }

    public int FetchCountWIDGETGearList() => _chambers.Count;

    public int IdxOf(WidgetGearSlot chamber) => _chambers.IndexOf(chamber);

    public WidgetGearSlot? GetItem(int ordinal)
        => ordinal >= 0 && ordinal < _chambers.Count ? _chambers[ordinal] : null;

    public void AddItem(WidgetGearSlot chamber)
    {
        chamber.SpriteResolve ??= SpriteResolve;
        chamber.CooldownSprites ??= CooldownSprites;
        chamber.CooldownHopProvider ??= CooldownHopSupplier;
        if (ChamberVacantSprite is not 0) chamber.VacantSprite = ChamberVacantSprite;
        chamber.Moorings = MooringRims.None;
        _chambers.Add(chamber);
        AddChild(chamber);
        if (_arrangementDeferralZDepth is 0)
            ArrangementChambers();
    }

    public IDisposable DeferArrangement()
    {
        ++_arrangementDeferralZDepth;
        return new ArrangementDeferral(this);
    }

    public int Columns { get; set; } = 1;

    public WidgetGearListFlow Flow { get; set; } = WidgetGearListFlow.RowMajor;

    public float ChamberWidth { get; set; }
    public float ChamberHeight { get; set; }

    public bool SingleRank { get; set; }

    public bool HorizontalRoll { get; set; }

    public bool PopulateShownVacantSockets { get; set; }

    public Func<WidgetGearSlot>? VacantSocketMaker { get; set; }

    public static int RankTally(int chamberTally, int columns)
    {
        int cols = columns < 1 ? 1 : columns;
        return (chamberTally + cols - 1) / cols;
    }

    public void Flush()
    {
        foreach (var slot in _chambers) DropDescendant(slot);
        _chambers.Clear();
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (RegistryDropped is not null && e.Payload is not null)
        {
            if (e.Type is WidgetEventType.PullJoin or WidgetEventType.PullOver)
                return true;
            if (e.Type == WidgetEventType.DiscardReleased)
            {
                RegistryDropped(e.Payload, e.Data1, e.Data2);
                return true;
            }
        }
        if (e.Type == WidgetEventType.Roll && ChamberWidth > 0f)
        {
            Scroll.RollByStrokes(-e.Data0);
            return true;
        }
        return base.OnSignal(e);
    }

    public void RollGearIntoLens(int ordinal)
    {
        if (ChamberWidth <= 0f || ordinal < 0 || ordinal >= _chambers.Count) return;
        if (SingleRank && HorizontalRoll)
        {
            float left = ordinal * ChamberWidth;
            float right = left + ChamberWidth;
            if (left < Scroll.RollY)
                Scroll.AssignRollY((int)MathF.Floor(left));
            else if (right > Scroll.RollY + Width)
                Scroll.AssignRollY((int)MathF.Ceiling(right - Width));
            return;
        }
        int columns = Math.Max(1, Columns);
        int rank = Flow == WidgetGearListFlow.ColumnMajor
            ? ordinal % Math.Max(1, RankTally(_chambers.Count, columns))
            : ordinal / columns;
        float top = rank * ChamberHeight;
        float bottom = top + ChamberHeight;
        if (top < Scroll.RollY) Scroll.AssignRollY((int)MathF.Floor(top));
        else if (bottom > Scroll.RollY + Height)
            Scroll.AssignRollY((int)MathF.Ceiling(bottom - Height));
    }

    internal static (float x, float y) ChamberShift(int ordinal, int columns, float chamberW, float chamberH)
    {
        int col = ordinal % columns, rank = ordinal / columns;
        return (col * chamberW, rank * chamberH);
    }

    internal static (float x, float y) ChamberShift(
        int ordinal, int columns, int chamberTally, WidgetGearListFlow flow, float chamberW, float chamberH)
    {
        int cols = columns < 1 ? 1 : columns;
        if (flow == WidgetGearListFlow.ColumnMajor)
        {
            int ranks = Math.Max(1, RankTally(chamberTally, cols));
            int col = ordinal / ranks, rank = ordinal % ranks;
            return (col * chamberW, rank * chamberH);
        }
        return ChamberShift(ordinal, cols, chamberW, chamberH);
    }

    internal void ArrangementChambers()
    {
        if (ChamberWidth <= 0f)
        {
            if (_chambers.Count > 0)
            {
                WidgetGearSlot slot = _chambers[0];
                slot.Left = 0; slot.Top = 0; slot.Width = Width; slot.Height = Height; slot.Visible = true;
            }
            return;
        }

        RefreshVacantSockets();

        int cols = SingleRank
            ? Math.Max(1, _chambers.Count)
            : Columns < 1 ? 1 : Columns;
        int chamberH = (int)MathF.Round(ChamberHeight);

        if (SingleRank && HorizontalRoll)
        {
            int chamberW = Math.Max(1, (int)MathF.Round(ChamberWidth));
            Scroll.LineHeight = chamberW;
            Scroll.ContentHeight = _chambers.Count * chamberW;
            Scroll.LensHeight = (int)MathF.Floor(Width);
            Scroll.AssignRollY(Scroll.RollY);
            float rollX = Scroll.RollY;

            for (int idx = 0; idx < _chambers.Count; ++idx)
            {
                float left = idx * ChamberWidth - rollX;
                var chamber = _chambers[idx];
                chamber.Left = left;
                chamber.Top = 0f;
                chamber.Width = ChamberWidth;
                chamber.Height = ChamberHeight;
                chamber.Visible = left < Width && left + ChamberWidth > 0f;
            }
            return;
        }

        Scroll.LineHeight = chamberH > 0 ? chamberH : 1;
        Scroll.ContentHeight = RankTally(_chambers.Count, cols) * chamberH;
        Scroll.LensHeight = (int)MathF.Floor(Height);
        Scroll.AssignRollY(Scroll.RollY);
        float rollY = Scroll.RollY;

        for (int idx = 0; idx < _chambers.Count; ++idx)
        {
            var (x, baseY) = ChamberShift(idx, cols, _chambers.Count, Flow, ChamberWidth, ChamberHeight);
            float top = baseY - rollY;
            WidgetGearSlot chamber = _chambers[idx];
            chamber.Left = x; chamber.Top = top; chamber.Width = ChamberWidth; chamber.Height = ChamberHeight;
            chamber.Visible = top < Height && top + ChamberHeight > 0f;
        }
    }

    protected override void OnPaint(WidgetRenderScope cx) => ArrangementChambers();

    private void RefreshVacantSockets()
    {
        if (!PopulateShownVacantSockets || !SingleRank || ChamberWidth <= 0f)
            return;

        int shownChamberTally = Math.Max(0, (int)MathF.Floor(Width / ChamberWidth));
        while (_chambers.Count < shownChamberTally)
        {
            WidgetGearSlot chamber = VacantSocketMaker?.Invoke() ?? new WidgetGearSlot();
            chamber.SpriteResolve ??= SpriteResolve;
            chamber.CooldownSprites ??= CooldownSprites;
            chamber.CooldownHopProvider ??= CooldownHopSupplier;
            if (ChamberVacantSprite is not 0) chamber.VacantSprite = ChamberVacantSprite;
            chamber.Moorings = MooringRims.None;
            _chambers.Add(chamber);
            AddChild(chamber);
        }

        while (_chambers.Count > shownChamberTally && _chambers[^1].IsVacantSocket)
        {
            var chamber = _chambers[^1];
            _chambers.RemoveAt(_chambers.Count - 1);
            DropDescendant(chamber);
        }
    }

    private void DisposeRest()
    {
        if (_arrangementDeferralZDepth <= 0)
            return;
        --_arrangementDeferralZDepth;
        if (_arrangementDeferralZDepth is 0)
            ArrangementChambers();
    }

    private sealed class ArrangementDeferral(WidgetGearRoster holder) : IDisposable
    {
        private WidgetGearRoster? _holder = holder;

        public void Dispose()
        {
            WidgetGearRoster? holder = _holder;
            _holder = null;
            holder?.DisposeRest();
        }
    }
}

using System.Numerics;

namespace MacAC.Client.Shell;

public abstract partial class WidgetElem
{
    public uint SignalIdent { get; internal set; }

    public uint DatElemIdent { get; internal set; }

    public string? Name { get; set; }

    public bool AuthoredInvisible { get; internal set; }

    public bool AuthoredHintTurnedOn { get; internal set; }

    public string? AuthoredHintPhrase { get; internal set; }

    public uint AuthoredHintTrunkElemIdent { get; internal set; }

    public uint AuthoredHintArrangementDid { get; internal set; }

    public uint AuthoredHintPhraseDescendantElemIdent { get; internal set; }

    public float? AuthoredHintDelaySecs { get; internal set; }

    public int? AuthoredRescaleUpperWidth { get; internal set; }

    public int? AuthoredRescaleLowerWidth { get; internal set; }

    public int? AuthoredRescaleUpperHeight { get; internal set; }

    public int? AuthoredRescaleLowerHeight { get; internal set; }

    public uint SrcArrangementDid { get; internal set; }

    public Func<uint>? LocatedObjectOidSupplier { get; set; }

    public IReadOnlyDictionary<string, WidgetCursorMedia> StateCursors => _phaseCursors;

    public virtual string EngagedCurPhaseLabel => "";

    public WidgetCursorMedia EngagedCur()
        => CurForPhase(EngagedCurPhaseLabel, allowBackup: true);

    public void AssignPhaseCursors(IReadOnlyDictionary<string, WidgetCursorMedia> cursors)
    {
        _phaseCursors.Clear();
        foreach (var kv in cursors)
        {
            if (kv.Value.IsValid)
                _phaseCursors[kv.Key] = kv.Value;
        }
    }

    public WidgetCursorMedia CurForPhase(string phaseLabel, bool allowBackup = true)
    {
        if (!string.IsNullOrEmpty(phaseLabel)
            && _phaseCursors.TryGetValue(phaseLabel, out var named)
            && named.IsValid)
            return named;

        if (!allowBackup)
            return default;

        if (_phaseCursors.TryGetValue("", out var straight) && straight.IsValid)
            return straight;
        return _phaseCursors.TryGetValue("Normal", out var norm) && norm.IsValid ? norm : default;
    }

    public virtual (uint tex, int w, int h)? FetchPullGhost() => null;

    public float Left { get; set; }

    public float Top { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public Vector2 MonitorLocus
    {
        get
        {
            var p = new Vector2(Left, Top);
            var ancestor = Ancestor;
            while (ancestor is not null)
            {
                p += new Vector2(ancestor.Left, ancestor.Top);
                ancestor = ancestor.Ancestor;
            }
            return p;
        }
    }

    public bool Visible
    {
        get;
        set
        {
            if (field == value) return;

            WidgetTrunk? trunk = SeekTrunk();
            trunk?.OnElemVisChanging(this, value);
            field = value;
            trunk?.OnElemVisAltered(this, value);
        }
    } = true;

    public Func<bool>? ShownSrc { get; set; }

    public bool Enabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnTurnedOnAltered();
        }
    } = true;

    public Func<bool>? TurnedOnSrc { get; set; }

    public bool ClickThrough { get; set; }

    public bool AcceptsFocus { get; set; }

    public bool IsEditControl { get; set; }

    public virtual bool IsPullSrc => false;

    public int ZOrder
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Ancestor?.DirtyDescendantOrdering();
        }
    }

    public static (float x, float y, float w, float h) CalculateAnchoredRect(
        MooringRims edges, float mL, float mT, float mR, float mB,
        float w0, float h0, float ancestorW, float ancestorH)
    {
        bool l = (edges & MooringRims.Left) != 0, r = (edges & MooringRims.Right) != 0;
        float x, w;
        if (l && r) { x = mL; w = ancestorW - mR - mL; }
        else if (r) { w = w0; x = ancestorW - mR - w0; }
        else { x = mL; w = w0; }

        bool t = (edges & MooringRims.Top) != 0, b = (edges & MooringRims.Bottom) != 0;
        float y, h;
        if (t && b) { y = mT; h = ancestorH - mB - mT; }
        else if (b) { h = h0; y = ancestorH - mB - h0; }
        else { y = mT; h = h0; }

        if (w < 0) w = 0;
        if (h < 0) h = 0;
        return (x, y, w, h);
    }

    public virtual void AddChild(WidgetElem descendant)
    {
        descendant.Ancestor?.DropDescendant(descendant);
        descendant.Ancestor = this;
        _descendants.Add(descendant);
        DirtyDescendantOrdering();
    }

    public bool Draggable { get; set; }

    public bool PaneRelocateHnd { get; set; }

    public bool ConstrainPullToParent { get; set; }

    public bool ConstrainRescaleToParent { get; set; }

    public bool Resizable { get; set; }

    public bool CapturesPointerDrag { get; set; }

    public virtual bool HndsPress => false;

    public virtual bool ReceivesHoverPointerRelocate => false;

    public MooringRims Moorings
    {
        get;
        set
        {
            field = value;
            ArrangementRule = null;
            _mooringGrabbed = false;
        }
    } = MooringRims.Left | MooringRims.Top;

    public WidgetArrangementRule? ArrangementRule { get; set; }

    public WidgetElem? Ancestor { get; private set; }

    public IReadOnlyList<WidgetElem> Children => _descendants;

    public static WidgetElem? SeekDescendant(WidgetElem trunk, uint datElemIdent)
    {
        ArgumentNullException.ThrowIfNull(trunk);
        if (trunk.DatElemIdent == datElemIdent) return trunk;
        foreach (WidgetElem descendant in trunk.Children)
        {
            WidgetElem? located = SeekDescendant(descendant, datElemIdent);
            if (located is not null) return located;
        }
        return null;
    }

    public virtual bool DropDescendant(WidgetElem descendant)
    {
        if (!_descendants.Contains(descendant)) return false;
        SeekTrunk()?.OnSubtreeRemoving(descendant);
        _descendants.Remove(descendant);
        descendant.Ancestor = null;
        DirtyDescendantOrdering();
        return true;
    }

    public virtual bool OnSignal(in WidgetSignal e) => false;

    public virtual object? FetchPullCargo() => null;

    public virtual string? FetchHintPhrase()
    {
        string? phrase = CoreHintPhraseSrc?.Invoke();
        return string.IsNullOrWhiteSpace(phrase) ? null : phrase;
    }

    internal virtual void AssignPullSrcEngaged(bool engaged, object? cargo) { }

    internal WidgetElem[] DescendantsBackToFrontCapture()
    {
        if (_descendantsBackToFront is not null)
            return _descendantsBackToFront;

        _descendantsBackToFront = [.. _descendants];
        Array.Sort(
            _descendantsBackToFront,
            static (element, b) => element.ZOrder.CompareTo(b.ZOrder));
        return _descendantsBackToFront;
    }

    public virtual bool ConsumesDatChildren => false;

    internal WidgetElem[] DescendantsFrontToBackCapture()
    {
        if (_descendantsFrontToBack is not null)
            return _descendantsFrontToBack;

        WidgetElem[] backToFront = DescendantsBackToFrontCapture();
        _descendantsFrontToBack = new WidgetElem[backToFront.Length];
        for (int src = backToFront.Length - 1, dest = 0;
             src >= 0;
             --src, ++dest)
        {
            _descendantsFrontToBack[dest] = backToFront[src];
        }

        return _descendantsFrontToBack;
    }

    internal WidgetTrunk? SeekTrunk()
    {
        WidgetElem element = this;
        while (element.Ancestor is not null) element = element.Ancestor;
        return element as WidgetTrunk;
    }

    protected virtual void OnPaint(WidgetRenderScope cx) { }

    protected virtual void OnPaintFollowingDescendants(WidgetRenderScope cx) { }

    protected virtual void OnPaintTopLayer(WidgetRenderScope cx) { }

    protected virtual void OnBeat(double diffSecs) { }

    protected virtual void OnTurnedOnAltered() { }

    protected virtual bool ClipsDescendants => true;

    protected virtual bool ExpandsClipForPopup => false;

    protected virtual bool OnStrikeTest(float ownX, float ownY)
        => ownX >= 0f && ownX < Width && ownY >= 0f && ownY < Height;

    internal void PaintSelfAndDescendants(WidgetRenderScope cx)
    {
        if (!Visible) return;

        cx.PushXform(Left, Top);
        cx.PushAlpha(Opacity);
        bool clipsDescendants = ClipsDescendants;
        if (clipsDescendants)
            cx.PushClip(0f, 0f, Width, Height);
        try
        {
            if (!cx.LatestClipIsVacant)
            {
                OnPaint(cx);

                for (int idx = 0; idx < _descendants.Count; ++idx)
                    _descendants[idx].ImposeMooring(Width, Height);

                if (_descendants.Count > 0)
                {
                    WidgetElem[] sequenced = DescendantsBackToFrontCapture();
                    for (int idx = 0; idx < sequenced.Length; ++idx)
                        sequenced[idx].PaintSelfAndDescendants(cx);
                }

                OnPaintFollowingDescendants(cx);
            }
        }
        finally
        {
            if (clipsDescendants)
                cx.TakeClip();
            cx.TakeAlpha();
            cx.TakeXform();
        }
    }

    public Func<string?>? CoreHintPhraseSrc { get; set; }

    internal void PaintTopLayers(WidgetRenderScope cx)
    {
        if (!Visible) return;
        cx.PushXform(Left, Top);
        cx.PushAlpha(Opacity);
        try
        {
            if (ExpandsClipForPopup)
            {
                cx.PushClipUnbounded();
                try { OnPaintTopLayer(cx); }
                finally { cx.TakeClip(); }
            }
            else
            {
                OnPaintTopLayer(cx);
            }
            if (_descendants.Count > 0)
            {
                bool clipsDescendants = ClipsDescendants;
                if (clipsDescendants)
                    cx.PushClip(0f, 0f, Width, Height);
                try
                {
                    WidgetElem[] sequenced = DescendantsBackToFrontCapture();
                    for (int idx = 0; idx < sequenced.Length; ++idx)
                        sequenced[idx].PaintTopLayers(cx);
                }
                finally
                {
                    if (clipsDescendants)
                        cx.TakeClip();
                }
            }
        }
        finally
        {
            cx.TakeAlpha();
            cx.TakeXform();
        }
    }

    internal void PulseSelfAndDescendants(double dt)
    {
        if (ShownSrc is { } vis)
            Visible = vis();
        if (!Visible) return;
        if (TurnedOnSrc is { } turnedOn)
            Enabled = turnedOn();
        OnBeat(dt);
        for (int idx = 0; idx < _descendants.Count; ++idx)
            _descendants[idx].PulseSelfAndDescendants(dt);
    }

    internal WidgetElem? HitTest(float ownX, float ownY)
    {
        if (!Visible || !Enabled) return null;
        if (ClipsDescendants
            && (ownX < 0f || ownX >= Width || ownY < 0f || ownY >= Height))
            return null;

        if (_descendants.Count > 0)
        {
            WidgetElem[] sequenced = DescendantsFrontToBackCapture();
            for (int idx = 0; idx < sequenced.Length; ++idx)
            {
                WidgetElem element = sequenced[idx];
                WidgetElem? descendantStrike = element.HitTest(ownX - element.Left, ownY - element.Top);
                if (descendantStrike is not null) return descendantStrike;
            }
        }

        return ClickThrough ? null : OnStrikeTest(ownX, ownY) ? this : null;
    }

    internal void ImposeMooring(float ancestorW, float ancestorH)
    {
        if (ArrangementRule is not null)
        {
            WidgetPixelRect latest = WidgetPixelRect.FromLocusAndDims(
                (int)Left,
                (int)Top,
                (int)Width,
                (int)Height);
            WidgetPixelRect ancestor = WidgetPixelRect.FromLocusAndDims(0, 0, (int)ancestorW, (int)ancestorH);
            WidgetPixelRect upcoming = ArrangementRule.Apply(latest, ancestor);
            Left = upcoming.X0;
            Top = upcoming.Y0;
            Width = upcoming.Width;
            Height = upcoming.Height;
            return;
        }

        if (Moorings == MooringRims.None) return;
        if (!_mooringGrabbed)
        {
            _amL = Left; _amT = Top;
            _amR = ancestorW - (Left + Width);
            _amB = ancestorH - (Top + Height);
            _aw0 = Width; _ah0 = Height;
            _mooringGrabbed = true;
        }
        var (x, y, w, h) = CalculateAnchoredRect(Moorings, _amL, _amT, _amR, _amB, _aw0, _ah0, ancestorW, ancestorH);
        Left = x; Top = y; Width = w; Height = h;
    }

    internal void RestartMooringGrab()
    {
        _mooringGrabbed = false;
        if (ArrangementRule is null || Ancestor is null) return;

        ArrangementRule.Rebase(
            WidgetPixelRect.FromLocusAndDims(
                (int)Left,
                (int)Top,
                (int)Width,
                (int)Height),
            WidgetPixelRect.FromLocusAndDims(
                0,
                0,
                (int)Ancestor.Width,
                (int)Ancestor.Height));
    }

    internal void GrabLatestMooringBaseline()
    {
        if (Ancestor is null || Moorings == MooringRims.None) return;
        _mooringGrabbed = false;
        ImposeMooring(Ancestor.Width, Ancestor.Height);
    }

    internal void RebaseDescendantArrangementBaselines()
    {
        foreach (var descendant in _descendants)
            descendant.RestartMooringGrab();
    }

    private void DirtyDescendantOrdering()
    {
        _descendantsBackToFront = null;
        _descendantsFrontToBack = null;
    }
}

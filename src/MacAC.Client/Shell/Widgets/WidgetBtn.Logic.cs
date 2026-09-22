using System.Numerics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class WidgetBtn
{
    public uint GearPullAdmitSprite { get; set; }

    public uint GearPullRejectSprite { get; set; }

    internal GearDragAcceptance GearPullAcceptanceForTest { get; private set; }

    public uint ElementId => _details.Id;

    public string? Label { get; set; }

    public WidgetDatFont? LabelFont { get; set; }

    public string? TooltipText { get; set; }

    public bool Outline { get; set; }

    public float FaceLeft { get; set; }

    public float FaceTop { get; set; }

    public float FaceWidth { get; set; }

    public float FaceHeight { get; set; }

    public uint? FaceFileOverride { get; set; }

    public Func<uint>? TintTagFaceLocator { get; set; }

    public (float X, float Y, float Width, float Height)? LabelBox { get; set; }

    public string? ValCaption { get; set; }

    public WidgetDatFont? ValTypeface { get; set; }

    public (float X, float Y, float Width, float Height)? ValBbox { get; set; }

    public bool RolloverTurnedOn { get; }

    public bool HotPressTurnedOn { get; }

    public float HotPressStartingDelay { get; }

    public float HotPressRepeatInterval { get; }

    public bool SuppressSelfFlip { get; set; }

    public bool DriverOwnsVisualPhase { get; set; }

    public bool Selected
    {
        get => _chosen;
        set
        {
            if (_chosen == value) return;
            _chosen = value;
            RefreshVisualPhase();
        }
    }

    public string EngagedCondition
    {
        get; set
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
                return;
            field = value;
            _engagedPhaseBegunAt = Panels.WidgetMediaClock.Secs;
        }
    } = "";

    public uint EngagedCanonPhaseIdent
    {
        get
        {
            if (string.IsNullOrEmpty(EngagedCondition))
                return WidgetStateInfo.StraightPhaseIdent;
            foreach (var (ident, phase) in _mediaDetails.States)
                if (string.Equals(phase.Name, EngagedCondition, StringComparison.Ordinal))
                    return ident;
            return WidgetButtonStateMachine.TryPhaseTag(EngagedCondition, out uint standard)
                ? standard
                : CanonWidgetStateIds.TryPhaseIdent(EngagedCondition, out uint custom) ? custom : 0u;
        }
    }

    public override string EngagedCurPhaseLabel => EngagedCondition;

    public override bool ConsumesDatChildren => true;

    public override bool HndsPress => true;

    public override string? FetchHintPhrase() =>
        string.IsNullOrWhiteSpace(TooltipText) ? null : TooltipText;

    public bool TryFetchEnumAttr(uint propIdent, out uint val)
    {
        if (_details.TryFetchNetProp(propIdent, out var prop)
            && prop.Kind == WidgetPropertyKind.Enum)
        {
            val = checked((uint)prop.UnsignedValue);
            return true;
        }

        val = 0;
        return false;
    }

    public bool TrySetCanonPhase(uint phaseIdent)
    {
        if (ToggleBehavior && phaseIdent is WidgetButtonStateMachine.Normal or WidgetButtonStateMachine.Highlight)
        {
            Selected = phaseIdent == WidgetButtonStateMachine.Highlight;
            ImposePhaseVis(phaseIdent);
            return true;
        }
        if (phaseIdent == WidgetButtonStateMachine.Ghosted)
        {
            Enabled = false;
            ImposePhaseVis(phaseIdent);
            return true;
        }
        if (!Enabled && phaseIdent != WidgetButtonStateMachine.Ghosted)
            Enabled = true;

        if (phaseIdent == WidgetStateInfo.StraightPhaseIdent)
        {
            if (!HasPhaseMedia(""))
                return false;
            EngagedCondition = "";
            ImposePhaseVis(phaseIdent);
            CascadePhaseToDescendants(phaseIdent);
            return true;
        }
        if (TrySeekPhase(phaseIdent, out var phase))
        {
            EngagedCondition = phase.Name;
            ImposePhaseVis(phaseIdent);
            CascadePhaseToDescendants(phaseIdent);
            return true;
        }
        string phaseLabel = WidgetButtonStateMachine.PhaseMoniker(phaseIdent);
        if (string.IsNullOrEmpty(phaseLabel))
            phaseLabel = CanonWidgetStateIds.PhaseLabel(phaseIdent);
        if (!string.IsNullOrEmpty(phaseLabel) && HasPhaseMedia(phaseLabel))
        {
            EngagedCondition = phaseLabel;
            ImposePhaseVis(phaseIdent);
            CascadePhaseToDescendants(phaseIdent);
            return true;
        }
        return false;
    }

    internal int FaceSegmentTally => _faceSegments.Length;

    internal IReadOnlyList<WidgetPixelRect> FrontSegmentRectsForTest()
        => _faceSegments.Select(segment => segment.Rect(Width, Height)).ToArray();

    internal void AssignPerPhaseCaptionStyling(
        IReadOnlyDictionary<uint, Vector4>? tints,
        IReadOnlyDictionary<uint, bool>? outlines)
    {
        _phaseCaptionTints = tints;
        _phaseCaptionOutlines = outlines;
        ImposePerPhaseCaptionStyling(CalculateAskedPhaseIdent());
    }

    private static string UpcomingMediaPhase(
        ElemDetails details,
        uint sealedIdent,
        string sealedLabel,
        string latest)
    {
        if (details.States.TryGetValue(sealedIdent, out WidgetStateInfo? phase))
            return HasImageMedia(details, phase, sealedLabel) ? sealedLabel : latest;
        if (details.StateMedia.ContainsKey(sealedLabel))
            return sealedLabel;
        return details.States.TryGetValue(
                WidgetStateInfo.StraightPhaseIdent, out WidgetStateInfo? basePhase)
            ? HasImageMedia(details, basePhase, "") ? "" : latest
            : details.StateMedia.ContainsKey("") ? "" : latest;
    }

    private static uint EngagedFile(ElemDetails mediaDetails, string mediaPhase)
    {
        return mediaDetails.StateMedia.TryGetValue(mediaPhase, out var m) ? m.File : 0u;
    }

    public bool ToggleBehavior { get; }

    // The frame this element's active state is showing right now, and the state its sequence hands off
    // to when it ends
    private uint MovingFile(ElemDetails mediaDetails, string mediaPhase, out uint? handOff)
    {
        handOff = null;
        if (!TrySeekPhaseNamed(mediaDetails, mediaPhase, out WidgetStateInfo? phase)
            || !Panels.WidgetMediaSequence.IsMoving(phase!.MediaSteps))

            return EngagedFile(mediaDetails, mediaPhase);

        (uint file, uint? changeover) = Panels.WidgetMediaSequence.Sample(
            phase.MediaSteps,
            (float)(Panels.WidgetMediaClock.Secs - _engagedPhaseBegunAt));
        handOff = changeover;
        return file is not 0u ? file : EngagedFile(mediaDetails, mediaPhase);
    }

    private bool ContainsOwn(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height;

    private void CascadePhaseToDescendants(uint phaseIdent)
    {
        if (Children.Count is 0)
            return;
        if (!TrySeekPhase(phaseIdent, out WidgetStateInfo phase) || !phase.PassToChildren)
            return;
        foreach (WidgetElem descendant in Children)
            if (descendant is IWidgetDatStateful stateful)
                stateful.TrySetCanonPhase(phaseIdent);
    }

    private static bool TrySeekPhaseNamed(
        ElemDetails mediaDetails, string mediaPhase, out WidgetStateInfo? phase)
    {
        foreach (var (_, contender) in mediaDetails.States)
        {
            if (string.Equals(contender.Name, mediaPhase, StringComparison.Ordinal))
            {
                phase = contender;
                return true;
            }
        }
        phase = null;
        return false;
    }

    private bool TrySeekPhase(uint phaseIdent, out WidgetStateInfo phase)
    {
        if (_faceSegments.Length is 0)
            return _mediaDetails.States.TryGetValue(phaseIdent, out phase!);
        foreach (FacetPiece segment in _faceSegments)
            if (segment.Info.States.TryGetValue(phaseIdent, out phase!))
                return true;
        phase = null!;
        return false;
    }

    private void ImposePhaseVis(uint phaseIdent)
    {
        if (_details.States.TryGetValue(phaseIdent, out var phase)
            && phase.Properties.Values.TryGetValue(0x3Bu, out var invisible)
            && invisible.Kind == WidgetPropertyKind.Bool)

            Visible = !invisible.BoolValue;
    }

    private void ImposePerPhaseCaptionStyling(uint askedPhaseIdent)
    {
        if (_phaseCaptionTints is { } tints && tints.TryGetValue(askedPhaseIdent, out Vector4 tint))
            CaptionColor = tint;
        if (_phaseCaptionOutlines is { } outlines && outlines.TryGetValue(askedPhaseIdent, out bool outline))
            Outline = outline;
    }

    private void SynchronizeMediaPhases()
    {
        if (string.Equals(EngagedCondition, _previousMediaSealPhase, StringComparison.Ordinal))
            return;
        uint sealedIdent = EngagedCanonPhaseIdent;
        if (_faceSegments.Length is 0)
        {
            _faceMediaPhase = UpcomingMediaPhase(
                _mediaDetails, sealedIdent, EngagedCondition, _faceMediaPhase);
        }
        else
        {
            for (int idx = 0; idx < _faceSegments.Length; ++idx)
            {
                _segmentMediaPhases[idx] = UpcomingMediaPhase(
                    _faceSegments[idx].Info,
                    sealedIdent,
                    EngagedCondition,
                    _segmentMediaPhases[idx]);
            }
        }
        _previousMediaSealPhase = EngagedCondition;
    }

    private static bool HasImageMedia(
        ElemDetails details,
        WidgetStateInfo phase,
        string phaseLabel)
    {
        return phase.ImageMediaCount is not 0 || details.StateMedia.ContainsKey(phaseLabel);
    }

    private bool HasPhaseMedia(string phaseLabel)
    {
        if (_faceSegments.Length is 0)
            return _mediaDetails.StateMedia.ContainsKey(phaseLabel);
        foreach (FacetPiece segment in _faceSegments)
            if (segment.Info.StateMedia.ContainsKey(phaseLabel))
                return true;
        return false;
    }

    private void PaintChunkCaption(
        WidgetRenderScope cx,
        string phrase,
        WidgetDatFont typeface,
        Vector4 tint,
        float bboxX,
        float bboxY,
        float bboxWidth,
        float bboxHeight,
        CaptionAlignment align,
        float leftShift)
    {
        var strokes = EncloseChunkStrokes(
            phrase, typeface.MeasureWidth, typeface.LineHeight,
            bboxX, bboxY, bboxWidth, bboxHeight, align, leftShift);

        bool clip = strokes.Count > 1;
        if (clip)
            cx.PushClip(bboxX, bboxY, bboxWidth, bboxHeight);
        try
        {
            foreach ((string stroke, float tx, float ty) in strokes)
                cx.PaintStringDat(typeface, stroke, tx, ty, tint, Outline, OutlineColor);
        }
        finally
        {
            if (clip)
                cx.TakeClip();
        }
    }

    private void PaintFace(WidgetRenderScope cx, uint file, WidgetPixelRect rect)
    {
        if (file is 0 || rect.Width <= 0 || rect.Height <= 0)
            return;
        var (texture, textureWidth, textureHeight) = _locate(file);
        if (texture is 0 || textureWidth is 0 || textureHeight is 0)
            return;

        cx.SketchSprite(texture, rect.X0, rect.Y0, rect.Width, rect.Height,
            0f, 0f, (float)rect.Width / textureWidth, (float)rect.Height / textureHeight,
            Tint);
    }

    private void RefreshVisualPhase()
    {
        if (DriverOwnsVisualPhase)
            return;

        uint asked = CalculateAskedPhaseIdent();
        if (_hasCustomPickDuo)
        {
            EngagedCondition = CanonWidgetStateIds.PhaseLabel(asked);
            ImposePerPhaseCaptionStyling(asked);
            CascadePhaseToDescendants(asked);
            return;
        }

        string askedLabel = WidgetButtonStateMachine.PhaseMoniker(asked);
        bool authored = _details.States.TryGetValue(
            asked, out WidgetStateInfo? committed);
        if (!authored && !HasPhaseMedia(askedLabel))
            return;

        EngagedCondition = authored && !string.IsNullOrEmpty(committed!.Name)
            ? committed.Name
            : askedLabel;
        ImposePerPhaseCaptionStyling(asked);
        CascadePhaseToDescendants(asked);
    }

    private uint CalculateAskedPhaseIdent()
    {
        return _hasCustomPickDuo
                ? (_chosen ? CanonWidgetStateIds.Selected : CanonWidgetStateIds.Unselected)
                : WidgetButtonStateMachine.AskedPhase(new WidgetButtonVisualInput(
                    Disabled: !Enabled,
                    Selected: _chosen,
                    RolloverEnabled: RolloverTurnedOn,
                    Pressed: _pressed,
                    PointerOver: _ptrOver));
    }
}

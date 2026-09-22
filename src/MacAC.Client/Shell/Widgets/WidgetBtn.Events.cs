using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetBtn
{
    public Action? OnClick { get; set; }

    public Action? OnDoublePress { get; set; }

    public Action? OnRightPress { get; set; }

    public Action? OnPressed { get; set; }

    public Action? OnReleased { get; set; }

    public Action<int, int>? OnClickAt { get; set; }

    public Func<GearDragPayload, GearDragAcceptance>? OnGearPullOver { get; set; }

    public Action<GearDragPayload>? OnGearDiscard { get; set; }

    public override bool OnSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.HoverJoin:
                _ptrOver = true;
                RefreshVisualPhase();
                return true;
            case WidgetEventType.HoverDepart:
                _ptrOver = false;
                RefreshVisualPhase();
                return true;
            case WidgetEventType.PointerDown:
                _ptrX = e.Data1;
                _ptrY = e.Data2;
                _ptrOver = ContainsOwn(e.Data1, e.Data2);
                _pressed = true;
                RefreshVisualPhase();
                if (Enabled)
                    OnPressed?.Invoke();
                if (HotPressTurnedOn && Enabled)
                {
                    OnClick?.Invoke();
                    OnClickAt?.Invoke(_ptrX, _ptrY);
                    _hotClicking = true;
                    _upcomingHotPressMoment = double.NaN;
                }
                return true;
            case WidgetEventType.PointerRelocate:
                if (_pressed)
                {
                    _ptrX = e.Data1;
                    _ptrY = e.Data2;
                    _ptrOver = ContainsOwn(e.Data1, e.Data2);
                    RefreshVisualPhase();
                    return true;
                }
                return false;
            case WidgetEventType.PointerUp:
                _ptrX = e.Data1;
                _ptrY = e.Data2;
                _ptrOver = ContainsOwn(e.Data1, e.Data2);
                _suppressUpcomingPress = _hotClicking && _ptrOver;
                _hotClicking = false;
                _upcomingHotPressMoment = double.NaN;
                if (_pressed && _ptrOver && Enabled && ToggleBehavior && !SuppressSelfFlip)
                    _chosen = !_chosen;
                if (_pressed && Enabled)
                    OnReleased?.Invoke();
                _pressed = false;
                RefreshVisualPhase();
                return true;
            case WidgetEventType.Click:
                if (!Enabled) return true;
                if (_suppressUpcomingPress)
                {
                    _suppressUpcomingPress = false;
                    return true;
                }
                OnClick?.Invoke();
                OnClickAt?.Invoke(e.Data1, e.Data2);
                return OnClick is not null || OnClickAt is not null;
            case WidgetEventType.DoublePress:
                if (OnDoublePress is null) return false;
                if (!Enabled) return true;
                OnDoublePress.Invoke();
                return true;
            case WidgetEventType.RightPress:
                if (OnRightPress is null) return false;
                if (!Enabled) return true;
                OnRightPress.Invoke();
                return true;
            case WidgetEventType.PullJoin:
                GearPullAcceptanceForTest = e.Payload is GearDragPayload cargo
                    ? OnGearPullOver?.Invoke(cargo) ?? GearDragAcceptance.None
                    : GearDragAcceptance.None;
                return OnGearPullOver is not null;
            case WidgetEventType.PullOver:
                GearPullAcceptanceForTest = GearDragAcceptance.None;
                return OnGearPullOver is not null;
            case WidgetEventType.DiscardReleased:
                GearPullAcceptanceForTest = GearDragAcceptance.None;
                if (e.Payload is GearDragPayload dropped)
                    OnGearDiscard?.Invoke(dropped);
                return OnGearDiscard is not null;
            default:
                return false;
        }
    }

    public void OnGlobalWidgetMoment(double instantSecs)
    {
        if (!_hotClicking || !HotPressTurnedOn)
            return;

        if (double.IsNaN(_upcomingHotPressMoment))
            _upcomingHotPressMoment = instantSecs + HotPressStartingDelay;

        if (!_ptrOver && instantSecs >= _upcomingHotPressMoment)
        {
            _upcomingHotPressMoment = instantSecs;
            return;
        }

        if (_ptrOver && instantSecs >= _upcomingHotPressMoment)
        {
            OnClick?.Invoke();
            OnClickAt?.Invoke(_ptrX, _ptrY);
            _upcomingHotPressMoment += HotPressRepeatInterval;
        }
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        SynchronizeMediaPhases();

        uint? queuedHandOff = null;
        if (_faceSegments.Length is not 0)
        {
            for (int idx = 0; idx < _faceSegments.Length; ++idx)
            {
                FacetPiece segment = _faceSegments[idx];
                uint cycle = MovingFile(
                    segment.Info, _segmentMediaPhases[idx], out uint? segmentHandOff);
                PaintFace(cx, cycle, segment.Rect(Width, Height));
                queuedHandOff ??= segmentHandOff;
            }
        }
        else if (TintTagFaceLocator is { } tintTagLocator)
        {
            uint bakedTexture = tintTagLocator();
            if (bakedTexture is not 0)
            {
                float faceWidth = FaceWidth > 0f ? FaceWidth : Width;
                float faceHeight = FaceHeight > 0f ? FaceHeight : Height;
                cx.SketchSprite(bakedTexture, FaceLeft, FaceTop, faceWidth, faceHeight,
                    0f, 0f, 1f, 1f, Vector4.One);
            }
        }
        else
        {
            uint file = FaceFileOverride
                ?? MovingFile(_mediaDetails, _faceMediaPhase, out queuedHandOff);
            if (file is not 0)
            {
                var (bmp, tw, th) = _locate(file);
                if (bmp is not 0 && tw is not 0 && th is not 0)
                {
                    float faceWidth = FaceWidth > 0f ? FaceWidth : Width;
                    float faceHeight = FaceHeight > 0f ? FaceHeight : Height;
                    cx.SketchSprite(bmp, FaceLeft, FaceTop, faceWidth, faceHeight,
                        0, 0, faceWidth / tw, faceHeight / th, Tint);
                }
            }
        }

        if (queuedHandOff is { } handOffPhase)
            TrySetCanonPhase(handOffPhase);

        if (Label is { Length: > 0 } caption && LabelFont is { } font)
        {
            float bboxX = LabelBox?.X ?? 0f;
            float bboxY = LabelBox?.Y ?? 0f;
            float bboxWidth = LabelBox?.Width ?? Width;
            float bboxHeight = LabelBox?.Height ?? Height;

            if (ValBbox is { X: var valBboxX } && valBboxX > bboxX)
                bboxWidth = MathF.Min(bboxWidth, valBboxX - bboxX);

            PaintChunkCaption(cx, caption, font, CaptionColor, bboxX, bboxY, bboxWidth, bboxHeight, CaptionAlign, CaptionShiftX);
        }

        if (ValCaption is { Length: > 0 } val && ValTypeface is { } vf)
        {
            float bboxX = ValBbox?.X ?? 0f;
            float bboxY = ValBbox?.Y ?? 0f;
            float bboxWidth = ValBbox?.Width ?? Width;
            float bboxHeight = ValBbox?.Height ?? Height;
            float valWidth = vf.MeasureWidth(val);
            float vx = ValAlign switch
            {
                CaptionAlignment.Left => bboxX + CaptionShiftX,
                CaptionAlignment.Right => bboxX + bboxWidth - valWidth,
                _ => bboxX + (bboxWidth - valWidth) * 0.5f,
            };
            float vy = bboxY + (bboxHeight - vf.LineHeight) * 0.5f;
            cx.PaintStringDat(vf, val, vx, vy, ValTint, Outline, OutlineColor);
        }

        uint pullSprite = GearPullAcceptanceForTest switch
        {
            GearDragAcceptance.Accept => GearPullAdmitSprite,
            GearDragAcceptance.Reject => GearPullRejectSprite,
            _ => 0u,
        };
        if (pullSprite is not 0)
        {
            var (bmp, _, _) = _locate(pullSprite);
            if (bmp is not 0)
                cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Tint);
        }
    }

    protected override void OnTurnedOnAltered()
    {
        if (!Enabled)
        {
            _pressed = false;
            _hotClicking = false;
            _upcomingHotPressMoment = double.NaN;
        }
        RefreshVisualPhase();
    }
}

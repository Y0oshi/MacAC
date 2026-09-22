using System.Numerics;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell;

public readonly record struct WidgetRadarBlip(
    uint ObjectId,
    string Name,
    float PixelX,
    float PixelY,
    Vector4 Color,
    RadarBlipGlyph Shape,
    bool Selected = false);

public sealed record WidgetRadarCapture(
    float PlayerHeadingDegrees,
    IReadOnlyList<WidgetRadarBlip> Blips,
    string? CoordinatesText,
    bool BlankBlips = false,
    bool UiLocked = false)
{
    public static WidgetRadarCapture Empty { get; } = new(
        0f,
        Array.Empty<WidgetRadarBlip>(),
        null);
}

public sealed class UiRadar : WidgetElem
{
    public const uint CanonClassIdent = 0x10000010u;
    public const float CanonRenewSecs = CanonRadar.RefreshIntervalSecs;
    public const float HoverRadiusPx = 6f;

    private static readonly Vector4 AvatarMarkerTint = new(0f, 1f, 0f, 1f);
    private double _renewAccumulator;
    private string? _hoveredObjectLabel;

    public Vector2 Center { get; set; } = new(60f, 60f);

    public Func<WidgetRadarCapture>? CaptureSupplier { get; set; }

    public Action<uint>? PickObject { get; set; }

    public Action<uint?>? HoveredObjectAltered { get; set; }

    public WidgetRadarCapture Snapshot { get; private set; } = WidgetRadarCapture.Empty;
    public uint? HoveredObjectIdent { get; private set; }

    public override bool HndsPress => true;

    public void ImposeCapture(WidgetRadarCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Snapshot = capture;

        if (HoveredObjectIdent is uint hovered)
        {
            bool stillPresent = false;
            for (int idx = 0; idx < capture.Blips.Count; ++idx)
            {
                if (capture.Blips[idx].ObjectId == hovered)
                {
                    stillPresent = true;
                    break;
                }
            }

            if (!stillPresent)
                AssignHovered(null, null);
        }
    }

    public void Refresh()
    {
        if (CaptureSupplier is { } supplier)
            ImposeCapture(supplier() ?? WidgetRadarCapture.Empty);
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.HoverDepart)
        {
            AssignHovered(null, null);
            return false;
        }

        if (e.Type == WidgetEventType.Click && HoveredObjectIdent is uint oid)
        {
            PickObject?.Invoke(oid);
            return PickObject is not null;
        }

        return false;
    }

    public override string? FetchHintPhrase() => _hoveredObjectLabel;

    protected override void OnBeat(double diffSecs)
    {
        if (CaptureSupplier is null)
            return;

        _renewAccumulator += diffSecs;
        if (_renewAccumulator < CanonRenewSecs)
            return;

        _renewAccumulator %= CanonRenewSecs;
        Refresh();
    }

    protected override bool OnStrikeTest(float ownX, float ownY)
    {
        bool inside = base.OnStrikeTest(ownX, ownY);
        if (!inside)
        {
            AssignHovered(null, null);
            return false;
        }

        RefreshHoveredBlip(ownX, ownY);
        return true;
    }

    protected override void OnPaintFollowingDescendants(WidgetRenderScope ctx)
    {
        if (!Snapshot.BlankBlips)
        {
            for (int idx = 0; idx < Snapshot.Blips.Count; ++idx)
                PaintBlip(ctx, Snapshot.Blips[idx]);
        }

        int cx = RoundPixel(Center.X);
        int cy = RoundPixel(Center.Y);
        PaintPx(ctx, cx, cy, AvatarMarkerTint, CanonRadar.AvatarMarkerPx);
    }

    private void RefreshHoveredBlip(float ownX, float ownY)
    {
        uint? closestIdent = null;
        string? closestLabel = null;
        float closestGapSquared = HoverRadiusPx * HoverRadiusPx;

        if (!Snapshot.BlankBlips)
        {
            for (int idx = 0; idx < Snapshot.Blips.Count; ++idx)
            {
                WidgetRadarBlip blip = Snapshot.Blips[idx];
                float dx = ownX - blip.PixelX;
                float dy = ownY - blip.PixelY;
                float gapSquared = dx * dx + dy * dy;
                if (gapSquared <= closestGapSquared)
                {
                    closestGapSquared = gapSquared;
                    closestIdent = blip.ObjectId;
                    closestLabel = blip.Name;
                }
            }
        }

        AssignHovered(closestIdent, closestLabel);
    }

    private void AssignHovered(uint? ident, string? label)
    {
        if (HoveredObjectIdent == ident && _hoveredObjectLabel == label)
            return;

        HoveredObjectIdent = ident;
        _hoveredObjectLabel = label;
        HoveredObjectAltered?.Invoke(ident);
    }

    private static void PaintBlip(WidgetRenderScope cx, in WidgetRadarBlip blip)
    {
        int x = RoundPixel(blip.PixelX);
        int y = RoundPixel(blip.PixelY);
        PaintPx(cx, x, y, blip.Color, CanonRadar.FetchBlipPx(blip.Shape));

        if (blip.Selected)
            PaintPx(cx, x, y, blip.Color, CanonRadar.PickPx);
    }

    private static void PaintPx(
        WidgetRenderScope cx,
        int middleX,
        int middleY,
        Vector4 tint,
        ReadOnlySpan<RadarPixelShift> shifts)
    {
        for (int idx = 0; idx < shifts.Length; ++idx)
            cx.SketchPopulate(middleX + shifts[idx].X, middleY + shifts[idx].Y, 1, 1, tint);
    }

    private static int RoundPixel(float val)
        => checked((int)MathF.Round(val, MidpointRounding.ToEven));
}

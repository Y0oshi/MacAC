using System.Numerics;
using MacAC.Assets;
using MacAC.Mechanics.Shell;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public readonly record struct VividMarkDetails(
    Vector3 SelectionSphereCenter,
    float SelectionSphereRadius,
    uint ItemType,
    uint ObjectDescriptionFlags);

public sealed record VividTargetEngineWiring(
    PickPhase Selection,
    Func<uint> PlayerGuid,
    Func<bool> Enabled,
    Func<uint, VividMarkDetails?> ResolveTarget,
    Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> Camera);

public sealed class VividTargetIndicatorDriver
{
    private const uint ClientEnumBucket = 0x10000009u;
    private const uint LeadSrcImageEnum = 1u;
    private const int SrcImageTally = 12;
    private const float ViewRectMargin = 8f;

    private readonly WidgetBoard _onMonitor;
    private readonly WidgetTextureElement[] _corners;
    private readonly WidgetTextureElement _offMonitor;
    private readonly VividTargetEngineWiring _bindings;
    private readonly VividMarkOrigin[] _sources;
    private readonly (float Width, float Height)[] _cornerDimsList;

    private VividTargetIndicatorDriver(
        WidgetBoard onMonitor,
        WidgetTextureElement[] corners,
        WidgetTextureElement offMonitor,
        VividMarkOrigin[] srcs,
        VividTargetEngineWiring mappings)
    {
        _onMonitor = onMonitor;
        _corners = corners;
        _offMonitor = offMonitor;
        _sources = srcs;
        _cornerDimsList = [.. srcs.Take(4).Select(src => (src.Width, src.Height))];
        _bindings = mappings;
    }

    public static VividTargetIndicatorDriver? Mount(
        WidgetTrunk hub,
        CanonWidgetAssets holdings,
        VividTargetEngineWiring mappings)
    {
        WidgetBoard onMonitor = new WidgetBoard
        {
            Name = "VividTargetIndicatorOnScreen",
            BackgroundColor = Vector4.Zero,
            BorderTint = Vector4.Zero,
            ClickThrough = true,
            Visible = false,
            ZOrder = -10_000,
            Moorings = MooringRims.None,
        };

        VividMarkOrigin[] srcs = new VividMarkOrigin[SrcImageTally];
        for (uint idx = 0; idx < SrcImageTally; ++idx)
        {
            uint did;
            lock (holdings.DatLock)
                did = CanonDataIdResolver.Resolve(
                    holdings.Dats,
                    enumVal: LeadSrcImageEnum + idx,
                    enumBucket: ClientEnumBucket);
            if (did is 0u)
                return null;

            var settled = holdings.ResolveSprite(did);
            if (settled.Texture is 0u || settled.Width <= 0 || settled.Height <= 0)
                return null;
            srcs[idx] = new VividMarkOrigin(
                settled.Texture, settled.Width, settled.Height);
        }

        WidgetTextureElement[] corners = new WidgetTextureElement[4];
        for (int idx = 0; idx < corners.Length; ++idx)
        {
            MountLoop(srcs, idx, corners, onMonitor);
        }

        WidgetTextureElement offMonitor = new WidgetTextureElement
        {
            Name = "VividTargetIndicatorOffScreen",
            Visible = false,
            ClickThrough = true,
            ZOrder = -10_000,
            Moorings = MooringRims.None,
        };

        hub.AddChild(onMonitor);
        hub.AddChild(offMonitor);
        return new VividTargetIndicatorDriver(
            onMonitor, corners, offMonitor, srcs, mappings);
    }

    private static void MountLoop(VividMarkOrigin[] srcs, int idx, WidgetTextureElement[] corners, WidgetBoard onMonitor)
    {
        var src = srcs[idx];
        corners[idx] = new WidgetTextureElement
        {
            Name = $"VividTargetCorner{idx + 1}",
            Texture = src.Texture,
            Width = src.Width,
            Height = src.Height,
            ClickThrough = true,
            Moorings = MooringRims.None,
        };
        onMonitor.AddChild(corners[idx]);
    }

    public void Tick()
    {
        if (!_bindings.Enabled()
            || _bindings.Selection.ChosenObjectTag is not uint oid
            || oid is 0u
            || oid == _bindings.PlayerGuid()
            || _bindings.ResolveTarget(oid) is not VividMarkDetails mark)
        {
            Hide();
            return;
        }

        var cam = _bindings.Camera();
        var proj = CalculateProj(
            mark.SelectionSphereCenter,
            mark.SelectionSphereRadius,
            cam.View,
            cam.Projection,
            cam.Viewport);
        if (proj.Status == VividTargetMirrorStatus.Invalid)
        {
            Hide();
            return;
        }

        var color = RadarBlipPalette.For(
            mark.ItemType, mark.ObjectDescriptionFlags);
        Vector4 tint = new Vector4(color.Red, color.Green, color.Blue, color.Alpha);
        if (proj.Status == VividTargetMirrorStatus.OnScreen)
        {
            var arrangement = CalculateArrangement(
                proj.RectMin, proj.RectMax, cam.Viewport, _cornerDimsList);
            _onMonitor.Left = arrangement.RootPosition.X;
            _onMonitor.Top = arrangement.RootPosition.Y;
            _onMonitor.Width = arrangement.RootSize.X;
            _onMonitor.Height = arrangement.RootSize.Y;
            for (int idx = 0; idx < _corners.Length; ++idx)
            {
                AssignCorner(idx, arrangement.CornerPositions[idx].X, arrangement.CornerPositions[idx].Y);
                _corners[idx].Tint = tint;
            }

            _offMonitor.Visible = false;
            _onMonitor.Visible = true;
            return;
        }

        uint imageEnum = PickOffMonitorImageEnum(proj.AngleDegrees);
        var offMonitorSrc = _sources[imageEnum - LeadSrcImageEnum];
        Vector2 locus = CalculateOffMonitorLocus(
            proj.AngleDegrees,
            cam.Viewport,
            new Vector2(offMonitorSrc.Width, offMonitorSrc.Height));
        _offMonitor.Texture = offMonitorSrc.Texture;
        _offMonitor.Left = locus.X;
        _offMonitor.Top = locus.Y;
        _offMonitor.Width = offMonitorSrc.Width;
        _offMonitor.Height = offMonitorSrc.Height;
        _offMonitor.Tint = tint;
        _onMonitor.Visible = false;
        _offMonitor.Visible = true;
    }

    internal static VividTargetMirror CalculateProj(
        Vector3 realmMiddle,
        float realmRadius,
        Matrix4x4 lens,
        Matrix4x4 proj,
        Vector2 viewRect)
    {
        if (viewRect.X <= 0f || viewRect.Y <= 0f
            || !float.IsFinite(realmRadius) || realmRadius < 0f)
            return default;

        bool projected = ScreenMapping.TryProjectOrbToMonitorRect(
            realmMiddle,
            realmRadius,
            lens,
            proj,
            viewRect,
            out Vector2 rectLower,
            out Vector2 rectUpper,
            out _,
            lowerFlankPx: 0f);

        if (projected
            && rectUpper.X >= 0f && rectLower.X <= viewRect.X
            && rectUpper.Y >= 0f && rectLower.Y <= viewRect.Y)
        {
            return new VividTargetMirror(
                VividTargetMirrorStatus.OnScreen,
                rectLower,
                rectUpper,
                0f);
        }

        Vector3 own = Vector3.Transform(realmMiddle, lens);
        float len = own.Length();
        float angle = 0f;
        if (float.IsFinite(len) && len > 1e-6f)
        {
            angle = CalculateProjBranch(own, len);
        }

        return new VividTargetMirror(
            VividTargetMirrorStatus.OffScreen,
            default,
            default,
            angle);
    }

    private static float CalculateProjBranch(Vector3 own, float len)
    {
        float angle;
        float right = own.X / len;
        float ahead = -own.Z / len;
        float up = own.Y / len;
        float radians = ahead > 0f
                        ? MathF.Atan2(right, up)
                        : MathF.Atan2(right, 0f);
        angle = PositiveModulo(
                        450f - radians * (180f / MathF.PI),
                        360f);
        return angle;
    }

    internal static uint PickOffMonitorImageEnum(float angleDeg)
    {
        float angle = PositiveModulo(angleDeg, 360f);
        if (angle is >= 338f or < 23f) return 6u;
        if (angle < 68f) return 7u;
        if (angle < 113f) return 9u;
        if (angle < 158f) return 12u;
        if (angle < 203f) return 11u;
        return angle < 248f ? 10u : angle < 293f ? 8u : 5u;
    }

    internal static Vector2 CalculateOffMonitorLocus(
        float angleDeg,
        Vector2 viewRect,
        Vector2 imageDims)
    {
        float angleRadians = PositiveModulo(angleDeg, 360f) * (MathF.PI / 180f);
        Vector2 dir = new Vector2(MathF.Cos(angleRadians), -MathF.Sin(angleRadians));
        Vector2 halfImage = imageDims * 0.5f;
        Vector2 middle = viewRect * 0.5f;
        Vector2 floorMiddle = new Vector2(ViewRectMargin) + halfImage;
        Vector2 ceilingMiddle = viewRect - new Vector2(ViewRectMargin) - halfImage;

        float horizontalGap = dir.X switch
        {
            > 1e-6f => (ceilingMiddle.X - middle.X) / dir.X,
            < -1e-6f => (floorMiddle.X - middle.X) / dir.X,
            _ => float.PositiveInfinity,
        };
        float verticalGap = dir.Y switch
        {
            > 1e-6f => (ceilingMiddle.Y - middle.Y) / dir.Y,
            < -1e-6f => (floorMiddle.Y - middle.Y) / dir.Y,
            _ => float.PositiveInfinity,
        };
        float gap = MathF.Min(horizontalGap, verticalGap);
        if (!float.IsFinite(gap) || gap < 0f)
            gap = 0f;

        Vector2 locus = middle + dir * gap - halfImage;
        return new Vector2(
            Math.Clamp(locus.X, ViewRectMargin,
                MathF.Max(ViewRectMargin, viewRect.X - imageDims.X - ViewRectMargin)),
            Math.Clamp(locus.Y, ViewRectMargin,
                MathF.Max(ViewRectMargin, viewRect.Y - imageDims.Y - ViewRectMargin)));
    }

    internal static VividTargetArrangement CalculateArrangement(
        Vector2 rectLower,
        Vector2 rectUpper,
        Vector2 viewRect,
        IReadOnlyList<(float Width, float Height)> sizes)
    {
        if (sizes.Count is not 4)
            throw new ArgumentException("Retail VividTargetIndicator has precisely four corners", nameof(sizes));

        float cornerWidth = sizes[0].Width;
        float cornerHeight = sizes[0].Height;
        float outerLeft = rectLower.X - cornerWidth;
        float outerTop = rectLower.Y - cornerHeight;
        float outerRight = rectUpper.X;
        float outerBottom = rectUpper.Y;

        if (outerLeft > outerRight) outerLeft = outerRight - 1f;
        if (outerTop > outerBottom) outerTop = outerBottom - 1f;
        outerLeft = MathF.Max(outerLeft, ViewRectMargin);
        outerTop = MathF.Max(outerTop, ViewRectMargin);
        outerRight = MathF.Max(outerRight, cornerWidth + ViewRectMargin);
        outerBottom = MathF.Max(outerBottom, cornerHeight + ViewRectMargin);

        float ceilingRight = viewRect.X - cornerWidth - ViewRectMargin;
        float ceilingBottom = viewRect.Y - cornerHeight - ViewRectMargin;
        outerLeft = MathF.Min(outerLeft, ceilingRight - cornerWidth);
        outerTop = MathF.Min(outerTop, ceilingBottom - cornerHeight);
        outerRight = MathF.Min(outerRight, ceilingRight);
        outerBottom = MathF.Min(outerBottom, ceilingBottom);
        Vector2 trunkDims = new(
            MathF.Max(1f, outerRight - outerLeft + cornerWidth),
            MathF.Max(1f, outerBottom - outerTop + cornerHeight));
        return new VividTargetArrangement(
            new Vector2(outerLeft, outerTop),
            trunkDims,
            [
                Vector2.Zero,
                new Vector2(trunkDims.X - sizes[1].Width, 0f),
                new Vector2(trunkDims.X - sizes[2].Width, trunkDims.Y - sizes[2].Height),
                new Vector2(0f, trunkDims.Y - sizes[3].Height),
            ]);
    }

    private void Hide()
    {
        _onMonitor.Visible = false;
        _offMonitor.Visible = false;
    }

    private void AssignCorner(int ordinal, float left, float top)
    {
        _corners[ordinal].Left = left;
        _corners[ordinal].Top = top;
    }

    private static float PositiveModulo(float val, float modulus)
    {
        float outcome = val % modulus;
        return outcome < 0f ? outcome + modulus : outcome;
    }
}

internal readonly record struct VividTargetArrangement(
    Vector2 RootPosition,
    Vector2 RootSize,
    IReadOnlyList<Vector2> CornerPositions);

internal enum VividTargetMirrorStatus
{
    Invalid,
    OnScreen,
    OffScreen,
}

internal readonly record struct VividTargetMirror(
    VividTargetMirrorStatus Status,
    Vector2 RectMin,
    Vector2 RectMax,
    float AngleDegrees);

internal readonly record struct VividMarkOrigin(
    uint Texture,
    float Width,
    float Height);

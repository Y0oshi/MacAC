using System.Numerics;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

internal sealed class ProjectileDebugOverlayDriver
{
    private static readonly Vector4 WipeTint = new(0f, 1f, 0f, 0.95f);
    private static readonly Vector4 BlockedTint = new(1f, 0f, 0f, 0.95f);

    private readonly WidgetBoard _trunk;
    private readonly Func<IReadOnlyList<ProjectileTraceSample>> _specimens;
    private readonly Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>
        _cam;
    private readonly List<WidgetBoard> _markers = [];

    private ProjectileDebugOverlayDriver(
        WidgetBoard trunk,
        Func<IReadOnlyList<ProjectileTraceSample>> specimens,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> cam)
    {
        _trunk = trunk;
        _specimens = specimens;
        _cam = cam;
    }

    internal static ProjectileDebugOverlayDriver Mount(
        WidgetTrunk hub,
        Func<IReadOnlyList<ProjectileTraceSample>> specimens,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> cam)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(specimens);
        ArgumentNullException.ThrowIfNull(cam);
        WidgetBoard trunk = new WidgetBoard
        {
            Name = "PluginProjectileDebugOverlay",
            BackgroundColor = Vector4.Zero,
            BorderTint = Vector4.Zero,
            ClickThrough = true,
            Visible = false,
            ZOrder = -9_999,
            Moorings = MooringRims.None,
        };
        hub.AddChild(trunk);
        return new ProjectileDebugOverlayDriver(trunk, specimens, cam);
    }

    internal void Tick()
    {
        var specimens = _specimens();
        var cam = _cam();
        if (specimens.Count is 0
            || cam.Viewport.X <= 0f
            || cam.Viewport.Y <= 0f)
        {
            ConcealAll();
            return;
        }

        SecureMarkerTally(specimens.Count);
        _trunk.Left = 0f;
        _trunk.Top = 0f;
        _trunk.Width = cam.Viewport.X;
        _trunk.Height = cam.Viewport.Y;
        int shown = 0;
        for (int ordinal = 0; ordinal < specimens.Count; ++ordinal)
        {
            var specimen = specimens[ordinal];
            if (!ScreenMapping.TryProjectOrbToMonitorRect(
                    specimen.WorldPosition,
                    specimen.Radius,
                    cam.View,
                    cam.Projection,
                    cam.Viewport,
                    out Vector2 floor,
                    out Vector2 ceiling,
                    out _,
                    lowerFlankPx: 4f)
                || ceiling.X < 0f
                || ceiling.Y < 0f
                || floor.X > cam.Viewport.X
                || floor.Y > cam.Viewport.Y)

                continue;

            WidgetBoard marker = _markers[shown++];
            marker.Left = MathF.Max(0f, floor.X);
            marker.Top = MathF.Max(0f, floor.Y);
            marker.Width = MathF.Max(
                1f,
                MathF.Min(cam.Viewport.X, ceiling.X) - marker.Left);
            marker.Height = MathF.Max(
                1f,
                MathF.Min(cam.Viewport.Y, ceiling.Y) - marker.Top);
            marker.BorderTint = specimen.IsClear ? WipeTint : BlockedTint;
            marker.Visible = true;
        }
        for (int ordinal = shown; ordinal < _markers.Count; ++ordinal)
            _markers[ordinal].Visible = false;
        _trunk.Visible = shown > 0;
    }

    private void SecureMarkerTally(int tally)
    {
        while (_markers.Count < tally)
        {
            WidgetBoard marker = new WidgetBoard
            {
                Name = $"PluginProjectileDebugMarker{_markers.Count}",
                BackgroundColor = Vector4.Zero,
                BorderTint = WipeTint,
                BorderThickness = 1.5f,
                ClickThrough = true,
                Visible = false,
                Moorings = MooringRims.None,
            };
            _markers.Add(marker);
            _trunk.AddChild(marker);
        }
    }

    private void ConcealAll()
    {
        _trunk.Visible = false;
        for (int ordinal = 0; ordinal < _markers.Count; ++ordinal)
            _markers[ordinal].Visible = false;
    }
}

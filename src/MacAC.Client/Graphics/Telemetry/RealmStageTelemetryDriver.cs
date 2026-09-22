using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

internal readonly record struct RealmStageTelemetryVerdict(
    int VisibleLandblocks,
    int TotalLandblocks);

internal interface IRealmStageTelemetry
{
    CameraChamberResolution CamCellResolution { get; }

    RealmStageTelemetryVerdict PaintAndBroadcast(
        in RealmCameraFrame cam,
        IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)> limits);
}

internal sealed class RealmStageTelemetryDriver(
    IRealmStagePViewTelemetrySource pview,
    IRealmStageDebugStateSource state,
    DiagStrokePainter? strokes,
    KineticEngine physics,
    IAvatarModeSource mode,
    ISimAvatarDriverSource player,
    DebugVmRenderFactsHerald debugVm,
    bool diagVmConsumerEngaged) : IRealmStageTelemetry
{
    private readonly IRealmStagePViewTelemetrySource _pview = pview ?? throw new ArgumentNullException(nameof(pview));
    private readonly IRealmStageDebugStateSource _phase = state ?? throw new ArgumentNullException(nameof(state));
    private readonly DiagStrokePainter? _strokes = strokes;
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly IAvatarModeSource _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly DebugVmRenderFactsHerald _diagVm = debugVm ?? throw new ArgumentNullException(nameof(debugVm));
    private readonly bool _diagVmConsumerEngaged = diagVmConsumerEngaged;
    private int _diagPaintTraceTally;

    public CameraChamberResolution CamCellResolution => _pview.CameraChamberResolution;

    public RealmStageTelemetryVerdict PaintAndBroadcast(
        in RealmCameraFrame cam,
        IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        PaintImpactWireframes(in cam);

        int shown = 0;
        int sum = limits.Count;
        for (int ordinal = 0; ordinal < limits.Count; ++ordinal)
        {
            var listing = limits[ordinal];
            if (FrustumPruner.IsAabbShown(
                    cam.Frustum,
                    listing.AabbMin,
                    listing.AabbMax))

                ++shown;
        }

        if (_diagVmConsumerEngaged)
        {
            Vector3 closestOrigin =
                _mode.IsPlayerMode && _avatar.Controller is { } driver
                    ? driver.Position
                    : cam.Position;
            _diagVm.BroadcastDiagVmFacts(
                consumerEngaged: true,
                shown,
                sum,
                closestOrigin,
                _physics.ShadeObjects.AllListingsForDiag());
        }
        return new RealmStageTelemetryVerdict(shown, sum);
    }

    private void PaintImpactWireframes(in RealmCameraFrame cam)
    {
        if (!_phase.ImpactWireframesVisible || _strokes is null)
            return;

        _strokes.Begin();
        int drawn = 0;
        foreach (ProxyEntry shade in _physics.ShadeObjects.AllListingsForDiag())
        {
            if (shade.CollisionType == ProxyContactType.Cylinder)
            {
                float height = shade.CylHeight > 0f
                    ? shade.CylHeight
                    : shade.Radius * 2f;
                _strokes.AppendCylinder(
                    shade.Position,
                    shade.Radius,
                    height,
                    new Vector3(0f, 1f, 0f));
            }
            else
            {
                _strokes.AppendCylinder(
                    shade.Position - new Vector3(0f, 0f, shade.Radius),
                    shade.Radius,
                    shade.Radius * 2f,
                    new Vector3(1f, 0.5f, 0f));
            }
            ++drawn;
        }

        if (_mode.IsPlayerMode && _avatar.Controller is { } ownAvatar)
        {
            Vector3 locus = ownAvatar.Position;
            _strokes.AppendCylinder(
                new Vector3(locus.X, locus.Y, locus.Z),
                DebugVmRenderFactsHerald.AvatarImpactRadius,
                1.8f,
                new Vector3(1f, 0f, 0f));
            TraceNearbyImpactObjects(locus, drawn);
        }

        _strokes.Flush(cam.Camera.View, cam.Projection);
    }

    private void TraceNearbyImpactObjects(Vector3 avatarLocus, int drawn)
    {
        if (_diagPaintTraceTally >= 5)
            return;

        Console.WriteLine(
            $"debug frame {_diagPaintTraceTally}: player=({avatarLocus.X:F1},{avatarLocus.Y:F1},{avatarLocus.Z:F1}) drew={drawn} "
            + $"totalReg={_physics.ShadeObjects.SumRegistered}");
        int logged = 0;
        foreach (ProxyEntry shade in _physics.ShadeObjects.AllListingsForDiag())
        {
            float dx = shade.Position.X - avatarLocus.X;
            float dy = shade.Position.Y - avatarLocus.Y;
            float horizontalGap = MathF.Sqrt(dx * dx + dy * dy);
            if (horizontalGap >= 10f)
                continue;

            Console.WriteLine(
                $"  near id=0x{shade.EntityId:X8} type={shade.CollisionType} "
                + $"pos=({shade.Position.X:F1},{shade.Position.Y:F1},{shade.Position.Z:F1}) "
                + $"r={shade.Radius:F2} h={shade.CylHeight:F2} dh={horizontalGap:F2}");
            if (++logged >= 5)
                break;
        }
        ++_diagPaintTraceTally;
    }
}

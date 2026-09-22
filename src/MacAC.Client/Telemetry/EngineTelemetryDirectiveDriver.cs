using System.Globalization;
using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Telemetry;

internal interface IEngineTelemetryDirectives
{
    bool Handle(FeedAct act);

    void CycleTimeOfDay();

    void CycleWeather();

    void SwitchImpactWireframes();
}

internal sealed class EngineTelemetryDirectiveSlot : IEngineTelemetryDirectives
{
    private readonly object _latch = new();
    private IEngineTelemetryDirectives? _mark;
    private bool _deactivated;

    public void Bind(IEngineTelemetryDirectives mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null && !ReferenceEquals(_mark, mark))
            {
                throw new InvalidOperationException(
                    "Runtime diagnostic commands are by now bound");
            }

            _mark = mark;
        }
    }

    public void Unbind(IEngineTelemetryDirectives mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            if (ReferenceEquals(_mark, mark))
                _mark = null;
        }
    }

    public IDisposable BindOwned(IEngineTelemetryDirectives mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null)
            {
                throw new InvalidOperationException(
                    "Runtime diagnostic commands are by now bound");
            }

            _mark = mark;
        }

        return new Binding(this, mark);
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _mark = null;
        }
    }

    public bool Handle(FeedAct act)
    {
        lock (_latch)
        {
            if (!_deactivated && _mark is { } mark)
                return mark.Handle(act);
        }

        return EngineTelemetryDirectiveDriver.IsProbeAct(act);
    }

    public void CycleTimeOfDay()
    {
        lock (_latch)
        {
            if (!_deactivated)
                _mark?.CycleTimeOfDay();
        }
    }

    public void CycleWeather()
    {
        lock (_latch)
        {
            if (!_deactivated)
                _mark?.CycleWeather();
        }
    }

    public void SwitchImpactWireframes()
    {
        lock (_latch)
        {
            if (!_deactivated)
                _mark?.SwitchImpactWireframes();
        }
    }

    private sealed class Binding(
        EngineTelemetryDirectiveSlot holder,
        IEngineTelemetryDirectives anticipated) : IDisposable
    {
        private EngineTelemetryDirectiveSlot? _holder = holder;
        private readonly IEngineTelemetryDirectives _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_anticipated);
    }
}

internal interface INearbyRealmTelemetrySource
{
    Vector3 WatcherLocus { get; }

    int OnlineCenterX { get; }

    int OnlineCenterY { get; }

    int SumShadeObjects { get; }

    IReadOnlyList<RealmActor> RealmActors { get; }

    IEnumerable<ProxyEntry> ShadeListings { get; }
}

internal sealed class EngineNearbyRealmTelemetrySource(
    IAvatarModeSource mode,
    ISimAvatarDriverSource player,
    CameraDriver camera,
    OnlineRealmOriginLedger origin,
    GpuRealmPhase world,
    KineticEngine physics) : INearbyRealmTelemetrySource
{
    private readonly IAvatarModeSource _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));

    public Vector3 WatcherLocus
    {
        get
        {
            if (_mode.IsPlayerMode && _avatar.Controller is { } avatar)
                return avatar.Position;

            Matrix4x4.Invert(_cam.Active.View, out Matrix4x4 invLens);
            return new Vector3(
                invLens.M41,
                invLens.M42,
                invLens.M43);
        }
    }

    public int OnlineCenterX => _origin.CenterX;

    public int OnlineCenterY => _origin.CenterY;

    public int SumShadeObjects => _physics.ShadeObjects.SumRegistered;

    public IReadOnlyList<RealmActor> RealmActors => _world.Entities;

    public IEnumerable<ProxyEntry> ShadeListings =>
        _physics.ShadeObjects.AllListingsForDiag();
}

internal sealed class NearbyRealmTelemetryDumper(
    INearbyRealmTelemetrySource source,
    Action<string>? trace = null)
{
    private const float NearbyGap = 15f;
    private readonly INearbyRealmTelemetrySource _src = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Action<string> _trace = trace ?? (_ => { });

    public void Dump()
    {
        Vector3 locus = _src.WatcherLocus;
        int middleX = _src.OnlineCenterX;
        int middleY = _src.OnlineCenterY;
        int lbX = middleX + (int)MathF.Floor(locus.X / 192f);
        int lbY = middleY + (int)MathF.Floor(locus.Y / 192f);

        _trace(string.Create(
            CultureInfo.InvariantCulture,
            $"=== F3 DEBUG DUMP ===\n" +
            $"  player pos=({locus.X:F2},{locus.Y:F2},{locus.Z:F2})\n" +
            $"  landblock=0x{(uint)((lbX << 24) | (lbY << 16) | 0xFFFF):X8} " +
            $"local=({locus.X - (lbX - middleX) * 192f:F2}," +
            $"{locus.Y - (lbY - middleY) * 192f:F2})\n" +
            $"  total shadow objects: {_src.SumShadeObjects}"));

        List<RealmActor> shownNearby = _src.RealmActors
            .Where(actor => HorizontalGapSquared(actor.Position, locus)
                < NearbyGap * NearbyGap)
            .OrderBy(actor => (actor.Position - locus).Length())
            .ToList();
        _trace($"  VISIBLE entities within 15m: {shownNearby.Count}");
        foreach (RealmActor entity in shownNearby.Take(12))
        {
            float gap = (entity.Position - locus).Length();
            _trace(
                $"    VIS  id=0x{entity.Id:X8} src=0x{entity.SrcGfxObjRefOrRigIdent:X8} " +
                $"pos=({entity.Position.X:F2},{entity.Position.Y:F2},{entity.Position.Z:F2}) " +
                $"dist={gap:F2} scale={entity.Scale:F2}");
        }

        var shades = _src.ShadeListings
            .Select(listing =>
            {
                float dx = listing.Position.X - locus.X;
                float dy = listing.Position.Y - locus.Y;
                return (Entry: listing, Distance: MathF.Sqrt(dx * dx + dy * dy));
            })
            .Where(gear => gear.Distance < NearbyGap)
            .OrderBy(gear => gear.Distance)
            .ToList();
        _trace($"  SHADOW objects within 15m: {shades.Count}");
        foreach ((ProxyEntry entry, float gap) in shades.Take(12))
        {
            _trace(
                $"    SHAD id=0x{entry.EntityId:X8} {entry.CollisionType} " +
                $"r={entry.Radius:F2} h={entry.CylHeight:F2} " +
                $"pos=({entry.Position.X:F2},{entry.Position.Y:F2},{entry.Position.Z:F2}) " +
                $"dist={gap:F2}");
        }
    }

    private static float HorizontalGapSquared(Vector3 left, Vector3 right)
    {
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }
}

internal sealed class EngineTelemetryDirectiveDriver(
    RealmEnvironmentDriver environment,
    RealmTableauDiagPhase sceneDebug,
    IPointerSensitivityDirectives? ptr,
    NearbyRealmTelemetryDumper nearby,
    Action<string>? toast = null) : IEngineTelemetryDirectives
{
    private readonly RealmEnvironmentDriver _surroundings = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly RealmTableauDiagPhase _tableauDiag = sceneDebug ?? throw new ArgumentNullException(nameof(sceneDebug));
    private readonly IPointerSensitivityDirectives? _ptr = ptr;
    private readonly NearbyRealmTelemetryDumper _nearby = nearby ?? throw new ArgumentNullException(nameof(nearby));
    private readonly Action<string>? _toast = toast;

    public bool Handle(FeedAct act)
    {
        switch (act)
        {
            case FeedAct.EngineToggleCollisionWires:
                SwitchImpactWireframes();
                return true;
            case FeedAct.EngineDumpNearby:
                _nearby.Dump();
                return true;
            case FeedAct.EngineCycleTimeOfDay:
                CycleTimeOfDay();
                return true;
            case FeedAct.EngineSensitivityDown:
                if (_ptr is not null)
                {
                    string msg = _ptr.TuneSensitivity(1f / 1.2f);
                    _toast?.Invoke(msg);
                }
                return true;
            case FeedAct.EngineSensitivityUp:
                if (_ptr is not null)
                {
                    string msg = _ptr.TuneSensitivity(1.2f);
                    _toast?.Invoke(msg);
                }
                return true;
            case FeedAct.EngineCycleWeather:
                CycleWeather();
                return true;
            default:
                return false;
        }
    }

    public void CycleTimeOfDay()
    {
        string msg = _surroundings.CycleTimeOfDay();
        _toast?.Invoke(msg);
    }

    public void CycleWeather()
    {
        string msg = _surroundings.CycleWeather();
        _toast?.Invoke(msg);
    }

    public void SwitchImpactWireframes()
    {
        bool shown = _tableauDiag.FlipImpactWireframes();
        _toast?.Invoke($"Collision wireframes {(shown ? "ON" : "OFF")}");
    }

    internal static bool IsProbeAct(FeedAct act)
    {
        return act is
        FeedAct.EngineToggleCollisionWires
        or FeedAct.EngineDumpNearby
        or FeedAct.EngineCycleTimeOfDay
        or FeedAct.EngineSensitivityDown
        or FeedAct.EngineSensitivityUp
        or FeedAct.EngineCycleWeather;
    }
}

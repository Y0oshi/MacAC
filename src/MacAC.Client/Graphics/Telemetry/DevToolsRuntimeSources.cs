using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

internal interface IRenderFrameTelemetryCaptureSource
{
    RenderFrameTelemetryCapture Capture { get; }
}

internal sealed class DeferredRenderFrameTelemetrySource
    : IRenderFrameTelemetryCaptureSource
{
    private IRenderFrameTelemetryCaptureSource? _mark;
    private bool _deactivated;

    public RenderFrameTelemetryCapture Capture
    {
        get
        {
            return !_deactivated && _mark is { } mark
            ? mark.Capture
            : RenderFrameTelemetryCapture.Initial;
        }
    }

    public void Bind(IRenderFrameTelemetryCaptureSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null && !ReferenceEquals(_mark, mark))
            throw new InvalidOperationException(
                "Render-frame diagnostics are by now bound");
        _mark = mark;
    }

    public IDisposable BindOwned(IRenderFrameTelemetryCaptureSource mark)
    {
        Bind(mark);
        return new Binding(this, mark);
    }

    public void Unbind(IRenderFrameTelemetryCaptureSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (ReferenceEquals(_mark, mark))
            _mark = null;
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private sealed class Binding(
        DeferredRenderFrameTelemetrySource holder,
        IRenderFrameTelemetryCaptureSource mark) : IDisposable
    {
        private DeferredRenderFrameTelemetrySource? _holder = holder;
        private readonly IRenderFrameTelemetryCaptureSource _mark = mark;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_mark);
    }
}

internal interface ICanonicalRealmActorCountSource
{
    int ActorTally { get; }
}

internal sealed class DeferredCanonicalRealmActorCountSource
    : ICanonicalRealmActorCountSource
{
    private GpuRealmPhase? _mark;
    private bool _deactivated;

    public int ActorTally => !_deactivated ? _mark?.Entities.Count ?? 0 : 0;

    public void Bind(GpuRealmPhase mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null && !ReferenceEquals(_mark, mark))
            throw new InvalidOperationException(
                "The canonical world entity-count source is by now bound");
        _mark = mark;
    }

    public IDisposable BindOwned(GpuRealmPhase mark)
    {
        Bind(mark);
        return new Binding(this, mark);
    }

    public void Unbind(GpuRealmPhase mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (ReferenceEquals(_mark, mark))
            _mark = null;
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private sealed class Binding(
        DeferredCanonicalRealmActorCountSource holder,
        GpuRealmPhase mark) : IDisposable
    {
        private DeferredCanonicalRealmActorCountSource? _holder = holder;
        private readonly GpuRealmPhase _mark = mark;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_mark);
    }
}

internal interface IDevToolsAvatarModeDirectives
{
    void FlipFlyOrChase();
}

internal interface IDevToolsAvatarModeTarget
{
    void FlipFlyOrPursue();
}

internal sealed class DeferredDevToolsAvatarModeDirectives
    : IDevToolsAvatarModeDirectives
{
    private IDevToolsAvatarModeTarget? _mark;
    private bool _deactivated;

    public void Bind(IDevToolsAvatarModeTarget mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null && !ReferenceEquals(_mark, mark))
            throw new InvalidOperationException(
                "Developer player-mode commands are by now bound");
        _mark = mark;
    }

    public IDisposable BindOwned(IDevToolsAvatarModeTarget mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null)
        {
            throw new InvalidOperationException(
                "Developer player-mode commands are by now bound");
        }

        _mark = mark;
        return new Binding(this, mark);
    }

    public void Unbind(IDevToolsAvatarModeTarget mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (ReferenceEquals(_mark, mark))
            _mark = null;
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    public void FlipFlyOrChase()
    {
        if (!_deactivated)
            _mark?.FlipFlyOrPursue();
    }

    private sealed class Binding(
        DeferredDevToolsAvatarModeDirectives holder,
        IDevToolsAvatarModeTarget anticipated) : IDisposable
    {
        private DeferredDevToolsAvatarModeDirectives? _holder = holder;
        private readonly IDevToolsAvatarModeTarget _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_anticipated);
    }
}

internal interface IDevToolsEngineFacts
{
    Vector3 AvatarLocus { get; }
    float AvatarBearingDeg { get; }
    uint AvatarChamberTag { get; }
    bool AvatarOnTerrain { get; }
    bool InAvatarManner { get; }
    bool InFlyManner { get; }
    float VerticalVel { get; }
    int ActorCount { get; }
    int MovingTally { get; }
    int VisibleLandblocks { get; }
    int TotalLandblocks { get; }
    int ShadeObjectTally { get; }
    float ClosestObjectGap { get; }
    string ClosestObjectCaption { get; }
    bool Colliding { get; }
    bool ImpactWireframesShown { get; }
    int PagingRadius { get; }
    float PointerSensitivity { get; }
    float PursueGap { get; }
    bool RmbOrbitHeld { get; }
    string HourLabel { get; }
    float DayFraction { get; }
    string Weather { get; }
    int EngagedLamps { get; }
    int RegisteredLamps { get; }
    int MoteTally { get; }
    float Fps { get; }
    float CycleMillis { get; }
}


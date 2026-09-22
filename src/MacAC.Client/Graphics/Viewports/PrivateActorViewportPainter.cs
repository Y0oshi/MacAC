using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Shell;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IPrivateActorViewportCamera : IClientCamera
{
    Vector3 Eye { get; }
}

internal sealed partial class PrivateActorViewportPainter :
    IWidgetViewportPainter,
    IDisposable
{
    private const uint PrivateLbIdent = 0u;

    private readonly ILatestGpuCycleOrigin _cycles;

    private readonly IRealmPassScope _ambit;

    private readonly RealmPaintRouter _router;

    private readonly StageLightingUboWiring _lampUbo;

    private readonly IBatchMeshBridge _triMeshBridge;

    private readonly IPrivateActorViewportCamera _cam;

    private readonly HashSet<uint> _movingIdents;

    private readonly string _probeLabel;

    private readonly ActorSlot _primarySocket;

    private readonly ActorSlot? _backdropSocket;

    private readonly PrivateViewRectFlightMarks _flightMarks;

    public PrivateActorViewportPainter(
        IRealmPassScope scope,
        IClientGpuDevice dev,
        ILatestGpuCycleOrigin frames,
        RealmPaintRouter dispatcher,
        StageLightingUboWiring lightUbo,
        IActorTextureLifetime textureLifetime,
        IBatchMeshBridge meshAdapter,
        uint renderId,
        IPrivateActorViewportCamera camera,
        string probeLabel,
        uint? backdropRenderId = null)
    {
        if (renderId is 0u)
            throw new ArgumentOutOfRangeException(nameof(renderId));
        if (backdropRenderId == 0u)
            throw new ArgumentOutOfRangeException(nameof(backdropRenderId));

        _ambit = scope ?? throw new ArgumentNullException(
            nameof(scope),
            "The viewport must publish a world pass scope to draw into");
        ArgumentNullException.ThrowIfNull(dev);
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _router = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _lampUbo = lightUbo ?? throw new ArgumentNullException(nameof(lightUbo));
        _triMeshBridge = meshAdapter
            ?? throw new ArgumentNullException(nameof(meshAdapter));
        _cam = camera ?? throw new ArgumentNullException(nameof(camera));
        _probeLabel = string.IsNullOrWhiteSpace(probeLabel)
            ? "creature viewport"
            : probeLabel;
        _flightMarks = new PrivateViewRectFlightMarks(
            dev,
            _probeLabel);

        IActorTextureLifetime textureLifespanChecked = textureLifetime
            ?? throw new ArgumentNullException(nameof(textureLifetime));

        _primarySocket = new ActorSlot(_triMeshBridge, textureLifespanChecked, renderId, _probeLabel);
        _backdropSocket = backdropRenderId is uint backdropIdent
            ? new ActorSlot(_triMeshBridge, textureLifespanChecked, backdropIdent, _probeLabel + " backdrop")
            : null;

        _movingIdents = backdropRenderId is uint movingBackdropIdent
            ? [renderId, movingBackdropIdent]
            : [renderId];
    }

    internal sealed class PrivateViewRectFlightMarks : IDisposable
    {
        internal sealed class MarkSocket(
            IGpuRasterizeMark mark,
            GpuTextureSlot textureSocket)
        {
            internal IGpuRasterizeMark Target { get; } = mark;
            internal GpuTextureSlot TextureSlot { get; } = textureSocket;
            internal bool HasRenderedTableau { get; set; }
        }

        private readonly IClientGpuDevice _device;
        private readonly string _probeLabel;
        private readonly List<MarkSocket?> _sockets = [];
        private int _width;
        private int _height;
        private bool _destroyed;

        internal PrivateViewRectFlightMarks(
            IClientGpuDevice device,
            string probeLabel)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _probeLabel = string.IsNullOrWhiteSpace(probeLabel)
                ? "creature viewport"
                : probeLabel;
        }

        internal int AllocatedSocketTally =>
            _sockets.Count(static socket => socket is not null);

        public void Dispose()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            FreeAll();
        }

        internal MarkSocket? Secure(int cycleSocket, int width, int height)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            if (_width is not 0 && (_width != width || _height != height))
                FreeAll();

            while (_sockets.Count <= cycleSocket)
                _sockets.Add(null);
            if (_sockets[cycleSocket] is { } extant)
                return extant;

            IGpuRasterizeMark mark;
            try
            {
                mark = _device.BuildRasterizeMark(
                    new GpuRenderTargetSpec(
                        $"{_probeLabel}-flight-{cycleSocket}",
                        width,
                        height,
                        GpuBitmapFmt.Rgba8UnormRenderTarget,
                        GpuBitmapFmt.Depth24Stencil8,
                        SampleCount: 1));
            }
            catch (Exception miss)
            {
                Console.WriteLine(
                    $"[{_probeLabel}] render target not available "
                    + $"({width}x{height}, flight {cycleSocket}): {miss.Message}");
                return null;
            }

            try
            {
                IClientGpuSampler sampler = _device.BuildSampler(
                    GpuSamplerSpec.RealmClamp);
                var textureSocket = _device.EnrollTexture(
                    mark.ColorTexture,
                    sampler);
                MarkSocket built = new MarkSocket(mark, textureSocket);
                _sockets[cycleSocket] = built;
                _width = width;
                _height = height;
                return built;
            }
            catch
            {
                mark.Dispose();
                throw;
            }
        }

        internal uint FinishedHnd(int cycleSocket)
        {
            return (uint)cycleSocket >= (uint)_sockets.Count
                || _sockets[cycleSocket] is not { HasRenderedTableau: true } socket
                ? 0u
                : WidgetTextureChartHandle.FromSocket(socket.TextureSlot);
        }

        internal void DirtyFinishedScenes()
        {
            for (int idx = 0; idx < _sockets.Count; ++idx)
            {
                if (_sockets[idx] is { } socket)
                    socket.HasRenderedTableau = false;
            }
        }

        private void FreeAll()
        {
            List<Exception>? misses = null;
            for (int idx = 0; idx < _sockets.Count; ++idx)
            {
                MarkSocket? socket = _sockets[idx];
                if (socket is null)
                    continue;
                try
                {
                    _device.FreeTextureSocket(socket.TextureSlot);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
                try
                {
                    socket.Target.Dispose();
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }
            _sockets.Clear();
            _width = 0;
            _height = 0;
            if (misses is { Count: > 0 })
                throw new AggregateException(misses);
        }
    }

    internal sealed class ActorSlot(
        IBatchMeshBridge triMeshBridge,
        IActorTextureLifetime textureLifespan,
        uint holderOwnIdent,
        string probeLabel)
    {
        private sealed class MeshCapture
        {
            private readonly ulong[] _possessedIdents;
            private readonly int _paintTriMeshTally;

            private MeshCapture(ulong[] possessedIdents, int paintTriMeshTally)
            {
                _possessedIdents = possessedIdents;
                _paintTriMeshTally = paintTriMeshTally;
            }

            public IReadOnlyList<ulong> PossessedIdents => _possessedIdents;

            public static MeshCapture Capture(RealmActor actor)
            {
                int paintTriMeshTally = actor.MeshRefs.Count;
                ulong[] idents = new ulong[paintTriMeshTally + actor.PieceSubstitutions.Count];
                for (int idx = 0; idx < paintTriMeshTally; ++idx)
                    idents[idx] = actor.MeshRefs[idx].GfxObjId;
                for (int idx = 0; idx < actor.PieceSubstitutions.Count; ++idx)
                    idents[paintTriMeshTally + idx] = actor.PieceSubstitutions[idx].GfxObjId;
                return new MeshCapture(idents, paintTriMeshTally);
            }

            public bool Matches(RealmActor actor)
            {
                if (actor.MeshRefs.Count != _paintTriMeshTally
                    || actor.PieceSubstitutions.Count != _possessedIdents.Length - _paintTriMeshTally)

                    return false;

                for (int idx = 0; idx < _paintTriMeshTally; ++idx)
                    if (actor.MeshRefs[idx].GfxObjId != _possessedIdents[idx])
                        return false;
                for (int idx = 0; idx < actor.PieceSubstitutions.Count; ++idx)
                    if (actor.PieceSubstitutions[idx].GfxObjId != _possessedIdents[_paintTriMeshTally + idx])
                        return false;
                return true;
            }

            public bool ArePaintTriMeshesPrimed(IBatchMeshBridge bridge)
            {
                for (int idx = 0; idx < _paintTriMeshTally; ++idx)
                {
                    ulong ident = _possessedIdents[idx];
                    if (ident is 0u || !bridge.IsRasterizeBlobPrimed(ident))
                        return false;
                }
                return true;
            }
        }

        private sealed record PendingActor(
            RealmActor Entity,
            MeshCapture Snapshot,
            SyntheticActorMeshReferenceOwner MeshReferences);

        private readonly IBatchMeshBridge _triMeshBridge = triMeshBridge;
        private readonly FixedActorTextureOwnerLease _textureHolderTenancy = new FixedActorTextureOwnerLease(textureLifespan, holderOwnIdent);
        private readonly string _probeLabel = probeLabel;
        private readonly List<SyntheticActorMeshReferenceOwner> _sunsettingTriMeshReferences = [];

        private SyntheticActorMeshReferenceOwner? _triMeshReferences;
        private MeshCapture? _triMeshCapture;
        private PendingActor? _queued;

        public RealmActor? Entity { get; private set; }

        internal bool HasQueued => _queued is not null;

        public void Set(RealmActor? actor)
        {
            FreeSunsettingTriMeshReferences();

            if (actor is null)
            {
                Clear();
                return;
            }

            if (ReferenceEquals(Entity, actor) && _queued is null)
                return;

            if (ReferenceEquals(Entity, actor)
                && _triMeshCapture?.Matches(actor) == true)
            {
                FreeQueued();
                return;
            }

            if (_queued is { } queued
                && ReferenceEquals(queued.Entity, actor)
                && queued.Snapshot.Matches(actor))

                return;

            Stage(actor);
        }

        public bool ReadyForPaint()
        {
            FreeSunsettingTriMeshReferences();

            if (_queued is { } queued)
            {
                if (!queued.Snapshot.Matches(queued.Entity))
                    Stage(queued.Entity);
            }
            else if (Entity is { } latest
                && _triMeshCapture?.Matches(latest) != true)
            {
                Stage(latest);
            }

            queued = _queued;
            if (queued is null)
                return true;
            if (!queued.Snapshot.ArePaintTriMeshesPrimed(_triMeshBridge))
                return false;

            PromoteQueued(queued);
            return true;
        }

        public void Dispose()
        {
            Entity = null;
            _triMeshCapture = null;
            if (_triMeshReferences is { } latest)
            {
                _triMeshReferences = null;
                _sunsettingTriMeshReferences.Add(latest);
            }
            if (_queued is { } queued)
            {
                _queued = null;
                _sunsettingTriMeshReferences.Add(queued.MeshReferences);
            }

            List<Exception>? misses = null;
            try
            {
                _textureHolderTenancy.Dispose();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
            try
            {
                FreeSunsettingTriMeshReferences();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }

            if (misses is not null)
            {
                throw new AggregateException(
                    $"The {_probeLabel} resources didn't fully release",
                    misses);
            }
        }

        private void Stage(RealmActor actor)
        {
            MeshCapture capture = MeshCapture.Capture(actor);
            var substitute = new SyntheticActorMeshReferenceOwner(
                _triMeshBridge,
                capture.PossessedIdents);
            try
            {
                substitute.Acquire();
            }
            catch (Exception acquisitionMiss)
            {
                try
                {
                    substitute.Dispose();
                }
                catch (Exception undoMiss)
                {
                    _sunsettingTriMeshReferences.Add(substitute);
                    throw new AggregateException(
                        $"The {_probeLabel} candidate mesh acquisition failed "
                        + "and its rollback didn't converge",
                        acquisitionMiss,
                        undoMiss);
                }

                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(acquisitionMiss)
                    .Throw();
                throw new InvalidOperationException("Unreachable exception dispatch path");
            }

            var earlier = _queued;
            _queued = new PendingActor(actor, capture, substitute);
            if (earlier is not null)
                Retire(earlier.MeshReferences);
        }

        private void PromoteQueued(PendingActor queued)
        {
            _textureHolderTenancy.Replace(hasSubstitute: true);

            var earlier = _triMeshReferences;
            _triMeshReferences = queued.MeshReferences;
            _triMeshCapture = queued.Snapshot;
            Entity = queued.Entity;
            _queued = null;

            if (earlier is not null)
                Retire(earlier);
        }

        private void Clear()
        {
            if (Entity is null && _queued is null)
                return;

            FreeQueued();
            _textureHolderTenancy.Replace(hasSubstitute: false);

            var earlier = _triMeshReferences;
            _triMeshReferences = null;
            _triMeshCapture = null;
            Entity = null;
            if (earlier is not null)
                Retire(earlier);
        }

        private void FreeQueued()
        {
            var queued = _queued;
            if (queued is null)
                return;
            _queued = null;
            Retire(queued.MeshReferences);
        }

        private void Retire(SyntheticActorMeshReferenceOwner holder)
        {
            try
            {
                holder.Dispose();
            }
            catch
            {
                _sunsettingTriMeshReferences.Add(holder);
                throw;
            }
        }

        private void FreeSunsettingTriMeshReferences()
        {
            List<Exception>? misses = null;
            for (int idx = _sunsettingTriMeshReferences.Count - 1; idx >= 0; --idx)
            {
                var holder =
                    _sunsettingTriMeshReferences[idx];
                try
                {
                    holder.Dispose();
                    if (holder.IsDisposed)
                        _sunsettingTriMeshReferences.RemoveAt(idx);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }

            if (misses is not null)
            {
                throw new AggregateException(
                    $"One or more {_probeLabel} mesh owners remain pending",
                    misses);
            }
        }
    }
}

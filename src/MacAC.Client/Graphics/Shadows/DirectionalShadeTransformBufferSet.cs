using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal readonly record struct DirectionalShadeTransformPublishStats(
    bool TopologyUploaded,
    int DynamicMatricesUpdated,
    int DynamicRangesUpdated,
    long BytesWritten,
    int CurrentChangedMatrices = 0,
    int PendingReplayMatrices = 0,
    bool UsedFullDynamicFallback = false,
    bool DenseDirectUpload = false,
    bool DenseFlightReplay = false);

internal sealed class DirectionalShadeTransformBufferSet : IDisposable
{
    private readonly IClientGpuDevice _device;
    private SlotLedger[] _sockets = [];
    private ulong _denseWiringAssembleSeries;
    private ulong _denseRev = 1;
    private bool _destroyed;

    internal DirectionalShadeTransformBufferSet(IClientGpuDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (!device.Capabilities.SupportsPersistentlyMappedRings)
        {
            throw new NotSupportedException(
                "Directional-shadow retained transforms require persistently mapped host-writable buffers");
        }
    }

    internal long KeptGpuOctets
    {
        get
        {
            long sum = 0;
            for (int idx = 0; idx < _sockets.Length; ++idx)
                sum = checked(sum + (_sockets[idx].Buffer?.SizeBytes ?? 0L));
            return sum;
        }
    }

    internal int BufTally
    {
        get
        {
            int tally = 0;
            for (int idx = 0; idx < _sockets.Length; ++idx)
            {
                if (_sockets[idx].Buffer is not null)
                    ++tally;
            }
            return tally;
        }
    }

    internal long KeptTempOctets
    {
        get
        {
            long octets = checked((long)_sockets.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<SlotLedger>());
            for (int ordinal = 0; ordinal < _sockets.Length; ++ordinal)
                octets = checked(octets + (_sockets[ordinal].Pending?.KeptOctets ?? 0L));
            return octets;
        }
    }

    internal DirectionalShadeTransformPublishStats PreviousStats { get; private set; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        for (int idx = 0; idx < _sockets.Length; ++idx)
        {
            _sockets[idx].Buffer?.Dispose();
            _sockets[idx] = default;
        }
        PreviousStats = default;
        _denseWiringAssembleSeries = 0;
        _denseRev = 0;
    }

    internal RealmTransformFrameSlice Publish(
        IGpuCycle cycle,
        ulong wiringAssembleSeries,
        ReadOnlySpan<Matrix4x4> xforms,
        ReadOnlySpan<int> dynamicXformSockets)
    {
        return Publish(
            cycle,
            wiringAssembleSeries,
            xforms,
            dynamicXformSockets,
            dynamicXformSockets,
            denseRenew: false);
    }

    internal RealmTransformFrameSlice Publish(
        IGpuCycle cycle,
        ulong wiringAssembleSeries,
        ReadOnlySpan<Matrix4x4> xforms,
        ReadOnlySpan<int> dynamicXformSockets,
        ReadOnlySpan<int> allDynamicXformSockets)
    {
        return Publish(
            cycle,
            wiringAssembleSeries,
            xforms,
            dynamicXformSockets,
            allDynamicXformSockets,
            denseRenew: false);
    }

    internal RealmTransformFrameSlice Publish(
        IGpuCycle cycle,
        ulong topologyBuildSequence,
        ReadOnlySpan<Matrix4x4> xforms,
        ReadOnlySpan<int> dynamicXformSockets,
        ReadOnlySpan<int> allDynamicXformSockets,
        bool denseRenew,
        uint mappingByteSize = 0)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        if (topologyBuildSequence is 0)
            throw new ArgumentOutOfRangeException(nameof(topologyBuildSequence));
        uint neededInsts = checked((uint)xforms.Length);
        if (mappingByteSize is 0)
        {
            mappingByteSize = RealmTransformCapRule.LocateMappingByteSize(
                neededInsts,
                _device.Capabilities.UpperDepotBufSpanOctets);
        }
        RealmTransformCapRule.VetMappingByteSize(
            mappingByteSize,
            neededInsts,
            _device.Capabilities.UpperDepotBufSpanOctets);
        if (!denseRenew)
            VetDynamicSockets(dynamicXformSockets, xforms.Length);
        RestartDenseRevForWiring(topologyBuildSequence);
        if (denseRenew)
        {
            VetDynamicSockets(allDynamicXformSockets, xforms.Length);
            if (_denseRev == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "Directional-shadow dense transform revision was exhausted");
            }
            ++_denseRev;
            for (int ordinal = 0; ordinal < _sockets.Length; ++ordinal)
                _sockets[ordinal].Pending?.Clear();
        }
        SecureSocketCap(cycle.SocketOrdinal);
        ref SlotLedger socket = ref _sockets[cycle.SocketOrdinal];
        bool matchingSocket = socket.Buffer is not null
            && socket.TopologyBuildSequence == topologyBuildSequence
            && socket.TransformCount == xforms.Length
            && socket.Buffer.SizeBytes >= mappingByteSize;
        bool denseFlightRerun = matchingSocket
            && socket.ConsumedDenseRevision != _denseRev;
        int queuedRerunMatrices = matchingSocket
                ? socket.Pending?.Count ?? 0
                : 0;
        if (!denseRenew)
        {
            FlagQueuedEdits(
                topologyBuildSequence,
                xforms.Length,
                dynamicXformSockets);
        }
        int substanceOctets = checked(xforms.Length * 64);
        int allocOctets = checked((int)mappingByteSize);

        if (socket.Buffer is null
            || socket.TopologyBuildSequence != topologyBuildSequence
            || socket.TransformCount != xforms.Length
            || socket.Buffer.SizeBytes < allocOctets)
        {
            IClientGpuBuffer? contender = null;
            try
            {
                contender = _device.BuildBuf(new GpuBufferSpec(
                    $"directional-shadow-transforms-slot-{cycle.SocketOrdinal}-build-{topologyBuildSequence}",
                    allocOctets,
                    GpuBufferPurpose.Storage | GpuBufferPurpose.TransferDestination,
                    GpuMemoryTenancy.HostWritable));
                if (!contender.HostWritesAreCoherent)
                {
                    throw new NotSupportedException(
                        "Directional-shadow retained transforms require coherent "
                        + "host-writable Vulkan memory. The pack will fail safe "
                        + "on this adapter rather than expose unflushed pose data");
                }
                if (!xforms.IsEmpty)
                {
                    contender.Upload(0, MemoryMarshal.AsBytes(xforms));
                    cycle.BroadcastHubDepotWrites(contender);
                }
            }
            catch
            {
                contender?.Dispose();
                throw;
            }

            IClientGpuBuffer? earlier = socket.Buffer;
            QueuedTransformGroup queued = socket.Pending
                ?? new QueuedTransformGroup(xforms.Length);
            queued.SecureCap(xforms.Length);
            queued.Clear();
            socket = new SlotLedger(
                contender,
                topologyBuildSequence,
                xforms.Length,
                queued,
                _denseRev);
            earlier?.Dispose();
            PreviousStats = new DirectionalShadeTransformPublishStats(
                TopologyUploaded: true,
                DynamicMatricesUpdated: 0,
                DynamicRangesUpdated: 0,
                BytesWritten: substanceOctets,
                CurrentChangedMatrices: dynamicXformSockets.Length,
                PendingReplayMatrices: 0,
                DenseDirectUpload: denseRenew);
        }
        else
        {
            QueuedTransformGroup queued = socket.Pending
                ?? throw new InvalidOperationException(
                    "A retained directional-shadow flight slot has no pending-change owner");
            bool straightDensePush = denseRenew || denseFlightRerun;
            ReadOnlySpan<int> socketsToPush = straightDensePush
                ? allDynamicXformSockets
                : queued.FetchSorted();
            if (straightDensePush && !denseRenew)
                VetDynamicSockets(allDynamicXformSockets, xforms.Length);
            int spans = PushDynamicSpans(
                socket.Buffer,
                xforms,
                socketsToPush,
                out long octetsWritten);
            if (spans is not 0)
                cycle.BroadcastHubDepotWrites(socket.Buffer);
            PreviousStats = new DirectionalShadeTransformPublishStats(
                TopologyUploaded: false,
                DynamicMatricesUpdated: socketsToPush.Length,
                DynamicRangesUpdated: spans,
                BytesWritten: octetsWritten,
                CurrentChangedMatrices: dynamicXformSockets.Length,
                PendingReplayMatrices: straightDensePush ? 0 : queuedRerunMatrices,
                DenseDirectUpload: denseRenew,
                DenseFlightReplay: denseFlightRerun && !denseRenew);
            queued.Clear();
            socket = socket with { ConsumedDenseRevision = _denseRev };
        }

        IClientGpuBuffer buf = socket.Buffer
            ?? throw new InvalidOperationException(
                "The retained directional-shadow transform buffer wasn't published");
        return new RealmTransformFrameSlice(
            cycle.SerialNo,
            buf,
            BaseOffsetBytes: 0,
            checked((uint)buf.SizeBytes),
            FirstInstance: 0,
            checked((uint)xforms.Length));
    }

    private void RestartDenseRevForWiring(ulong wiringAssembleSeries)
    {
        if (_denseWiringAssembleSeries == wiringAssembleSeries)
            return;
        _denseWiringAssembleSeries = wiringAssembleSeries;
        _denseRev = 1;
        for (int ordinal = 0; ordinal < _sockets.Length; ++ordinal)
            _sockets[ordinal].Pending?.Clear();
    }

    private void FlagQueuedEdits(
        ulong wiringAssembleSeries,
        int xformTally,
        ReadOnlySpan<int> dynamicXformSockets)
    {
        if (dynamicXformSockets.IsEmpty)
            return;
        for (int ordinal = 0; ordinal < _sockets.Length; ++ordinal)
        {
            ref SlotLedger contender = ref _sockets[ordinal];
            if (contender.Buffer is null
                || contender.TopologyBuildSequence != wiringAssembleSeries
                || contender.TransformCount != xformTally)

                continue;
            var queued = contender.Pending
                ??= new QueuedTransformGroup(xformTally);
            queued.SecureCap(xformTally);
            queued.Flag(dynamicXformSockets);
        }
    }

    private static int PushDynamicSpans(
        IClientGpuBuffer buf,
        ReadOnlySpan<Matrix4x4> xforms,
        ReadOnlySpan<int> sockets,
        out long octetsWritten)
    {
        octetsWritten = 0;
        int spans = 0;
        int cur = 0;
        while (cur < sockets.Length)
        {
            int begin = sockets[cur];
            int finish = begin + 1;
            ++cur;
            while (cur < sockets.Length && sockets[cur] == finish)
            {
                ++finish;
                ++cur;
            }

            var vals = xforms[begin..finish];
            var octets = MemoryMarshal.AsBytes(vals);
            buf.Upload(checked((long)begin * 64L), octets);
            octetsWritten = checked(octetsWritten + octets.Length);
            ++spans;
        }
        return spans;
    }

    private static void VetDynamicSockets(
        ReadOnlySpan<int> sockets,
        int xformTally)
    {
        int earlier = -1;
        for (int idx = 0; idx < sockets.Length; ++idx)
        {
            int latest = sockets[idx];
            if ((uint)latest >= (uint)xformTally)
            {
                throw new InvalidOperationException(
                    $"Dynamic shadow transform slot {latest} is beyond the "
                    + $"{xformTally}-matrix retained product");
            }
            if (latest <= earlier)
            {
                throw new InvalidOperationException(
                    "Dynamic shadow transform slots has to be strictly increasing");
            }
            earlier = latest;
        }
    }

    private void SecureSocketCap(int socketOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(socketOrdinal);
        if (_sockets.Length > socketOrdinal)
            return;
        int cap = _sockets.Length is 0 ? 2 : _sockets.Length;
        while (cap <= socketOrdinal)
            cap = checked(cap * 2);
        Array.Resize(ref _sockets, cap);
    }

    private record struct SlotLedger(
        IClientGpuBuffer? Buffer,
        ulong TopologyBuildSequence,
        int TransformCount,
        QueuedTransformGroup? Pending,
        ulong ConsumedDenseRevision);

    private sealed class QueuedTransformGroup
    {
        private int[] _sockets;
        private bool[] _marked;

        internal QueuedTransformGroup(int cap)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(cap);
            _sockets = new int[cap];
            _marked = new bool[cap];
        }

        internal int Count { get; private set; }

        internal long KeptOctets
        {
            get
            {
                return checked(
            (long)_sockets.Length * sizeof(int) + _marked.Length);
            }
        }

        internal void SecureCap(int cap)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(cap);
            if (_sockets.Length >= cap)
                return;
            Array.Resize(ref _sockets, cap);
            Array.Resize(ref _marked, cap);
        }

        internal void Flag(ReadOnlySpan<int> sockets)
        {
            for (int ordinal = 0; ordinal < sockets.Length; ++ordinal)
            {
                int socket = sockets[ordinal];
                if (_marked[socket])
                    continue;
                _marked[socket] = true;
                _sockets[Count++] = socket;
            }
        }

        internal ReadOnlySpan<int> FetchSorted()
        {
            Array.Sort(_sockets, 0, Count);
            return _sockets.AsSpan(0, Count);
        }

        internal void Clear()
        {
            for (int ordinal = 0; ordinal < Count; ++ordinal)
                _marked[_sockets[ordinal]] = false;
            Count = 0;
        }
    }
}

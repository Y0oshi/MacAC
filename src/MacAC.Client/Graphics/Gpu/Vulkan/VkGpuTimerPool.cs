using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGpuTimerPool : IGpuTickerReservoir, IDisposable
{
    // Distinct named scopes measurable per frame
    internal const int UpperScopesPerCycle = 16;

    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly double _stampPeriodNanoseconds;
    private readonly QueryPool[] _reservoirs;
    private readonly List<string>[] _ambitLabels;
    private readonly Dictionary<string, double> _settled = new(StringComparer.Ordinal);

    private int _latestSocket;
    private bool _destroyed;

    internal VkGpuTimerPool(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        int flightTally,
        bool isSupported)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        IsSupported = isSupported;

        vk.GetPhysicalDeviceProperties(physicalDev, out PhysicalDeviceProperties props);
        _stampPeriodNanoseconds = props.Limits.TimestampPeriod;

        _reservoirs = new QueryPool[flightTally];
        _ambitLabels = new List<string>[flightTally];
        for (int socket = 0; socket < flightTally; ++socket)
        {
            _ambitLabels[socket] = [];
            if (!isSupported)
                continue;

            QueryPoolCreateInfo build = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = UpperScopesPerCycle * 2,
            };
            VkInterop.Check(
                _vk.CreateQueryPool(_device, &build, null, out QueryPool reservoir),
                "vkCreateQueryPool (timer pool)");
            _reservoirs[socket] = reservoir;
        }
    }

    public bool IsSupported { get; }

    public bool TryLocate(string ambitLabel, out double millis) =>
        _settled.TryGetValue(ambitLabel, out millis);

    public bool TryGrabSettled(string ambitLabel, out double millis)
    {
        if (!_settled.TryGetValue(ambitLabel, out millis))
            return false;
        _settled.Remove(ambitLabel);
        return true;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        foreach (QueryPool reservoir in _reservoirs)
        {
            if (reservoir.Handle is not 0)
                _vk.DestroyQueryPool(_device, reservoir, null);
        }
    }

    internal void CommenceSocket(int socketOrdinal)
    {
        if (!IsSupported || _destroyed)
            return;

        _latestSocket = socketOrdinal;
        var labels = _ambitLabels[socketOrdinal];
        if (labels.Count > 0)
        {
            Resolve(socketOrdinal, labels);
            labels.Clear();
        }

        _vk.ResetQueryPool(_device, _reservoirs[socketOrdinal], 0, UpperScopesPerCycle * 2);
    }

    internal IDisposable CommenceAmbit(CommandBuffer directives, string ambitLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ambitLabel);
        if (!IsSupported || _destroyed)
            return ClientNullScope.Instance;

        var labels = _ambitLabels[_latestSocket];
        if (labels.Count >= UpperScopesPerCycle)
            return ClientNullScope.Instance;
        if (labels.Contains(ambitLabel, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"GPU timer scope '{ambitLabel}' has by now been measured this frame. Two ranges " +
                "sharing a name would silently report whichever finished last");
        }

        int ordinal = labels.Count;
        labels.Add(ambitLabel);
        _vk.CmdWriteTimestamp2(
            directives,
            PipelineStageFlags2.TopOfPipeBit,
            _reservoirs[_latestSocket],
            (uint)(ordinal * 2));
        return new EngagedAmbit(this, directives, _latestSocket, ordinal);
    }

    private void DisposeRest(CommandBuffer directives, int socketOrdinal, int ordinal)
    {
        if (!IsSupported || _destroyed)
            return;
        _vk.CmdWriteTimestamp2(
            directives,
            PipelineStageFlags2.BottomOfPipeBit,
            _reservoirs[socketOrdinal],
            (uint)((ordinal * 2) + 1));
    }

    private void Resolve(int socketOrdinal, List<string> labels)
    {
        int askTally = labels.Count * 2;
        Span<ulong> outcomes = stackalloc ulong[UpperScopesPerCycle * 2];
        fixed (ulong* lead = outcomes)
        {
            Result condition = _vk.GetQueryPoolResults(
                _device,
                _reservoirs[socketOrdinal],
                0,
                (uint)askTally,
                (nuint)(askTally * sizeof(ulong)),
                lead,
                sizeof(ulong),
                QueryResultFlags.Result64Bit);
            if (condition != Result.Success)
                return;
        }

        for (int idx = 0; idx < labels.Count; ++idx)
        {
            ulong begin = outcomes[idx * 2];
            ulong finish = outcomes[(idx * 2) + 1];
            if (finish <= begin)
                continue;
            double nanoseconds = (finish - begin) * _stampPeriodNanoseconds;
            _settled[labels[idx]] = nanoseconds / 1_000_000d;
        }
    }

    private sealed class EngagedAmbit(
        VkGpuTimerPool reservoir,
        CommandBuffer directives,
        int socketOrdinal,
        int ordinal) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
                return;
            _ended = true;
            reservoir.DisposeRest(directives, socketOrdinal, ordinal);
        }
    }

    private sealed class ClientNullScope : IDisposable
    {
        internal static ClientNullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

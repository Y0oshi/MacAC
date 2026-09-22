using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal interface IVkTimelineApi
{
    // Highest value the timeline has signalled
    ulong LatestVal { get; }

    // Blocks until the timeline reaches val
    void Wait(ulong val);
}

// Live implementation over one VkSemaphore of type timeline
internal sealed unsafe class VkTimelineApi(
    Silk.NET.Vulkan.Vk vk,
    Device dev,
    Semaphore timeline) : IVkTimelineApi
{
    private readonly Silk.NET.Vulkan.Vk _vk = vk ?? throw new ArgumentNullException(nameof(vk));

    public ulong LatestVal
    {
        get
        {
            VkInterop.Check(
                _vk.GetSemaphoreCounterValue(dev, timeline, out ulong val),
                "vkGetSemaphoreCounterValue");
            return val;
        }
    }

    public void Wait(ulong val)
    {
        Semaphore semaphore = timeline;
        ulong mark = val;
        SemaphoreWaitInfo pause = new SemaphoreWaitInfo
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &semaphore,
            PValues = &mark,
        };
        VkInterop.Check(
            _vk.WaitSemaphores(dev, &pause, ulong.MaxValue),
            $"vkWaitSemaphores (frame flight, value {val})");
    }
}

internal sealed class VkFrameFlightDriver : IGpuAssetSunsetFifo, IDisposable
{
    // Plan §4.8: two frames in flight
    internal const int DefaultCyclesInFlight = 2;

    private readonly IVkTimelineApi _timeline;
    private readonly SortedDictionary<long, List<Action>> _retirements = [];
    private readonly object _synchronize = new();

    private long _openSerialNo;
    private long _submittedSerialNo;
    private bool _destroyed;

    internal VkFrameFlightDriver(
        IVkTimelineApi timeline,
        int cyclesInFlight = DefaultCyclesInFlight)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        ArgumentOutOfRangeException.ThrowIfLessThan(cyclesInFlight, 1);
        SocketTally = cyclesInFlight;
    }

    internal int SocketTally { get; }

    internal long OpenSerialNo
    {
        get
        {
            lock (_synchronize)
                return _openSerialNo;
        }
    }

    internal long SubmittedSerialNo
    {
        get
        {
            lock (_synchronize)
                return _submittedSerialNo;
        }
    }

    internal int LatestSocket
    {
        get
        {
            lock (_synchronize)
                return SocketOrdinalOf(_openSerialNo);
        }
    }

    internal int QueuedSunsetTally
    {
        get
        {
            lock (_synchronize)
                return _retirements.Sum(listing => listing.Value.Count);
        }
    }

    public void Retire(Action free)
    {
        ArgumentNullException.ThrowIfNull(free);
        lock (_synchronize)
        {
            if (_destroyed)
            {
                free();
                return;
            }

            long tag = _openSerialNo is not 0 ? _openSerialNo : _submittedSerialNo + 1;
            if (!_retirements.TryGetValue(tag, out List<Action>? acts))
            {
                acts = [];
                _retirements.Add(tag, acts);
            }

            acts.Add(free);
        }
    }

    public void Dispose()
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            EmptyAll();
            _destroyed = true;
        }
    }

    // Maps a frame serial onto its flight slot
    internal int SocketOrdinalOf(long serialNo) =>
        serialNo <= 0 ? 0 : (int)((serialNo - 1) % SocketTally);

    internal long BeginFrame()
    {
        lock (_synchronize)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (_openSerialNo is not 0)
            {
                throw new InvalidOperationException(
                    $"Frame {_openSerialNo} is still open; call EndFrame prior to beginning another");
            }

            long serialNo = _submittedSerialNo + 1;
            long mustDone = serialNo - SocketTally;
            if (mustDone > 0)
                _timeline.Wait((ulong)mustDone);

            _openSerialNo = serialNo;
            ExecuteRetirements();
            return serialNo;
        }
    }

    // Records that the open frame has been submitted with its serial as the timeline signal value
    internal void EndFrame()
    {
        lock (_synchronize)
        {
            if (_openSerialNo is 0)
                return;
            _submittedSerialNo = _openSerialNo;
            _openSerialNo = 0;
        }
    }

    internal void ExecuteRetirements()
    {
        lock (_synchronize)
        {
            if (_retirements.Count is 0)
                return;

            long finished = (long)_timeline.LatestVal;
            while (_retirements.Count > 0)
            {
                var lead = _retirements.First();
                if (lead.Key > finished)
                    break;

                _retirements.Remove(lead.Key);
                foreach (Action free in lead.Value)
                    free();
            }
        }
    }

    internal void PauseForSubmittedJob()
    {
        lock (_synchronize)
        {
            if (_submittedSerialNo > 0)
                _timeline.Wait((ulong)_submittedSerialNo);
            EmptyAll();
        }
    }

    // Runs every pending retirement regardless of serial
    internal void EmptyAll()
    {
        lock (_synchronize)
        {
            while (_retirements.Count > 0)
            {
                var lead = _retirements.First();
                _retirements.Remove(lead.Key);
                foreach (Action free in lead.Value)
                    free();
            }
        }
    }
}

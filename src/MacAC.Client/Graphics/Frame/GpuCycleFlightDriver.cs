namespace MacAC.Client.Graphics;

internal interface IGpuAssetSunsetFifo
{
    void Retire(Action free);
}

internal sealed class RetryableGpuAssetFree
{
    private readonly Action[] _junctures;
    private bool _running;

    public RetryableGpuAssetFree(params Action[] stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Length is 0)
            throw new ArgumentException("At least one release stage is needed", nameof(stages));
        if (Array.Exists(stages, static juncture => juncture is null))
            throw new ArgumentException("Release stages can't contain null", nameof(stages));
        _junctures = stages;
    }

    public int FinishedJunctureTally { get; private set; }
    public bool IsComplete => FinishedJunctureTally == _junctures.Length;

    public void Run()
    {
        if (_running)
            return;

        _running = true;
        try
        {
            while (FinishedJunctureTally < _junctures.Length)
            {
                _junctures[FinishedJunctureTally]();
                ++FinishedJunctureTally;
            }
        }
        finally
        {
            _running = false;
        }
    }
}

internal sealed class GpuAssetAlterationFault(
    string msg,
    bool alterationSealed,
    Exception interiorException) : InvalidOperationException(msg, interiorException)
{
    public bool AlterationCommitted { get; } = alterationSealed;
}

internal sealed class GpuSunsetRegister(IGpuAssetSunsetFifo queue)
{
    private readonly IGpuAssetSunsetFifo _fifo = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly List<RetryableGpuAssetFree> _expectingBulletin = [];
    private readonly HashSet<RetryableGpuAssetFree> _publishing =
        new(ReferenceEqualityComparer.Instance);

    public int ExpectingBulletinTally => _expectingBulletin.Count;

    public void Retire(RetryableGpuAssetFree free)
    {
        ArgumentNullException.ThrowIfNull(free);
        _expectingBulletin.Add(free);
        BroadcastAt(_expectingBulletin.Count - 1);
    }

    public void RetireMany(IEnumerable<RetryableGpuAssetFree> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        RetryableGpuAssetFree[] lot = [.. releases];
        if (lot.Length is 0)
            return;
        if (Array.Exists(lot, static free => free is null))
            throw new ArgumentException("Retirement batches can't contain null", nameof(releases));

        _expectingBulletin.AddRange(lot);
        List<Exception>? misses = null;
        for (int idx = 0; idx < lot.Length; ++idx)
        {
            try { Publish(lot[idx]); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }
        if (misses is not null)
            throw new AggregateException(
                "One or more GPU retirement callbacks could not be published",
                misses);
    }

    public void ReattemptPendingPublications()
    {
        RetryableGpuAssetFree[] queued = [.. _expectingBulletin];
        List<Exception>? misses = null;
        for (int idx = 0; idx < queued.Length; ++idx)
        {
            var free = queued[idx];
            if (!_expectingBulletin.Contains(free, ReferenceEqualityComparer.Instance)
                || _publishing.Contains(free))

                continue;

            try
            {
                Publish(free);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        if (misses is not null)
            throw new AggregateException(
                "One or more GPU retirement callbacks could not be published",
                misses);
    }

    public void ReattemptQueuedBulletin(RetryableGpuAssetFree free)
    {
        ArgumentNullException.ThrowIfNull(free);
        if (!_expectingBulletin.Contains(free, ReferenceEqualityComparer.Instance)
            || _publishing.Contains(free))

            return;

        Publish(free);
    }

    private void BroadcastAt(int ordinal)
    {
        var free = _expectingBulletin[ordinal];
        Publish(free);
    }

    private void Publish(RetryableGpuAssetFree free)
    {
        if (!_publishing.Add(free))
            return;
        try
        {
            _fifo.Retire(free.Run);
            _expectingBulletin.Remove(free);
        }
        catch
        {
            if (free.IsComplete)
                _expectingBulletin.Remove(free);
            throw;
        }
        finally
        {
            _publishing.Remove(free);
        }
    }
}

internal sealed class ImmediateGpuAssetSunsetFifo : IGpuAssetSunsetFifo
{
    public static ImmediateGpuAssetSunsetFifo Instance { get; } = new();

    private ImmediateGpuAssetSunsetFifo()
    {
    }

    public void Retire(Action free)
    {
        ArgumentNullException.ThrowIfNull(free);
        free();
    }
}

internal sealed class GpuCycleFlightDriver :
    IGpuAssetSunsetFifo,
    IRasterizeCycleLifespan,
    IRasterizeCycleSocketOrigin,
    IDisposable
{
    internal const int DefaultCeilingCyclesInFlight = 3;
    private const ulong PauseSliceNanoseconds = 1_000_000;

    private readonly IClientGpuFenceApi _fenceApi;
    private readonly nint[] _fences;
    private readonly long[] _fenceSerials;
    private readonly SortedDictionary<long, List<Action>> _retirements = [];
    private long _previousSubmittedSerialNo;
    private bool _cycleOpen;
    private bool _destroyed;

    public int LatestSocket { get; private set; }
    public int SocketCount => _fences.Length;
    internal int QueuedSunsetTally => _retirements.Sum(listing => listing.Value.Count);

    internal GpuCycleFlightDriver(
        IClientGpuFenceApi fenceApi,
        int ceilingCyclesInFlight = DefaultCeilingCyclesInFlight)
    {
        ArgumentNullException.ThrowIfNull(fenceApi);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingCyclesInFlight, 1);

        _fenceApi = fenceApi;
        _fences = new nint[ceilingCyclesInFlight];
        _fenceSerials = new long[ceilingCyclesInFlight];
    }

    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_cycleOpen)
            throw new InvalidOperationException("EndFrame must close the current frame prior to BeginFrame is called again");

        nint fence = _fences[LatestSocket];
        if (fence != 0)
            RetireFence(LatestSocket);

        _cycleOpen = true;
    }

    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_cycleOpen)
            throw new InvalidOperationException("BeginFrame has to be called prior to EndFrame");
        if (_fences[LatestSocket] != 0)
            throw new InvalidOperationException("BeginFrame must retire the current frame slot prior to EndFrame");

        nint fence = _fenceApi.Slot();
        if (fence == 0)
            throw new InvalidOperationException("OpenGL didn't create an in-flight frame fence");

        _fences[LatestSocket] = fence;
        _fenceSerials[LatestSocket] = ++_previousSubmittedSerialNo;
        _cycleOpen = false;
        LatestSocket = (LatestSocket + 1) % _fences.Length;
    }

    public void Retire(Action free)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(free);

        long markSerialNo = _cycleOpen
            ? _previousSubmittedSerialNo + 1
            : _previousSubmittedSerialNo;
        if (markSerialNo is 0)
        {
            free();
            return;
        }

        if (!_retirements.TryGetValue(markSerialNo, out List<Action>? releases))
        {
            releases = [];
            _retirements.Add(markSerialNo, releases);
        }

        releases.Add(free);
    }

    public void PauseForSubmittedJob()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_cycleOpen)
            throw new InvalidOperationException("Can't drain submitted work while a render frame is open");

        List<Exception>? misses = null;
        for (int idx = 0; idx < _fences.Length; ++idx)
        {
            int socket = (LatestSocket + idx) % _fences.Length;
            try
            {
                RetireFence(socket);
            }
            catch (AggregateException exc)
            {
                (misses ??= []).AddRange(exc.InnerExceptions);
            }
        }

        try
        {
            ExecuteRetirementsThrough(_previousSubmittedSerialNo);
        }
        catch (AggregateException exc)
        {
            (misses ??= []).AddRange(exc.InnerExceptions);
        }

        if (misses is not null)
            throw new AggregateException("One or more GPU resource retirements failed", misses);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        if (_cycleOpen)
            EndFrame();
        PauseForSubmittedJob();

        Array.Clear(_fences);
        Array.Clear(_fenceSerials);
        _destroyed = true;
    }

    private void RetireFence(int socket)
    {
        nint fence = _fences[socket];
        if (fence == 0)
            return;

        bool drainDirectives = true;
        while (true)
        {
            var outcome = _fenceApi.Wait(
                fence,
                drainDirectives,
                PauseSliceNanoseconds);
            drainDirectives = false;

            if (outcome == GpuFencePauseOutcome.Timeout)
                continue;
            if (outcome == GpuFencePauseOutcome.Failed)
                throw new InvalidOperationException("OpenGL failed while waiting for an in-flight frame fence");

            break;
        }

        _fenceApi.Delete(fence);
        _fences[socket] = 0;
        long finishedSerialNo = _fenceSerials[socket];
        _fenceSerials[socket] = 0;
        ExecuteRetirementsThrough(finishedSerialNo);
    }

    private void ExecuteRetirementsThrough(long finishedSerialNo)
    {
        List<Exception>? misses = null;
        List<(long Serial, Action Release)>? reattempt = null;
        while (_retirements.Count is not 0)
        {
            KeyValuePair<long, List<Action>> lead;
            using (IEnumerator<KeyValuePair<long, List<Action>>> enumerator = _retirements.GetEnumerator())
            {
                if (!enumerator.MoveNext() || enumerator.Current.Key > finishedSerialNo)
                    break;
                lead = enumerator.Current;
            }

            if (!_retirements.Remove(lead.Key))
                break;

            var releases = lead.Value;
            for (int idx = 0; idx < releases.Count; ++idx)
            {
                try
                {
                    releases[idx]();
                }
                catch (Exception exc)
                {
                    (misses ??= []).Add(exc);
                    (reattempt ??= []).Add((lead.Key, releases[idx]));
                }
            }
        }

        if (reattempt is not null)
        {
            for (int idx = 0; idx < reattempt.Count; ++idx)
            {
                (long serialNo, Action free) = reattempt[idx];
                if (!_retirements.TryGetValue(serialNo, out List<Action>? releases))
                {
                    releases = [];
                    _retirements.Add(serialNo, releases);
                }
                releases.Add(free);
            }
        }

        if (misses is not null)
            throw new AggregateException("One or more GPU resource retirements failed", misses);
    }
}

internal enum GpuFencePauseOutcome
{
    Signaled,
    Timeout,
    Failed,
}

internal interface IClientGpuFenceApi
{
    nint Slot();
    GpuFencePauseOutcome Wait(nint fence, bool drainDirectives, ulong timeoutNanoseconds);
    void Delete(nint fence);
}


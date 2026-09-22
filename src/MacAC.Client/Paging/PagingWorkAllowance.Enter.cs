namespace MacAC.Client.Paging;

public sealed partial class PagingWorkMeter
{

    public PagingWorkAdmission TryAllocate(
        PagingWorkCost price,
        string juncture,
        bool secureHeadway = false)
    {
        price.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(juncture);
        if (_reservationEngaged)
            throw new InvalidOperationException(
                "The prior streaming operation hasn't completed");

        var threshold = SeekThreshold(price);
        bool grantEnsuredHeadway =
            secureHeadway && !_ensuredHeadwayGranted;
        if (threshold != PagingWorkLimit.None
            && _ops is not 0
            && !grantEnsuredHeadway)
        {
            ++_yields;
            _previousJuncture = juncture;
            _previousThreshold = threshold;
            return PagingWorkAdmission.Yielded;
        }

        _consumed = _consumed.Add(price);
        if (_lane == PagingWorkLane.Destination)
            _destConsumed = _destConsumed.Add(price);
        else
            _nonDestConsumed = _nonDestConsumed.Add(price);
        ++_ops;
        _reservationEngaged = true;
        _engagedJuncture = juncture;
        _engagedOpBegin = _stamp();
        if (secureHeadway)
            _ensuredHeadwayGranted = true;
        if (threshold != PagingWorkLimit.None)
        {
            _previousJuncture = juncture;
            _previousThreshold = threshold;
            ++_oversizedHeadway;
            return PagingWorkAdmission.OversizedProgress;
        }

        if (_previousThreshold == PagingWorkLimit.None)
            _previousJuncture = juncture;
        return PagingWorkAdmission.Admitted;
    }

    public void Complete()
    {
        SecureReservation();
        ConcludeLaneTiming();
        ++_completed;
        _reservationEngaged = false;
        WatchPassedOverrun();
        _engagedJuncture = null;
    }

    public void Fail()
    {
        SecureReservation();
        ConcludeLaneTiming();
        ++_misses;
        _reservationEngaged = false;
        WatchPassedOverrun();
        _engagedJuncture = null;
    }

    public void CompleteCycle()
    {
        if (_reservationEngaged)
            throw new InvalidOperationException(
                "A streaming operation is still reserved at frame end");
        WatchPassedOverrun();
    }
    internal LaneAmbit JoinLane(PagingWorkLane lane)
    {
        if (_reservationEngaged)
            throw new InvalidOperationException(
                "Can't change streaming lane during an active operation");

        var earlier = _lane;
        _lane = lane;
        return new LaneAmbit(this, earlier);
    }

    private void ConcludeLaneTiming()
    {
        long raw = Math.Max(0L, _stamp() - _engagedOpBegin);
        double passedMillis = raw * 1000.0 / _frequency;
        if (_ceilingOpJuncture is null
            || passedMillis > _ceilingOpMillis)
        {
            _ceilingOpMillis = passedMillis;
            _ceilingOpJuncture = _engagedJuncture;
        }
        long passed = raw > long.MaxValue / TimeSpan.TicksPerSecond
            ? long.MaxValue
            : raw * TimeSpan.TicksPerSecond / _frequency;
        if (_lane == PagingWorkLane.Destination)
            _destPassedBeats = SaturatingAppend(
                _destPassedBeats,
                passed);
        else
            _nonDestPassedBeats = SaturatingAppend(
                _nonDestPassedBeats,
                passed);
    }

    public PagingWorkMeterCapture Snapshot
    {
        get
        {
            long instant = _stamp();
            return new PagingWorkMeterCapture(
                _consumed,
                _destConsumed,
                _nonDestConsumed,
                _ops,
                _completed,
                _yields,
                _overruns,
                _oversizedHeadway,
                _misses,
                PassedMillis(instant),
                _ceilingOpMillis,
                _ceilingOpJuncture,
                _previousJuncture,
                _previousThreshold);
        }
    }

    private PagingWorkLimit SeekThreshold(PagingWorkCost price)
    {
        if (PassedBeats(_stamp()) >= _allowance.UpperRefreshMoment.Ticks)
            return PagingWorkLimit.Time;
        if ((long)_consumed.CompletionAdmissions + price.CompletionAdmissions
            > _allowance.UpperWrapUpAdmissions)

            return PagingWorkLimit.CompletionAdmissions;
        if (_consumed.AdoptedCpuBytes > _allowance.UpperAdoptedCpuOctets - price.AdoptedCpuBytes)
            return PagingWorkLimit.AdoptedCpuBytes;
        if ((long)_consumed.EntityOperations + price.EntityOperations
            > _allowance.UpperActorOps)

            return PagingWorkLimit.EntityOperations;
        if (_consumed.GpuUploadBytes > _allowance.UpperGpuPushOctets - price.GpuUploadBytes)
            return PagingWorkLimit.GpuUploadBytes;
        if ((long)_consumed.GlRetireOperations + price.GlRetireOperations
            > _allowance.UpperGlRetireOps)

            return PagingWorkLimit.GlRetireOperations;

        if (_destReservationEngaged
            && _lane == PagingWorkLane.NonDestination)
        {
            double unreservedRatio =
                1.0 - _allowance.DestAllocateRatio;
            long upperMomentBeats = ReservedThreshold(
                _allowance.UpperRefreshMoment.Ticks,
                unreservedRatio);
            if (_nonDestPassedBeats >= upperMomentBeats)
                return PagingWorkLimit.Time;
            if ((long)_nonDestConsumed.CompletionAdmissions
                    + price.CompletionAdmissions
                > ReservedThreshold(
                    _allowance.UpperWrapUpAdmissions,
                    unreservedRatio))

                return PagingWorkLimit.CompletionAdmissions;
            if (_nonDestConsumed.AdoptedCpuBytes
                > ReservedThreshold(
                    _allowance.UpperAdoptedCpuOctets,
                    unreservedRatio) - price.AdoptedCpuBytes)

                return PagingWorkLimit.AdoptedCpuBytes;
            if ((long)_nonDestConsumed.EntityOperations
                    + price.EntityOperations
                > ReservedThreshold(
                    _allowance.UpperActorOps,
                    unreservedRatio))

                return PagingWorkLimit.EntityOperations;
            if (_nonDestConsumed.GpuUploadBytes
                > ReservedThreshold(
                    _allowance.UpperGpuPushOctets,
                    unreservedRatio) - price.GpuUploadBytes)

                return PagingWorkLimit.GpuUploadBytes;
            if ((long)_nonDestConsumed.GlRetireOperations
                    + price.GlRetireOperations
                > ReservedThreshold(
                    _allowance.UpperGlRetireOps,
                    unreservedRatio))

                return PagingWorkLimit.GlRetireOperations;
        }
        return PagingWorkLimit.None;
    }

    private static long ReservedThreshold(long sum, double ratio) =>
        Math.Max(1L, (long)Math.Floor(sum * ratio));

    private static long SaturatingAppend(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private void ReinstateLane(PagingWorkLane lane)
    {
        if (_reservationEngaged)
            throw new InvalidOperationException(
                "Can't restore streaming lane during an active operation");
        _lane = lane;
    }

    private void SecureReservation()
    {
        if (!_reservationEngaged)
            throw new InvalidOperationException(
                "No streaming operation is reserved");
    }

    private void WatchPassedOverrun()
    {
        if (!_cycleOverrunRecorded
            && PassedBeats(_stamp()) > _allowance.UpperRefreshMoment.Ticks)
        {
            _cycleOverrunRecorded = true;
            ++_overruns;
            _previousThreshold = PagingWorkLimit.Time;
            _previousJuncture = _engagedJuncture ?? _previousJuncture;
        }
    }

    private long PassedBeats(long stamp)
    {
        long raw = Math.Max(0L, stamp - _begin);
        return raw > long.MaxValue / TimeSpan.TicksPerSecond
            ? long.MaxValue
            : raw * TimeSpan.TicksPerSecond / _frequency;
    }

    private double PassedMillis(long stamp) =>
        Math.Max(0L, stamp - _begin) * 1000.0 / _frequency;
}

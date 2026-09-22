using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public sealed partial class SimDealingTransactionLedger : IDisposable
{
    public const long CanonUseThrottleMsec = 200;

    private const long NeverConsumed = long.MinValue / 2;

    private readonly PackTransactionState _satchel;
    private long _previousUseMsec = NeverConsumed;
    private uint _previousUseSrc;
    private uint _previousUseMark;
    private uint _expectingAppraisal;
    private uint _latestAppraisal;
    private bool _expectingUseWrapUp;
    private uint _wipeEpoch;
    private long _rev;
    private bool _destroyed;

    public SimDealingTransactionLedger(PackTransactionState inventory)
    {
        _satchel = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public PackTransactionState Inventory => _satchel;
    public uint ExpectingAppraisalIdent => _expectingAppraisal;
    public uint LatestAppraisalTag => _latestAppraisal;
    public bool IsDisposed => _destroyed;
    public long Revision => Interlocked.Read(ref _rev);
    public SimItemUseFinish PreviousGearUseWrapUp { get; private set; }

    public SimDealingTransactionCapture CaptureOwnership()
    {
        return new(
        _destroyed,
        Revision,
        _previousUseSrc,
        _previousUseMark,
        _expectingAppraisal,
        _latestAppraisal,
        _outgoing.Count,
        _grab.Loaded is not null,
        _grab.Loaded?.Token ?? 0u,
        DispatchFailureCount,
        _use.Loaded is not null,
        _use.Loaded?.Token ?? 0u,
        _expectingUseWrapUp,
        PreviousGearUseWrapUp);
    }

    public bool TryAbsorbUseThrottle(long instantMsec)
    {
        Live();
        if (instantMsec - _previousUseMsec < CanonUseThrottleMsec)
            return false;
        _previousUseMsec = instantMsec;
        Touch();
        return true;
    }

    public ItemUseHold OpenUseReqReservation()
    {
        Live();
        return _satchel.CommenceUseReqReservation();
    }

    public SimDealingDispatchResult TryRelayUse(
        uint srvOid,
        bool possessedByAvatar,
        bool useable,
        ItemUseHold? reservation,
        ISimDealingTransport conveyance,
        out uint series)
    {
        Live();
        ArgumentNullException.ThrowIfNull(conveyance);
        series = 0u;

        SimDealingDispatchResult refusal;
        if (srvOid is 0u)
            refusal = SimDealingDispatchResult.Rejected;
        else if (!conveyance.IsInRealm)
            refusal = SimDealingDispatchResult.NotInWorld;
        else if (!possessedByAvatar && !useable)
            refusal = SimDealingDispatchResult.NotUseable;
        else if (!conveyance.TryTransmitUse(srvOid, out series))
            refusal = SimDealingDispatchResult.Rejected;
        else
        {
            reservation?.FlagDispatched();
            _previousUseSrc = srvOid;
            _previousUseMark = 0u;
            _expectingUseWrapUp = true;
            Touch();
            return SimDealingDispatchResult.Dispatched;
        }

        reservation?.AbortPriorRelay();
        return refusal;
    }

    public bool TryRelayTargetedUse(uint srcObjectIdent, uint markObjectIdent, Action<uint, uint>? relay, bool incrementOccupied)
    {
        Live();
        if (srcObjectIdent is 0u || markObjectIdent is 0u || relay is null)
            return false;

        uint epoch = _wipeEpoch;
        relay(srcObjectIdent, markObjectIdent);
        if (_destroyed || epoch != _wipeEpoch)
            return false;

        _previousUseSrc = srcObjectIdent;
        _previousUseMark = markObjectIdent;
        _expectingUseWrapUp = true;
        if (incrementOccupied)
            _satchel.IncrementOccupiedCount();
        Touch();
        return true;
    }

    public void IncrementBusyTally()
    {
        Live();
        _satchel.IncrementOccupiedCount();
        Touch();
    }

    public void CompleteUse(uint problem)
    {
        Live();
        int occupiedPrior = _satchel.OccupiedCount;
        _satchel.FinishUse(problem);
        if (_expectingUseWrapUp)
        {
            PreviousGearUseWrapUp = new SimItemUseFinish(PreviousGearUseWrapUp.Revision + 1, _previousUseSrc, _previousUseMark, problem);
            _expectingUseWrapUp = false;
            Touch();
        }
        else if (_satchel.OccupiedCount != occupiedPrior)
        {
            Touch();
        }
    }

    public bool TryReqAppraisal(uint objectIdent, Action<uint> transmitAppraisal)
    {
        Live();
        ArgumentNullException.ThrowIfNull(transmitAppraisal);
        if (objectIdent is 0u)
            return false;

        uint epoch = _wipeEpoch;
        bool tookOccupied = _expectingAppraisal is 0u;
        if (tookOccupied)
        {
            _satchel.IncrementOccupiedCount();
            if (_destroyed || epoch != _wipeEpoch)
                return false;
        }

        uint earlier = _expectingAppraisal;
        _expectingAppraisal = objectIdent;
        Touch();
        try
        {
            transmitAppraisal(objectIdent);
        }
        catch
        {
            // Undo only if nothing reset or re-targeted us while the send was failing.
            if (!_destroyed && epoch == _wipeEpoch && _expectingAppraisal == objectIdent)
            {
                _expectingAppraisal = earlier;
                if (tookOccupied)
                    _satchel.FinishUse(0u);
                Touch();
            }
            throw;
        }
        return true;
    }

    public SimAssessResponseAcceptance AllowAppraisalResponse(uint objectIdent)
    {
        Live();
        if (objectIdent is 0u || (objectIdent != _expectingAppraisal && objectIdent != _latestAppraisal))
            return default;

        bool lead = objectIdent == _expectingAppraisal;
        if (lead)
        {
            _expectingAppraisal = 0u;
            _latestAppraisal = objectIdent;
            _satchel.FinishUse(0u);
            Touch();
        }
        return new SimAssessResponseAcceptance(Accepted: true, FirstResponse: lead);
    }

    public bool UpdateLatestAppraisal(Action<uint> transmitAppraisal)
    {
        Live();
        ArgumentNullException.ThrowIfNull(transmitAppraisal);
        if (_latestAppraisal is 0u)
            return false;
        transmitAppraisal(_latestAppraisal);
        return true;
    }

    public bool RevokeObjectAppraisalForArcanum(Action<uint> transmitAppraisal)
    {
        Live();
        ArgumentNullException.ThrowIfNull(transmitAppraisal);
        if (_expectingAppraisal is 0u && _latestAppraisal is 0u)
            return false;

        if (_expectingAppraisal is not 0u)
            _satchel.FinishUse(0u);
        _expectingAppraisal = 0u;
        _latestAppraisal = 0u;
        Touch();
        transmitAppraisal(0u);
        return true;
    }

    public void ResetSession()
    {
        Live();
        Wipe();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        Wipe();
        _destroyed = true;
    }

    private void Touch() => Interlocked.Increment(ref _rev);

    private void Live() => ObjectDisposedException.ThrowIf(_destroyed, this);

    private void Wipe()
    {
        bool altered =
            _expectingAppraisal is not 0u || _latestAppraisal is not 0u || _outgoing.Count is not 0
            || _grab.Loaded is not null || _use.Loaded is not null
            || _previousUseSrc is not 0u || _previousUseMark is not 0u
            || _expectingUseWrapUp || PreviousGearUseWrapUp.Revision is not 0
            || _previousUseMsec != NeverConsumed;

        _use.Loaded?.Reservation?.AbortPriorRelay();

        _previousUseSrc = 0u;
        _previousUseMark = 0u;
        _expectingUseWrapUp = false;
        PreviousGearUseWrapUp = default;
        _expectingAppraisal = 0u;
        _latestAppraisal = 0u;
        _outgoing.Clear();
        _grab.Loaded = null;
        _use.Loaded = null;
        _previousUseMsec = NeverConsumed;
        ++_wipeEpoch;
        _satchel.ResetSession();
        if (altered)
            Touch();
    }
}

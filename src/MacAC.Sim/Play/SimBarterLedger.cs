using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct SimBarterHoldingCapture(bool IsDisposed, bool IsOpen, int StagedItemCount)
{
    public bool IsConverged => IsDisposed && !IsOpen && StagedItemCount is 0;
}

public enum SimBarterSide : uint
{
    Self = 1u,
    Partner = 2u,
}

public readonly record struct SimBarterCapture(
    long Revision,
    bool IsOpen,
    uint PartnerGuid,
    bool SelfAccepted,
    bool PartnerAccepted,
    int SelfItemCount,
    int PartnerItemCount,
    uint LastFailureItemGuid,
    uint LastFailureReason);

public interface ISimBarterLens
{
    SimBarterCapture Snapshot { get; }

    IReadOnlyList<uint> FetchGearList(SimBarterSide flank);
}

public sealed class SimBarterLedger : IDisposable
{
    private readonly object _latch = new();
    private readonly ClientThingChart? _objects;
    private readonly List<uint> _mine = [];
    private readonly List<uint> _theirs = [];
    private bool _open;
    private uint _partner;
    private bool _idxApproved;
    private bool _theyApproved;
    private uint _failedGear;
    private uint _failedCause;
    private long _rev;
    private bool _destroyed;

    public SimBarterLedger(ClientThingChart? objects = null)
    {
        _objects = objects;
        View = new Lens(this);
    }

    public ISimBarterLens View { get; }

    public bool IsDisposed
    {
        get { lock (_latch) return _destroyed; }
    }

    public void ImposeEnroll(PlaySignals.EnrollBarter refresh, uint selfOid)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            _open = true;
            _partner = refresh.Initiator != selfOid && refresh.Initiator is not 0u ? refresh.Initiator : refresh.Partner;
            _mine.Clear();
            _theirs.Clear();
            _idxApproved = false;
            _theyApproved = false;
            _failedGear = 0u;
            _failedCause = 0u;
            ++_rev;
        }
    }

    public void EnactShut()
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            Wipe();
        }
    }

    public void ImposeAppend(PlaySignals.AppendToBarter refresh)
    {
        WhileOpen(() =>
    {
        bool partnerFlank = refresh.Side == (uint)SimBarterSide.Partner;
        List<uint> flank = partnerFlank ? _theirs : _mine;
        if (!flank.Contains(refresh.ItemGuid))
            flank.Add(refresh.ItemGuid);
        if (!partnerFlank)
            Mark(refresh.ItemGuid, 1);
        _idxApproved = false;
        _theyApproved = false;
    });
    }

    public void ImposeDrop(PlaySignals.DropFromBarter refresh)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!_open)
                return;
            bool removed = _mine.Remove(refresh.ItemGuid);
            removed |= _theirs.Remove(refresh.ItemGuid);
            if (removed)
            {
                Mark(refresh.ItemGuid, 0);
                ++_rev;
            }
        }
    }

    public void ImposeAdmit(uint whoApproved, uint selfOid)
    {
        WhileOpen(() =>
    {
        if (whoApproved == selfOid)
            _idxApproved = true;
        else
            _theyApproved = true;
    });
    }

    /// <summary>0x0203 DeclineTrade - withdraws that side's acceptance.</summary>
    public void ImposeDecline(uint whoDeclined, uint selfOid)
    {
        WhileOpen(() =>
    {
        if (whoDeclined == selfOid)
            _idxApproved = false;
        else
            _theyApproved = false;
    });
    }

    public void ImposeRestart() => WhileOpen(UnstageAll);

    public void ImposeMiss(PlaySignals.BarterMiss miss)
    {
        WhileOpen(() =>
    {
        _mine.Remove(miss.ItemGuid);
        _theirs.Remove(miss.ItemGuid);
        Mark(miss.ItemGuid, 0);
        _failedGear = miss.ItemGuid;
        _failedCause = miss.Reason;
    });
    }

    public void ImposeWipeAcceptance()
    {
        WhileOpen(() =>
    {
        _idxApproved = false;
        _theyApproved = false;
    });
    }

    public void Clear()
    {
        lock (_latch)
        {
            if (!_destroyed)
                Wipe();
        }
    }

    public SimBarterHoldingCapture CaptureOwnership()
    {
        lock (_latch)
            return new SimBarterHoldingCapture(_destroyed, _open, _mine.Count + _theirs.Count);
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            Wipe();
            _destroyed = true;
        }
    }

    // Runs edit under the gate on an open trade and ticks the revision
    private void WhileOpen(Action edit)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!_open)
                return;
            edit();
            ++_rev;
        }
    }

    private void Mark(uint gearOid, int barterPhase)
    {
        if (_objects?.Get(gearOid) is { } gear)
            gear.BarterPhase = barterPhase;
    }

    private void UnstageAll()
    {
        foreach (uint oid in _mine)
            Mark(oid, 0);
        _mine.Clear();
        _theirs.Clear();
        _idxApproved = false;
        _theyApproved = false;
    }

    private void Wipe()
    {
        bool altered = _open || _mine.Count is not 0 || _theirs.Count is not 0 || _idxApproved || _theyApproved;
        UnstageAll();
        _open = false;
        _partner = 0u;
        _failedGear = 0u;
        _failedCause = 0u;
        if (altered)
            ++_rev;
    }

    private sealed class Lens(SimBarterLedger holder) : ISimBarterLens
    {
        public SimBarterCapture Snapshot
        {
            get
            {
                lock (holder._latch)
                {
                    return new SimBarterCapture(
                        holder._rev, holder._open, holder._partner, holder._idxApproved, holder._theyApproved,
                        holder._mine.Count, holder._theirs.Count, holder._failedGear, holder._failedCause);
                }
            }
        }

        public IReadOnlyList<uint> FetchGearList(SimBarterSide flank)
        {
            lock (holder._latch)
                return flank == SimBarterSide.Partner ? [.. holder._theirs] : [.. holder._mine];
        }
    }
}

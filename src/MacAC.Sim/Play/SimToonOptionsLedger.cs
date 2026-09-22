using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct SimToonOptionsCapture(uint Options1, uint Options2, long Revision)
{
    public bool DragItemOnPlayerOpensSecureTrade
    {
        get
        {
            return (Options1 & (uint)PlayerDescReader.ToonOptions1.DragItemOnPlayerOpensSecureTrade) is not 0u;
        }
    }
}

public sealed class SimToonOptionsLedger(TimeProvider? momentSupplier = null)
{
    public const uint DefaultOptions1 = (uint)PlayerDescReader.ToonOptions1.Default;
    public const uint DefaultOptions2 = 0x00948700u;

    public static TimeSpan AutoPersistDelay => TimeSpan.FromSeconds(480);

    private readonly TimeProvider _clock = momentSupplier ?? TimeProvider.System;
    private readonly object _staleLatch = new();
    private long _staleGen;
    private uint _word1 = DefaultOptions1;
    private uint _word2 = DefaultOptions2;
    private long _rev;
    private bool _stale;
    private DateTimeOffset _dirtiedAt;
    private bool _seeded;

    public uint Options1 => Volatile.Read(ref _word1);
    public uint Options2 => Volatile.Read(ref _word2);
    public long Revision => Interlocked.Read(ref _rev);
    public SimToonOptionsCapture Snapshot => new(_word1, _word2, Revision);

    public bool IsDirty
    {
        get { lock (_staleLatch) return _stale; }
    }

    public DateTimeOffset? LeadDirtiedAt
    {
        get { lock (_staleLatch) return _stale ? _dirtiedAt : null; }
    }

    public bool HasServerSeed
    {
        get { lock (_staleLatch) return _seeded; }
    }

    public bool DragItemOnPlayerOpensSecureTrade => Snapshot.DragItemOnPlayerOpensSecureTrade;

    public void Replace(uint options1, uint options2, bool armSrvSeed = true)
    {
        Store(options1, options2);
        lock (_staleLatch)
        {
            _stale = false;
            if (armSrvSeed)
                _seeded = true;
        }
    }

    public bool GetOptionBit(uint toonKnobIdent)
    {
        return ToonOptionChart.TryGet(toonKnobIdent, out ToonOptionChartEntry listing) && ((listing.IsOptions1 ? Options1 : Options2) & listing.Mask) is not 0u;
    }

    public bool GetOptionBit(CharacterOptionId toonKnobIdent) => GetOptionBit((uint)toonKnobIdent);

    public void AssignKnobBit(uint toonKnobIdent, bool val)
    {
        if (!ToonOptionChart.TryGet(toonKnobIdent, out ToonOptionChartEntry listing))
            return;

        if (listing.IsOptions1)
            Volatile.Write(ref _word1, val ? Options1 | listing.Mask : Options1 & ~listing.Mask);
        else
            Volatile.Write(ref _word2, val ? Options2 | listing.Mask : Options2 & ~listing.Mask);
        Interlocked.Increment(ref _rev);
    }

    public bool TrySetKnob(uint toonKnobIdent, bool val, Action<uint, bool> transmitAutoPersist)
    {
        ArgumentNullException.ThrowIfNull(transmitAutoPersist);
        if (!ToonOptionChart.TryGet(toonKnobIdent, out ToonOptionChartEntry listing))
            return false;

        uint word = listing.IsOptions1 ? Options1 : Options2;
        if (((word & listing.Mask) is not 0u) == val)
            return true;

        AssignKnobBit(toonKnobIdent, val);

        if (val)
        {
            switch ((CharacterOptionId)toonKnobIdent)
            {
                case CharacterOptionId.IgnoreFellowshipRequests:
                    TrySetKnob((uint)CharacterOptionId.FellowshipAutoAcceptRequests, false, transmitAutoPersist);
                    break;
                case CharacterOptionId.FellowshipAutoAcceptRequests:
                    TrySetKnob((uint)CharacterOptionId.IgnoreFellowshipRequests, false, transmitAutoPersist);
                    break;
            }
        }

        if (listing.IsAutoSave)
            transmitAutoPersist(toonKnobIdent, val);
        else
            StampStale();
        return true;
    }

    public void StampStale()
    {
        lock (_staleLatch)
        {
            ++_staleGen;
            if (_stale)
                return;
            _stale = true;
            _dirtiedAt = _clock.GetUtcNow();
        }
    }

    public bool TryDrain(Action drain) => Flush(drain, soleIfDue: false);

    public bool TryDrainIfAutoPersistDue(Action drain) => Flush(drain, soleIfDue: true);

    public void ResetSession()
    {
        Store(DefaultOptions1, DefaultOptions2);
        lock (_staleLatch)
        {
            _stale = false;
            _seeded = false;
        }
    }

    private void Store(uint word1, uint word2)
    {
        Volatile.Write(ref _word1, word1);
        Volatile.Write(ref _word2, word2);
        Interlocked.Increment(ref _rev);
    }

    // Runs the flush outside the gate; a dirtying that races the flush keeps the blob dirty
    private bool Flush(Action drain, bool soleIfDue)
    {
        ArgumentNullException.ThrowIfNull(drain);
        long gen;
        lock (_staleLatch)
        {
            if (!_stale || !_seeded)
                return false;
            if (soleIfDue && _clock.GetUtcNow() - _dirtiedAt < AutoPersistDelay)
                return false;
            gen = _staleGen;
        }
        drain();
        lock (_staleLatch)
        {
            if (_staleGen == gen)
                _stale = false;
        }
        return true;
    }
}

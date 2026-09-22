namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkWiringScopeArena
{
    private readonly int _depotMappingTally;
    private readonly int _uniformMappingTally;
    private readonly Func<uint, bool> _isDynamicDepot;

    private readonly ulong[] _depotBufs;
    private readonly uint[] _depotShifts;
    private readonly uint[] _depotSpans;
    private readonly ulong[] _uniformBufs;
    private readonly uint[] _uniformShifts;
    private readonly uint[] _uniformSpans;

    private readonly List<Entry> _listings = [];
    private int _engaged = -1;
    private bool _stale = true;

    private sealed class Entry(int depotMappingTally, int uniformMappingTally)
    {
        public ulong[] DepotBufs { get; } = new ulong[depotMappingTally];
        public uint[] DepotShifts { get; } = new uint[depotMappingTally];
        public uint[] DepotSpans { get; } = new uint[depotMappingTally];
        public ulong[] UniformBufs { get; } = new ulong[uniformMappingTally];
        public uint[] UniformSpans { get; } = new uint[uniformMappingTally];
        public int Slot { get; set; } = -1;
    }

    internal VkWiringScopeArena(
        int depotMappingTally,
        int uniformMappingTally,
        Func<uint, bool> isDynamicStorage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depotMappingTally);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uniformMappingTally);
        _depotMappingTally = depotMappingTally;
        _uniformMappingTally = uniformMappingTally;
        _isDynamicDepot = isDynamicStorage
            ?? throw new ArgumentNullException(nameof(isDynamicStorage));
        _depotBufs = new ulong[depotMappingTally];
        _depotShifts = new uint[depotMappingTally];
        _depotSpans = new uint[depotMappingTally];
        _uniformBufs = new ulong[uniformMappingTally];
        _uniformShifts = new uint[uniformMappingTally];
        _uniformSpans = new uint[uniformMappingTally];
    }

    // Distinct descriptor states this slot has ever materialised
    internal int Count => _listings.Count;

    internal int OnlineTally { get; private set; }

    internal void SeedDepot(uint mapping, ulong buf, uint shiftOctets, uint spanOctets)
    {
        _depotBufs[mapping] = buf;
        _depotShifts[mapping] = shiftOctets;
        _depotSpans[mapping] = spanOctets;
    }

    internal void SeedUniform(uint mapping, ulong buf, uint spanOctets)
    {
        _uniformBufs[mapping] = buf;
        _uniformSpans[mapping] = spanOctets;
    }

    internal void BeginFrame()
    {
        OnlineTally = 0;
        _engaged = -1;
        _stale = true;
    }

    internal void AssignDepot(uint mapping, ulong buf, uint shiftOctets, uint spanOctets)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mapping, (uint)_depotMappingTally);
        bool dynamic = _isDynamicDepot(mapping);
        if (_depotBufs[mapping] != buf
            || _depotSpans[mapping] != spanOctets
            || (!dynamic && _depotShifts[mapping] != shiftOctets))
        {
            _depotBufs[mapping] = buf;
            _depotSpans[mapping] = spanOctets;
            _stale = true;
        }

        _depotShifts[mapping] = shiftOctets;
    }

    internal void AssignUniform(uint mapping, ulong buf, uint shiftOctets, uint spanOctets)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mapping, (uint)_uniformMappingTally);
        if (_uniformBufs[mapping] != buf || _uniformSpans[mapping] != spanOctets)
        {
            _uniformBufs[mapping] = buf;
            _uniformSpans[mapping] = spanOctets;
            _stale = true;
        }

        _uniformShifts[mapping] = shiftOctets;
    }

    internal uint DepotShift(uint mapping) => _depotShifts[mapping];

    internal uint UniformShift(uint mapping) => _uniformShifts[mapping];

    internal ulong DepotBuf(uint mapping) => _depotBufs[mapping];

    internal uint DepotSpan(uint mapping) => _depotSpans[mapping];

    internal uint DepotDescriptorShift(uint mapping) =>
        _isDynamicDepot(mapping) ? 0u : _depotShifts[mapping];

    internal ulong UniformBuf(uint mapping) => _uniformBufs[mapping];

    internal uint UniformSpan(uint mapping) => _uniformSpans[mapping];

    internal (int Index, int Slot, bool NeedsWrite) Resolve()
    {
        if (!_stale && _engaged >= 0)
            return (_engaged, _listings[_engaged].Slot, false);

        for (int idx = 0; idx < _listings.Count; ++idx)
        {
            if (!Fits(_listings[idx]))
                continue;
            if (idx >= OnlineTally)
            {
                (_listings[idx], _listings[OnlineTally]) = (_listings[OnlineTally], _listings[idx]);
                _engaged = OnlineTally;
                ++OnlineTally;
            }
            else
            {
                _engaged = idx;
            }

            _stale = false;
            return (_engaged, _listings[_engaged].Slot, false);
        }

        while (_listings.Count <= OnlineTally)
            _listings.Add(new Entry(_depotMappingTally, _uniformMappingTally));

        Entry mark = _listings[OnlineTally];
        Adopt(mark);
        _engaged = OnlineTally;
        ++OnlineTally;
        _stale = false;
        return (_engaged, mark.Slot, true);
    }

    internal void AssignSocket(int ordinal, int socket) => _listings[ordinal].Slot = socket;

    private bool Fits(Entry listing)
    {
        if (listing.Slot < 0)
            return false;
        for (uint mapping = 0; mapping < _depotMappingTally; ++mapping)
        {
            if (listing.DepotBufs[mapping] != _depotBufs[mapping])
                return false;
            if (listing.DepotSpans[mapping] != _depotSpans[mapping])
                return false;
            if (!_isDynamicDepot(mapping) && listing.DepotShifts[mapping] != _depotShifts[mapping])
                return false;
        }

        for (uint mapping = 0; mapping < _uniformMappingTally; ++mapping)
        {
            if (listing.UniformBufs[mapping] != _uniformBufs[mapping])
                return false;
            if (listing.UniformSpans[mapping] != _uniformSpans[mapping])
                return false;
        }

        return true;
    }

    private void Adopt(Entry listing)
    {
        Array.Copy(_depotBufs, listing.DepotBufs, _depotMappingTally);
        Array.Copy(_depotShifts, listing.DepotShifts, _depotMappingTally);
        Array.Copy(_depotSpans, listing.DepotSpans, _depotMappingTally);
        Array.Copy(_uniformBufs, listing.UniformBufs, _uniformMappingTally);
        Array.Copy(_uniformSpans, listing.UniformSpans, _uniformMappingTally);
    }
}

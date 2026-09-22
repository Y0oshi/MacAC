namespace MacAC.Wire.Packets;

internal readonly record struct LeasedDatagram(
    DatagramHeader Header,
    LeasedHeaderExtras Optional,
    ReadOnlyMemory<byte> Body,
    ReadOnlyMemory<byte> FragmentBytes,
    int FragmentCount)
{
    public LeasedFragmentRun Fragments => new(FragmentBytes);
}

internal readonly record struct LeasedHeaderExtras(
    uint AckSequence,
    double TimeSync,
    float EchoRequestClientTime,
    uint FlowBytes,
    ushort FlowInterval,
    double ConnectRequestServerTime,
    ulong ConnectRequestCookie,
    uint ConnectRequestClientId,
    uint ConnectRequestServerSeed,
    uint ConnectRequestClientSeed,
    ReadOnlyMemory<byte> RawBytes,
    ReadOnlyMemory<byte> RetransmitRequestBytes,
    int RetransmitRequestCount,
    ReadOnlyMemory<byte> RejectRetransmitBytes,
    int RejectRetransmitCount);

internal readonly record struct LeasedFragment(WireFragmentHeader Header, ReadOnlyMemory<byte> Payload);

// Enumerates the fragments packed back to back in a datagram body; the bytes were validated at
// decode time
internal readonly struct LeasedFragmentRun(ReadOnlyMemory<byte> encoded)
{
    public WireEnumerator GetEnumerator() => new(encoded);

    internal struct WireEnumerator(ReadOnlyMemory<byte> rest)
    {
        private ReadOnlyMemory<byte> _rest = rest;

        public LeasedFragment Current { get; private set; }

        public bool MoveNext()
        {
            if (_rest.IsEmpty)
                return false;
            if (!WireFragment.TryDecodeBorrowed(_rest, out LeasedFragment fragment, out int consumed))
                throw new InvalidOperationException("validated packet contained an not valid fragment");
            Current = fragment;
            _rest = _rest.Slice(consumed);
            return true;
        }
    }
}

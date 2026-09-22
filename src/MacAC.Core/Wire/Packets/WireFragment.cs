namespace MacAC.Wire.Packets;

/// <summary>One blob fragment with its payload copied out of the datagram.</summary>
public readonly record struct WireFragment(WireFragmentHeader Header, byte[] Payload)
{
    /// <summary>Bytes on the wire, header included.</summary>
    public int WireSize => WireFragmentHeader.Size + Payload.Length;

    public static (WireFragment? fragment, int consumed) TryParse(ReadOnlySpan<byte> src)
    {
        if (!TryDecodeArrangement(src, out WireFragmentHeader preamble, out int cargoLen, out int consumed))
            return (null, 0);
        return (new WireFragment(preamble, src.Slice(WireFragmentHeader.Size, cargoLen).ToArray()), consumed);
    }

    internal static bool TryDecodeBorrowed(ReadOnlyMemory<byte> src, out LeasedFragment fragment, out int consumed)
    {
        if (!TryDecodeArrangement(src.Span, out WireFragmentHeader preamble, out int cargoLen, out consumed))
        {
            fragment = default;
            return false;
        }
        fragment = new LeasedFragment(preamble, src.Slice(WireFragmentHeader.Size, cargoLen));
        return true;
    }

    // Checks a fragment's header against its own size claims and the bytes actually present
    internal static bool TryDecodeArrangement(ReadOnlySpan<byte> src, out WireFragmentHeader preamble, out int cargoLen, out int consumed)
    {
        preamble = default;
        cargoLen = 0;
        consumed = 0;
        if (src.Length < WireFragmentHeader.Size)
            return false;

        preamble = WireFragmentHeader.Unpack(src);
        bool sane = preamble.SumDims >= WireFragmentHeader.Size
            && preamble.SumDims <= WireFragmentHeader.UpperFragmentDims
            && preamble.Count is not 0
            && preamble.Index < preamble.Count;
        if (!sane)
            return false;

        cargoLen = preamble.SumDims - WireFragmentHeader.Size;
        if (src.Length < preamble.SumDims)
            return false;

        consumed = preamble.SumDims;
        return true;
    }
}

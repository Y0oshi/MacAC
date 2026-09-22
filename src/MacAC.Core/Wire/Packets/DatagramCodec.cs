using MacAC.Wire.Cryptography;

namespace MacAC.Wire.Packets;

public static class DatagramCodec
{
    public enum UnpackProblem
    {
        None = 0,
        TooShort,
        HeaderSizeExceedsBuffer,
        InvalidOptionalHeader,
        InvalidFragment,
        ChecksumMismatch,
    }

    public readonly record struct DatagramDecodeOutcome(Datagram? Packet, UnpackProblem Error)
    {
        public bool IsOk => Error == UnpackProblem.None;
    }

    public static DatagramDecodeOutcome TryUnpack(ReadOnlySpan<byte> datagram, IsaacStream? incomingIsaac)
    {
        static DatagramDecodeOutcome Fail(UnpackProblem problem) => new(null, problem);

        if (datagram.Length < DatagramHeader.Size)
            return Fail(UnpackProblem.TooShort);

        Datagram packet = new Datagram { Header = DatagramHeader.Unpack(datagram) };
        int corpusLen = packet.Header.BlobDims;
        if (datagram.Length - DatagramHeader.Size < corpusLen)
            return Fail(UnpackProblem.HeaderSizeExceedsBuffer);

        var corpus = datagram.Slice(DatagramHeader.Size, corpusLen);
        packet.CorpusOctets = corpus.ToArray();

        int optionalLen = packet.Optional.Parse(corpus, packet.Header.Flags);
        if (optionalLen < 0)
            return Fail(UnpackProblem.InvalidOptionalHeader);

        if (packet.Header.HasFlag(DatagramHeaderFlags.BlobFragments))
        {
            var rest = corpus.Slice(optionalLen);
            while (!rest.IsEmpty)
            {
                (WireFragment? fragment, int consumed) = WireFragment.TryParse(rest);
                if (fragment is not { } decoded || consumed is 0)
                    return Fail(UnpackProblem.InvalidFragment);
                packet.Fragments.Add(decoded);
                rest = rest.Slice(consumed);
            }
        }

        uint preambleDigest = packet.Header.DerivePreambleHash32();
        uint cargoDigest = packet.Optional.DeriveHash32();
        foreach (WireFragment fragment in packet.Fragments)
            cargoDigest += DeriveFragmentHash32(fragment);

        if (packet.Header.HasFlag(DatagramHeaderFlags.EncryptedChecksum))
        {
            // The sealed word is (checksum - headerHash) ^ payloadHash and must match the next keystream word.
            if (incomingIsaac is null || ((packet.Header.Checksum - preambleDigest) ^ cargoDigest) != incomingIsaac.Next())
                return Fail(UnpackProblem.ChecksumMismatch);
        }
        else if (packet.Header.Checksum != preambleDigest + cargoDigest)
        {
            return Fail(UnpackProblem.ChecksumMismatch);
        }

        return new DatagramDecodeOutcome(packet, UnpackProblem.None);
    }

    public static byte[] Serialize(DatagramHeader preamble, ReadOnlySpan<byte> body, IsaacStream? outgoingIsaac)
    {
        int optionalLen = new DatagramHeaderExtras().Parse(body, preamble.Flags);
        if (optionalLen < 0)
            throw new ArgumentException("body's optional section is malformed", nameof(body));

        byte[] datagram = new byte[DatagramHeader.Size + body.Length];
        body.CopyTo(datagram.AsSpan(DatagramHeader.Size));
        FinalizeInPlace(preamble, datagram, body.Length, optionalLen, outgoingIsaac);
        return datagram;
    }

    public static uint DeriveFragmentHash32(in WireFragment fragment)
    {
        Span<byte> preamble = stackalloc byte[WireFragmentHeader.Size];
        fragment.Header.Pack(preamble);
        return CanonHash32.Work(preamble) + CanonHash32.Work(fragment.Payload);
    }

    // Zero-copy decode that leaves checksum verification to the caller (see VerifyChecksum)
    internal static bool TryDecodeBorrowed(ReadOnlyMemory<byte> datagram, out LeasedDatagram packet, out uint preambleDigest, out uint cargoDigest, out UnpackProblem problem)
    {
        packet = default;
        preambleDigest = 0;
        cargoDigest = 0;

        var wire = datagram.Span;
        if (wire.Length < DatagramHeader.Size)
        {
            problem = UnpackProblem.TooShort;
            return false;
        }

        var preamble = DatagramHeader.Unpack(wire);
        if (wire.Length - DatagramHeader.Size < preamble.BlobDims)
        {
            problem = UnpackProblem.HeaderSizeExceedsBuffer;
            return false;
        }

        var corpus = datagram.Slice(DatagramHeader.Size, preamble.BlobDims);
        if (!OptionalLayout.TryScan(corpus.Span, preamble.Flags, out OptionalLayout arrangement))
        {
            problem = UnpackProblem.InvalidOptionalHeader;
            return false;
        }

        var fragmentOctets = ReadOnlyMemory<byte>.Empty;
        int fragmentTally = 0;
        uint fragmentDigest = 0;
        if (preamble.HasFlag(DatagramHeaderFlags.BlobFragments))
        {
            fragmentOctets = corpus.Slice(arrangement.Consumed);
            if (!TryDigestFragments(fragmentOctets.Span, out fragmentDigest, out fragmentTally))
            {
                problem = UnpackProblem.InvalidFragment;
                return false;
            }
        }

        LeasedHeaderExtras optional = new LeasedHeaderExtras(
            arrangement.AckSequence,
            arrangement.TimeSync,
            arrangement.EchoReqClientTime,
            arrangement.FlowBytes,
            arrangement.FlowInterval,
            arrangement.LinkRequestServerTime,
            arrangement.LinkRequestCookie,
            arrangement.LinkReqClientTag,
            arrangement.LinkRequestServerSeed,
            arrangement.LinkRequestClientSeed,
            corpus.Slice(0, arrangement.Consumed),
            arrangement.RetransmitTally is 0 ? ReadOnlyMemory<byte>.Empty : corpus.Slice(arrangement.RetransmitShift, arrangement.RetransmitTally * 4),
            arrangement.RetransmitTally,
            arrangement.RejectTally is 0 ? ReadOnlyMemory<byte>.Empty : corpus.Slice(arrangement.RejectShift, arrangement.RejectTally * 4),
            arrangement.RejectTally);

        preambleDigest = preamble.DerivePreambleHash32();
        cargoDigest = CanonHash32.Work(optional.RawBytes.Span) + fragmentDigest;
        packet = new LeasedDatagram(preamble, optional, corpus, fragmentOctets, fragmentTally);
        problem = UnpackProblem.None;
        return true;
    }

    internal static bool VerifyChecksum(in DatagramHeader preamble, uint preambleDigest, uint cargoDigest, uint? isaacTag)
    {
        return preamble.Checksum == preambleDigest + (isaacTag is { } tag ? tag ^ cargoDigest : cargoDigest);
    }

    internal static int FinalizeInPlace(DatagramHeader preamble, Span<byte> datagram, int corpusLen, int optionalLen, IsaacStream? outgoingIsaac)
    {
        return FinalizeInPlace(preamble, datagram, corpusLen, optionalLen, outgoingIsaac, out _, out _);
    }

    // Stamps the size and checksum into preamble and writes it over the first 20 bytes; the body must
    // already be in place
    internal static int FinalizeInPlace(
        DatagramHeader preamble,
        Span<byte> datagram,
        int bodyLength,
        int optionalLength,
        IsaacStream? outgoingIsaac,
        out uint isaacTagConsumed,
        out uint sealedChecksum)
    {
        if ((uint)bodyLength > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(bodyLength));
        int len = checked(DatagramHeader.Size + bodyLength);
        if (datagram.Length < len)
            throw new ArgumentException($"datagram has to be no fewer than {len} bytes", nameof(datagram));
        if ((uint)optionalLength > (uint)bodyLength)
            throw new ArgumentOutOfRangeException(nameof(optionalLength));

        uint cargoDigest = CargoDigest(datagram.Slice(DatagramHeader.Size, bodyLength), preamble.Flags, optionalLength);

        preamble.BlobDims = checked((ushort)bodyLength);
        uint preambleDigest = preamble.DerivePreambleHash32();
        if (preamble.HasFlag(DatagramHeaderFlags.EncryptedChecksum))
        {
            if (outgoingIsaac is null)
                throw new InvalidOperationException("EncryptedChecksum flag set but no ISAAC keystream provided");
            isaacTagConsumed = outgoingIsaac.Next();
            sealedChecksum = isaacTagConsumed ^ cargoDigest;
        }
        else
        {
            isaacTagConsumed = 0;
            sealedChecksum = cargoDigest;
        }

        preamble.Checksum = preambleDigest + sealedChecksum;
        preamble.Pack(datagram);
        return len;
    }

    // Hashes each fragment (header and payload separately, as retail does) and counts them
    private static bool TryDigestFragments(ReadOnlySpan<byte> octets, out uint digest, out int tally)
    {
        digest = 0;
        tally = 0;
        while (!octets.IsEmpty)
        {
            if (!WireFragment.TryDecodeArrangement(octets, out _, out int cargoLen, out int consumed))
                return false;
            digest += CanonHash32.Work(octets.Slice(0, WireFragmentHeader.Size))
                + CanonHash32.Work(octets.Slice(WireFragmentHeader.Size, cargoLen));
            ++tally;
            octets = octets.Slice(consumed);
        }
        return true;
    }

    private static uint CargoDigest(ReadOnlySpan<byte> body, DatagramHeaderFlags flagSet, int optionalLen)
    {
        uint optionalDigest = CanonHash32.Work(body.Slice(0, optionalLen));
        if ((flagSet & DatagramHeaderFlags.BlobFragments) == 0)
        {
            if (optionalLen != body.Length)
                throw new ArgumentException("non-fragment body contains bytes beyond the optional section", nameof(body));
            return optionalDigest;
        }

        if (!TryDigestFragments(body.Slice(optionalLen), out uint fragmentDigest, out _))
            throw new ArgumentException("body contains a malformed fragment", nameof(body));
        return optionalDigest + fragmentDigest;
    }
}

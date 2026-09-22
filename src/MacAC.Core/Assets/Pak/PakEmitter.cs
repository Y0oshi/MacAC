using System.Buffers;
using System.Security.Cryptography;

namespace MacAC.Assets.Pak;

public sealed class PakEmitter : IDisposable
{
    private const int Alignment = 64;
    private const int TextureIdentOctets = 7;

    private readonly record struct BitmapPersona(int Length, byte[] Sha256);

    private readonly FileStream _flow;
    private readonly PakPreamble _preamble;
    private readonly List<PakTocListing> _toc = [];
    private readonly HashSet<ulong> _claimedTags = [];
    private readonly Dictionary<ulong, PakTocListing> _rankByTag = new();
    private readonly Dictionary<ulong, BitmapPersona> _textureByTag = new();
    private bool _finished;

    public int TextureBlobTally { get; private set; }
    public int ListingTally => _toc.Count;
    public int PhysicalBlobTally { get; private set; }
    public long DecodedCargoOctets { get; private set; }
    public long StoredCargoOctets { get; private set; }
    public int CompressedBlobTally { get; private set; }

    public PakEmitter(string trail, PakPreamble preambleBlueprint)
    {
        _flow = new FileStream(trail, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _preamble = preambleBlueprint;
        _preamble.FmtVer = PakFmt.LatestFmtVer;
        _preamble.BakeToolVer = PakFmt.LatestBakeToolVer;

        _preamble.WriteTo(_flow);
        PadToAlignment();
    }

    public void AddBlob(ulong tag, HarvestedMesh blob)
    {
        HurlIfFinished();
        Claim(tag);

        using MemoryStream buf = new MemoryStream();
        HarvestedMeshCodec.EmitExternalTextures(blob, buf, ShelveTexture);
        if (!buf.TryGetBuffer(out ArraySegment<byte> written))
            throw new InvalidOperationException("mesh serializer didn't expose its write buffer");
        EmitBlob(tag, written.AsSpan(0, checked((int)buf.Length)));
    }

    public void AddBlob(ulong tag, byte[] octets)
    {
        ArgumentNullException.ThrowIfNull(octets);
        HurlIfFinished();
        Claim(tag);
        EmitBlob(tag, octets);
    }

    public void AppendAlias(ulong aliasKey, ulong extantTag)
    {
        HurlIfFinished();
        if (aliasKey == extantTag)
            throw new ArgumentException("an alias key must differ from its source key", nameof(aliasKey));
        if (!_rankByTag.TryGetValue(extantTag, out PakTocListing src))
            throw new KeyNotFoundException($"source pak key 0x{extantTag:X16} hasn't been written");
        if (!_claimedTags.Add(aliasKey))
            throw new ArgumentException($"duplicate pak key 0x{aliasKey:X16}", nameof(aliasKey));

        PakTocListing alias = src;
        alias.Key = aliasKey;
        _toc.Add(alias);
        _rankByTag.Add(aliasKey, alias);
    }

    public void Finish()
    {
        HurlIfFinished();
        _finished = true;

        _toc.Sort(static (entry, b) => entry.Key.CompareTo(b.Key));
        ulong tocShift = (ulong)_flow.Position;
        foreach (PakTocListing rank in _toc)
            rank.WriteTo(_flow);

        PakPreamble preamble = _preamble;
        preamble.TocShift = tocShift;
        preamble.TocTally = (uint)_toc.Count;
        _flow.Position = 0;
        preamble.WriteTo(_flow);
        _flow.Flush();
    }

    public void Dispose()
    {
        try
        {
            if (!_finished)
                Finish();
        }
        finally
        {
            _flow.Dispose();
        }
    }

    // Stores one (already claimed) key's payload, compressed when that pays
    private void EmitBlob(ulong tag, ReadOnlySpan<byte> octets)
    {
        bool compressed = PakBlobPacker.TryCompress(octets, out byte[]? rented, out int storedLen);
        try
        {
            ReadOnlySpan<byte> stored = compressed ? rented.AsSpan(0, storedLen) : octets;
            long shift = _flow.Position;
            _flow.Write(stored);
            PadToAlignment();

            PakTocListing rank = new PakTocListing
            {
                Key = tag,
                Offset = (ulong)shift,
                Length = checked((uint)stored.Length) | (compressed ? PakTocListing.CompressionBit : 0u),
                Crc32 = AssetCrc32.Compute(stored),
            };
            _toc.Add(rank);
            _rankByTag.Add(tag, rank);
            ++PhysicalBlobTally;
            DecodedCargoOctets = checked(DecodedCargoOctets + octets.Length);
            StoredCargoOctets = checked(StoredCargoOctets + stored.Length);
            if (compressed)
                ++CompressedBlobTally;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // Texture payload keys are the first 56 bits of the SHA-256 of the bytes; a repeat returns the
    // existing key
    private ulong ShelveTexture(BitmapTag _, byte[] octets)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(octets, digest);
        ulong cargoIdent = 0;
        for (int idx = 0; idx < TextureIdentOctets; ++idx)
            cargoIdent = (cargoIdent << 8) | digest[idx];
        ulong tag = PakTag.ConstructSolid(PakAssetKind.TexturePayload, cargoIdent);

        if (_textureByTag.TryGetValue(tag, out BitmapPersona recognized))
        {
            if (recognized.Length != octets.Length || !CryptographicOperations.FixedTimeEquals(recognized.Sha256, digest))
                throw new InvalidDataException($"texture payload identity collision at key 0x{tag:X16}");
            return tag;
        }

        Claim(tag);
        EmitBlob(tag, octets);
        _textureByTag.Add(tag, new BitmapPersona(octets.Length, digest.ToArray()));
        ++TextureBlobTally;
        return tag;
    }

    private void Claim(ulong key)
    {
        if (!_claimedTags.Add(key))
            throw new ArgumentException($"duplicate pak key 0x{key:X16}", nameof(key));
    }

    private void PadToAlignment()
    {
        long remainder = _flow.Position % Alignment;
        if (remainder is 0)
            return;
        Span<byte> zeros = stackalloc byte[(int)(Alignment - remainder)];
        zeros.Clear();
        _flow.Write(zeros);
    }

    private void HurlIfFinished()
    {
        if (_finished)
            throw new InvalidOperationException("PakWriter.Finish() has by now been called");
    }
}

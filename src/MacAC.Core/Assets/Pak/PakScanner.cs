using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;

namespace MacAC.Assets.Pak;

public sealed class PakScanner : IDisposable
{
    private enum Verdict
    {
        Bad = 0,
        Ok = 1,
    }

    private readonly MemoryMappedFile _lookup;
    private readonly MemoryMappedViewAccessor _lens;
    private readonly PakTocListing[] _toc; // sorted ascending by Key
    private readonly PakTextureShelf _textures = new();

    // Lazy per-entry verdict: absent = not yet judged
    private readonly ConcurrentDictionary<int, Verdict> _verdicts = new();
    private readonly ConcurrentDictionary<int, bool> _reported = new();

    public PakPreamble Header { get; }

    public long FileLen { get; }

    public PakScanner(string trail)
    {
        FileLen = new FileInfo(trail).Length;
        if (FileLen < PakPreamble.Size)
            throw new InvalidDataException($"pak file '{trail}' is {FileLen} bytes - smaller than the {PakPreamble.Size}-byte header");

        _lookup = MemoryMappedFile.CreateFromFile(trail, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        _lens = _lookup.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        byte[] preambleOctets = new byte[PakPreamble.Size];
        _lens.ReadArray(0, preambleOctets, 0, PakPreamble.Size);
        Header = PakPreamble.ReadFrom((ReadOnlySpan<byte>)preambleOctets);

        if (Header.FmtVer != PakFmt.LatestFmtVer)
        {
            throw new InvalidDataException(
                $"pak file '{trail}' has format version {Header.FmtVer}; this build reads only " +
                $"version {PakFmt.LatestFmtVer}. Re-bake with the matching macac-bake");
        }
        if (Header.TocShift < PakPreamble.Size)
        {
            throw new InvalidDataException(
                $"pak file '{trail}' has an unfinalized header (tocOffset={Header.TocShift}) — " +
                "the bake was interrupted prior to Finish(); re-bake");
        }
        if ((long)Header.TocShift + (long)Header.TocTally * PakTocListing.Size > FileLen)
        {
            throw new InvalidDataException(
                $"pak file '{trail}' TOC (offset {Header.TocShift}, {Header.TocTally} entries) extends past " +
                $"the file's actual length ({FileLen} bytes) - truncated or corrupt file");
        }

        _toc = new PakTocListing[Header.TocTally];
        byte[] rank = new byte[PakTocListing.Size];
        long at = (long)Header.TocShift;
        for (int idx = 0; idx < _toc.Length; ++idx, at += PakTocListing.Size)
        {
            _lens.ReadArray(at, rank, 0, PakTocListing.Size);
            _toc[idx] = PakTocListing.ScanFrom((ReadOnlySpan<byte>)rank);

            ref readonly PakTocListing listing = ref _toc[idx];
            if (!InSpan(listing))
            {
                _verdicts[idx] = Verdict.Bad;
                AnnounceOnce(idx, $"TOC entry out of bounds (offset={listing.Offset}, storedLength={listing.StoredLen}, file={FileLen}, toc@{Header.TocShift})");
            }
        }
    }

    /// <summary>True if <paramref name="tag"/> is present AND its blob verifies (bounds + CRC).</summary>
    public bool ContainsKey(ulong tag)
    {
        int ordinal = Find(tag);
        return ordinal >= 0 && Judge(ordinal) == Verdict.Ok;
    }

    public PakListingPhase InspectListing(ulong tag)
    {
        int ordinal = Find(tag);
        if (ordinal < 0)
            return PakListingPhase.Missing;
        return _verdicts.TryGetValue(ordinal, out Verdict verdict) && verdict == Verdict.Bad ? PakListingPhase.Corrupt : PakListingPhase.Available;
    }

    public bool TryScanObjectTriMeshBlob(ulong tag, out HarvestedMesh? blob) => ScanObjectTriMeshBlob(tag, out blob) == PakReadOutcome.Loaded;

    public PakReadOutcome ScanObjectTriMeshBlob(ulong tag, out HarvestedMesh? blob)
    {
        blob = null;
        if (PakTag.Decompose(tag).Type is not (PakAssetKind.GfxObjMesh or PakAssetKind.SetupMesh or PakAssetKind.EnvCellMesh))
            return PakReadOutcome.Missing;

        var verdict = ScanBlobOctets(tag, out byte[]? octets);
        if (verdict != PakReadOutcome.Loaded || octets is null)
            return verdict;

        try
        {
            blob = HarvestedMeshCodec.ScanExternalTextures(octets, TextureCargo);
            return PakReadOutcome.Loaded;
        }
        catch (Exception exc)
        {
            blob = null;
            FlagCargoCorrupt(tag, $"deserialization failed despite matching CRC: {exc.GetType().Name}: {exc.Message}");
            return PakReadOutcome.Corrupt;
        }
    }

    public long FetchBlobShiftForTest(ulong tag) => (long)_toc[Demand(tag)].Offset;

    public PakTocListing FetchTocListingForTest(ulong tag) => _toc[Demand(tag)];

    public int TallyListings(PakAssetKind kind)
    {
        byte wanted = (byte)kind;
        int tally = 0;
        foreach (ref readonly PakTocListing listing in _toc.AsSpan())
        {
            if ((byte)(listing.Key >> 56) == wanted)
                ++tally;
        }
        return tally;
    }

    public void VetTocStructure()
    {
        ulong earlier = 0;
        for (int idx = 0; idx < _toc.Length; ++idx)
        {
            ref readonly PakTocListing listing = ref _toc[idx];
            if (idx > 0 && listing.Key <= earlier)
                throw new InvalidDataException($"pak TOC isn't strictly sorted at entry {idx}: 0x{listing.Key:X16} follows 0x{earlier:X16}");
            earlier = listing.Key;

            if (!InSpan(listing))
            {
                throw new InvalidDataException(
                    $"pak TOC entry 0x{listing.Key:X16} has an not valid range " +
                    $"(offset={listing.Offset}, storedLength={listing.StoredLen}, " +
                    $"file={FileLen}, toc@{Header.TocShift})");
            }
        }
    }

    public bool DiagLinearScanContainsTag(ulong tag)
    {
        for (int idx = 0; idx < _toc.Length; ++idx)
        {
            if (_toc[idx].Key == tag)
                return Judge(idx) == Verdict.Ok;
        }
        return false;
    }

    public void Dispose()
    {
        _lens.Dispose();
        _lookup.Dispose();
    }

    internal PakReadOutcome ScanBlobOctets(ulong tag, out byte[]? octets)
    {
        octets = null;
        int ordinal = Find(tag);
        if (ordinal < 0)
            return PakReadOutcome.Missing;

        bool judged = _verdicts.TryGetValue(ordinal, out Verdict verdict);
        if (judged && verdict == Verdict.Bad)
            return PakReadOutcome.Corrupt;

        ref readonly PakTocListing listing = ref _toc[ordinal];
        octets = Slice(listing);

        if (!judged)
        {
            uint crc = AssetCrc32.Compute(octets);
            if (crc != listing.Crc32)
            {
                _verdicts[ordinal] = Verdict.Bad;
                AnnounceOnce(ordinal, $"crc mismatch (expected 0x{listing.Crc32:X8}, got 0x{crc:X8})");
                octets = null;
                return PakReadOutcome.Corrupt;
            }
            _verdicts[ordinal] = Verdict.Ok;
        }

        if (listing.IsCompressed)
        {
            try
            {
                octets = PakBlobPacker.Unpack(octets);
            }
            catch (Exception exc) when (exc is InvalidDataException or OverflowException)
            {
                _verdicts[ordinal] = Verdict.Bad;
                AnnounceOnce(ordinal, $"decompression failed despite matching CRC: {exc.Message}");
                octets = null;
                return PakReadOutcome.Corrupt;
            }
        }

        return PakReadOutcome.Loaded;
    }

    internal void FlagCargoCorrupt(ulong tag, string cause)
    {
        int ordinal = Find(tag);
        if (ordinal < 0)
            return;
        _verdicts[ordinal] = Verdict.Bad;
        AnnounceOnce(ordinal, cause);
    }

    private byte[] Slice(in PakTocListing listing)
    {
        int len = checked((int)listing.StoredLen);
        byte[] octets = new byte[len];
        _lens.ReadArray((long)listing.Offset, octets, 0, len);
        return octets;
    }

    private byte[] TextureCargo(ulong tag)
    {
        if (PakTag.Decompose(tag).Type != PakAssetKind.TexturePayload)
            throw new InvalidDataException($"mesh references non-texture pak key 0x{tag:X16}");
        if (_textures.TryGet(tag, out byte[] shelved))
            return shelved;

        var verdict = ScanBlobOctets(tag, out byte[]? fetched);
        if (verdict != PakReadOutcome.Loaded || fetched is null)
            throw new InvalidDataException($"mesh references {verdict.ToString().ToLowerInvariant()} texture payload 0x{tag:X16}");
        return _textures.AppendOrFetch(tag, fetched);
    }

    private int Demand(ulong tag)
    {
        int ordinal = Find(tag);
        return ordinal >= 0 ? ordinal : throw new KeyNotFoundException($"pak key 0x{tag:X16} not found");
    }

    // The entry's verdict, checking the CRC now if it has never been checked
    private Verdict Judge(int ordinal)
    {
        if (_verdicts.TryGetValue(ordinal, out Verdict recognized))
            return recognized;

        ref readonly PakTocListing listing = ref _toc[ordinal];
        uint crc = AssetCrc32.Compute(Slice(listing));
        Verdict verdict = crc == listing.Crc32 ? Verdict.Ok : Verdict.Bad;
        _verdicts[ordinal] = verdict;
        if (verdict == Verdict.Bad)
            AnnounceOnce(ordinal, $"crc mismatch (expected 0x{listing.Crc32:X8}, got 0x{crc:X8})");
        return verdict;
    }

    private void AnnounceOnce(int ordinal, string cause)
    {
        if (!_reported.TryAdd(ordinal, true))
            return;
        ref readonly PakTocListing listing = ref _toc[ordinal];
        Console.Error.WriteLine(
            $"[pak-corrupt] key 0x{listing.Key:X16} at offset {listing.Offset} " +
            $"(storedLength {listing.StoredLen}, compressed={listing.IsCompressed}): " +
            $"{cause} - treating as absent");
    }

    private int Find(ulong tag)
    {
        int lo = 0, hi = _toc.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            ulong sensor = _toc[mid].Key;
            if (sensor == tag)
                return mid;
            if (sensor < tag)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return -1;
    }

    // A blob must start after the header, end before the TOC and the file, and stay under the payload
    // cap
    private bool InSpan(in PakTocListing listing)
    {
        ulong fileLen = (ulong)FileLen;
        if (listing.Offset < PakPreamble.Size || listing.Offset > long.MaxValue || listing.Offset > Header.TocShift || listing.Offset > fileLen)
            return false;
        if (listing.StoredLen > PakBlobPacker.CeilingDecodedOctets)
            return false;
        return listing.StoredLen <= Header.TocShift - listing.Offset && listing.StoredLen <= fileLen - listing.Offset;
    }
}

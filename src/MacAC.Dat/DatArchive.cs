using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace MacAC.Dat;

public readonly record struct DatEntry(uint Id, uint Offset, uint Size, uint Iteration, uint Flags, uint Date);

// One .dat file. The file is memory-mapped; the directory is decoded once into a sorted table.
public sealed unsafe class DatArchive : IDisposable
{
    private readonly MemoryMappedFile? _map;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly byte* _base;
    private readonly long _length;
    private DatEntry[]? _entries;          // sorted by id; built on first use
    private readonly uint _root;
    private readonly Lock _indexLatch = new();

    // A shelf with no file behind it (an optional high-res pack that is not installed).
    private DatArchive(DatShelf shelf, DatVault? vault)
    {
        Path = ""; Shelf = shelf; Vault = vault; _entries = [];
    }

    public static DatArchive Empty(DatShelf shelf, DatVault? vault = null) => new(shelf, vault);
    public bool IsEmpty => _map is null;

    public string Path { get; }
    public DatShelf Shelf { get; }
    public DatVault? Vault { get; }
    public DatHeader Header => new(BlockSize, FileSize, DataSet, DataSubset, MasterMapId, EnginePackVersion, GamePackVersion);
    public uint BlockSize { get; }
    public uint FileSize { get; }
    public uint DataSet { get; }
    public uint DataSubset { get; }
    public uint EnginePackVersion { get; }
    public uint GamePackVersion { get; }
    public uint MasterMapId { get; }
    public ReadOnlySpan<DatEntry> Entries => Index;
    public int Count => Index.Length;

    private const int HeaderAt = 0x140;

    public DatArchive(string path, DatShelf shelf = DatShelf.Portal, DatVault? vault = null)
    {
        Path = path;
        Shelf = shelf;
        Vault = vault;
        _length = new FileInfo(path).Length;
        _map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p + _view.PointerOffset;
        var h = Span(HeaderAt, 60);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(h);
        if (magic != 0x5442) throw new DatFormatException($"{path} is not a dat file (magic 0x{magic:X})");
        BlockSize = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
        FileSize = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
        DataSet = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        DataSubset = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
        uint root = BinaryPrimitives.ReadUInt32LittleEndian(h[32..]);
        MasterMapId = BinaryPrimitives.ReadUInt32LittleEndian(h[48..]);
        EnginePackVersion = BinaryPrimitives.ReadUInt32LittleEndian(h[52..]);
        GamePackVersion = BinaryPrimitives.ReadUInt32LittleEndian(h[56..]);
        _root = root;
    }

    // The directory is only walked when someone actually looks a file up, so opening a vault
    // costs four header reads even when a run never touches, say, the cell file.
    private DatEntry[] Index
    {
        get
        {
            var built = Volatile.Read(ref _entries);
            if (built is not null) return built;
            lock (_indexLatch)
            {
                if (_entries is not null) return _entries;
                var list = new List<DatEntry>(_root == 0 ? 0 : 1 << 16);
                if (_root != 0) WalkDirectory(_root, list);
                var array = list.ToArray();
                Array.Sort(array, static (a, b) => a.Id.CompareTo(b.Id));
                Volatile.Write(ref _entries, array);
                return array;
            }
        }
    }

    public bool IsIndexed => Volatile.Read(ref _entries) is not null;

    private ReadOnlySpan<byte> Span(long at, int n)
    {
        if (at < 0 || at + n > _length) throw new DatFormatException($"{Path}: offset {at} (+{n}) is outside the file");
        return new ReadOnlySpan<byte>(_base + at, n);
    }

    // Files are chains of fixed blocks; each block starts with the offset of the next one.
    private void ReadChain(uint offset, Span<byte> dst)
    {
        int done = 0, payload = (int)BlockSize - 4;
        while (offset != 0 && done < dst.Length)
        {
            var block = Span(offset, (int)BlockSize);
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(block);
            int take = Math.Min(payload, dst.Length - done);
            block.Slice(4, take).CopyTo(dst[done..]);
            done += take;
            offset = next;
        }
        if (done < dst.Length) throw new DatFormatException($"{Path}: block chain at 0x{offset:X} ended early");
    }

    private const int NodeKids = 62, NodeSlots = 61, NodeBytes = NodeKids * 4 + 4 + NodeSlots * 24;

    private void WalkDirectory(uint nodeOffset, List<DatEntry> into)
    {
        Span<byte> raw = stackalloc byte[NodeBytes];
        ReadChain(nodeOffset, raw);
        Span<uint> kids = stackalloc uint[NodeKids];
        for (int i = 0; i < NodeKids; i++) kids[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw[(i * 4)..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(raw[(NodeKids * 4)..]);
        if (count > NodeSlots) throw new DatFormatException($"{Path}: directory node at 0x{nodeOffset:X} claims {count} entries");
        bool leaf = kids[0] == 0;
        for (int i = 0; i < count; i++)
        {
            if (!leaf) WalkDirectory(kids[i], into);
            var e = raw[(NodeKids * 4 + 4 + i * 24)..];
            into.Add(new DatEntry(
                Id: BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                Offset: BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                Size: BinaryPrimitives.ReadUInt32LittleEndian(e[12..]),
                Iteration: BinaryPrimitives.ReadUInt32LittleEndian(e[20..]),
                Flags: BinaryPrimitives.ReadUInt32LittleEndian(e),
                Date: BinaryPrimitives.ReadUInt32LittleEndian(e[16..])));
        }
        if (!leaf) WalkDirectory(kids[(int)count], into);
    }

    public bool TryLocate(uint id, out DatEntry entry)
    {
        var entries = Index;
        int lo = 0, hi = entries.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            uint v = entries[mid].Id;
            if (v == id) { entry = entries[mid]; return true; }
            if (v < id) lo = mid + 1; else hi = mid - 1;
        }
        entry = default; return false;
    }

    public bool Contains(uint id) => TryLocate(id, out _);

    // Copies the file's bytes into a rented buffer. Single-block files could be sliced from the map
    // directly, but callers keep decoded records, not raw bytes, so one copy is the simplest contract.
    public byte[] ReadFile(in DatEntry entry)
    {
        var bytes = new byte[entry.Size];
        ReadChain(entry.Offset, bytes);
        return bytes;
    }

    public bool TryReadFile(uint id, out byte[] bytes)
    {
        if (TryLocate(id, out var e)) { bytes = ReadFile(e); return true; }
        bytes = []; return false;
    }

    // Reads into a rented buffer: the caller decodes from it and returns it, so the file's
    // bytes never become garbage. Only what a record keeps is allocated.
    internal bool TryRentFile(uint id, out byte[] buffer, out int length)
    {
        if (!TryLocate(id, out var e)) { buffer = []; length = 0; return false; }
        buffer = ArrayPool<byte>.Shared.Rent((int)e.Size);
        length = (int)e.Size;
        ReadChain(e.Offset, buffer.AsSpan(0, length));
        return true;
    }

    // Fills a caller-owned buffer, growing it when needed; returns the byte count.
    public bool TryReadFile(uint id, ref byte[] buffer, out int length)
    {
        if (!TryLocate(id, out var e)) { length = 0; return false; }
        if (buffer.Length < e.Size) buffer = new byte[e.Size];
        ReadChain(e.Offset, buffer.AsSpan(0, (int)e.Size));
        length = (int)e.Size;
        return true;
    }

    public IEnumerable<uint> IdsWhere(Func<uint, bool> pick)
    {
        foreach (var e in Index) if (pick(e.Id)) yield return e.Id;
    }

    public IEnumerable<uint> IdsBetween(uint first, uint last)
    {
        var entries = Index;
        int lo = 0, hi = entries.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (entries[mid].Id < first) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < entries.Length && entries[i].Id <= last; i++) yield return entries[i].Id;
    }

    // Every id this file holds for a record type.
    public IEnumerable<uint> IdsOf<T>() where T : IDatRecord
    {
        var codec = RecordCodecs.For<T>();
        if (Shelf == DatShelf.Cell || codec.Shelf == DatShelf.Cell)
            return codec.Shelf == DatShelf.Cell && Shelf == DatShelf.Cell ? IdsWhere(i => RecordCodecs.KindOf(DatShelf.Cell, i) == codec.Kind) : [];
        if (codec.Last == 0) return [];
        return IdsBetween(codec.First, codec.Last);
    }

    public RecordKind KindOf(uint id) => RecordCodecs.KindOf(Shelf, id);

    // Null when the id is not in this file; a record that fails to decode still throws.
    public T? Fetch<T>(uint id) where T : IDatRecord => TryFetch<T>(id, out var r) ? r : default;

    public bool TryFetch<T>(uint id, [MaybeNullWhen(false)] out T record) where T : IDatRecord
    {
        if (Vault is not null) return Vault.TryFetchCached(this, id, out record);
        if (!TryReadFile(id, out var bytes)) { record = default; return false; }
        record = (T)RecordCodecs.For<T>().Decode(bytes, id, null);
        return true;
    }

    private RevisionStamp? _revision;
    private bool _revisionTried;

    public RevisionStamp? Revision
    {
        get
        {
            if (!_revisionTried)
            {
                _revisionTried = true;
                if (TryReadFile(RevisionStamp.FileId, out var bytes)) { var c = new DatCursor(bytes, RevisionStamp.FileId); _revision = RevisionStamp.Read(ref c); }
            }
            return _revision;
        }
    }

    public void Dispose()
    {
        if (_view is null) return;
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _map?.Dispose();
    }
}

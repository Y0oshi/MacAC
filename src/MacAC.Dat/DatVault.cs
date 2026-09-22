using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MacAC.Dat;

// The four data files opened together, with routing by id and a per-record cache.
public class DatVault : IDisposable
{
    public DatArchive Portal { get; }
    public DatArchive Cell { get; }
    public DatArchive Local { get; }
    public DatArchive HighRes { get; }
    public string Folder { get; }
    private readonly ConcurrentDictionary<(Type, DatShelf, uint), object> _cache = new();
    public bool CacheEnabled { get; set; } = true;

    public DatVault(string folder, string language = "English")
    {
        Folder = folder;
        Portal = new DatArchive(Path.Combine(folder, "client_portal.dat"), DatShelf.Portal, this);
        Cell = new DatArchive(Path.Combine(folder, "client_cell_1.dat"), DatShelf.Cell, this);
        Local = new DatArchive(Path.Combine(folder, $"client_local_{language}.dat"), DatShelf.Local, this);
        string hi = Path.Combine(folder, "client_highres.dat");
        HighRes = File.Exists(hi) ? new DatArchive(hi, DatShelf.HighRes, this) : DatArchive.Empty(DatShelf.HighRes, this);
    }

    public DatArchive this[DatShelf shelf] => shelf switch
    {
        DatShelf.Cell => Cell, DatShelf.Local => Local, DatShelf.HighRes => HighRes, _ => Portal,
    };

    public DatShelf ShelfOf<T>() where T : IDatRecord => RecordCodecs.For<T>().Shelf;

    // Every id of a record type across the files that can hold it.
    public IEnumerable<uint> IdsOf<T>() where T : IDatRecord => RecordCodecs.For<T>().Shelf switch
    {
        DatShelf.Cell => Cell.IdsOf<T>(),
        DatShelf.Local => Local.IdsOf<T>(),
        _ => HighRes.IsEmpty ? Portal.IdsOf<T>() : Portal.IdsOf<T>().Concat(HighRes.IdsOf<T>()).Distinct(),
    };

    // Ids below 0x01000000 are landblocks and cells; local-language tables live in their own file.
    public DatArchive ShelfFor(uint id) => (id >> 24) switch
    {
        0 => Cell,
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x29 or 0x2A or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x31 or 0x41 => LocalOrPortal(id),
        _ => Portal,
    };

    private DatArchive LocalOrPortal(uint id) => Local.Contains(id) ? Local : Portal;

    public bool TryReadBytes(uint id, out byte[] bytes)
    {
        if ((id >> 24) == 0x06 && HighRes.TryReadFile(id, out bytes)) return true;
        return ShelfFor(id).TryReadFile(id, out bytes);
    }

    public byte[] ReadBytes(uint id) => TryReadBytes(id, out var b) ? b : throw new KeyNotFoundException($"0x{id:X8} is not in the data files");

    // Null when no file holds the id; a record that fails to decode still throws.
    public T? Fetch<T>(uint id) where T : IDatRecord => TryFetch<T>(id, out var r) ? r : default;

    // Routes by the record type's shelf; portal records may be overridden by the high-res file.
    public bool TryFetch<T>(uint id, [MaybeNullWhen(false)] out T record) where T : IDatRecord
    {
        var codec = RecordCodecs.For<T>();
        switch (codec.Shelf)
        {
            case DatShelf.Cell: return Cell.TryFetch(id, out record);
            case DatShelf.Local: return Local.TryFetch(id, out record);
            default:
                if (HighRes.TryFetch(id, out record)) return true;
                return Portal.TryFetch(id, out record);
        }
    }

    internal bool TryFetchCached<T>(DatArchive archive, uint id, [MaybeNullWhen(false)] out T record) where T : IDatRecord
    {
        var key = (typeof(T), archive.Shelf, id);
        if (CacheEnabled && _cache.TryGetValue(key, out var hit)) { record = (T)hit; return true; }
        if (!archive.TryRentFile(id, out var buffer, out int length)) { record = default; return false; }
        try
        {
            record = Decode<T>(id, buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        if (CacheEnabled) _cache[key] = record;
        return true;
    }

    public T Decode<T>(uint id, ReadOnlySpan<byte> bytes) where T : IDatRecord
    {
        var codec = RecordCodecs.For<T>();
        return (T)codec.Decode(bytes, id, typeof(T) == typeof(PropertyCatalog) ? null : PropertyCatalogOrNull);
    }

    private PropertyCatalog? _catalog;
    private bool _catalogTried;

    // The property catalog is needed to decode UI layouts and property bags; loaded once on demand.
    public PropertyCatalog? PropertyCatalogOrNull
    {
        get
        {
            if (!_catalogTried)
            {
                _catalogTried = true;
                if (TryReadBytes(PropertyCatalog.DefaultId, out var bytes))
                {
                    var c = new DatCursor(bytes, PropertyCatalog.DefaultId);
                    _catalog = PropertyCatalog.Read(ref c);
                }
            }
            return _catalog;
        }
    }

    public RevisionStamp? Revision(DatShelf shelf) => this[shelf].Revision;

    public void ForgetAll() => _cache.Clear();

    public void Dispose()
    {
        Portal.Dispose(); Cell.Dispose(); Local.Dispose(); HighRes.Dispose();
    }
}

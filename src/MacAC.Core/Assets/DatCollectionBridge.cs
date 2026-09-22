using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;

namespace MacAC.Assets;

public sealed class DatCollectionBridge : IDatAccess
{
    private readonly DatVault _datFiles;
    private readonly DatDatabaseShim _gateway;
    private readonly DatDatabaseShim _chamber;
    private readonly DatDatabaseShim _hiRes;
    private readonly DatDatabaseShim _language;
    private readonly ReadOnlyDictionary<uint, IDatDatabase> _chamberZones;

    public DatCollectionBridge(DatVault datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        _datFiles = datFiles;
        _gateway = new DatDatabaseShim(datFiles.Portal);
        _chamber = new DatDatabaseShim(datFiles.Cell);
        _hiRes = new DatDatabaseShim(datFiles.HighRes);
        _language = new DatDatabaseShim(datFiles.Local);
        _chamberZones = new ReadOnlyDictionary<uint, IDatDatabase>(new Dictionary<uint, IDatDatabase> { [0u] = _chamber });
    }

    public string SrcFolder => _datFiles.Folder ?? string.Empty;

    public ShelfStats ObjectStashStats
    {
        get
        {
            return _gateway.ObjectCacheStats + _chamber.ObjectCacheStats + _hiRes.ObjectCacheStats + _language.ObjectCacheStats;
        }
    }

    public IDatDatabase Portal => _gateway;
    public IDatDatabase Cell => _chamber;
    public ReadOnlyDictionary<uint, IDatDatabase> ChamberZones => _chamberZones;
    public IDatDatabase HighRes => _hiRes;
    public IDatDatabase Language => _language;
    public IDatDatabase Local => _language;

    public ReadOnlyDictionary<uint, uint> ZoneFileLookup => new(new Dictionary<uint, uint>());

    public int GatewayIteration => _gateway.Iteration;
    public int ChamberIteration => _chamber.Iteration;
    public int HiResIteration => _hiRes.Iteration;
    public int LanguageIteration => _language.Iteration;

    [return: MaybeNull]
    public T Get<T>(uint fileIdent) where T : IDatRecord => TryGet<T>(fileIdent, out T? val) ? val : default;

    public bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord
    {
        if (typeof(T) == typeof(RevisionStamp))
        {
            throw new Exception(
                "Iteration isn't a valid type to get from a dat file collection since it is used in all dat files. Use a specific dat like datCollection.Portal.Get<Iteration>()");
        }

        switch (_datFiles.ShelfOf<T>())
        {
            case DatShelf.Cell:
                return _chamber.TryGet(fileIdent, out val);
            case DatShelf.Portal:
                // High-res holds portal-typed overrides
                return _gateway.TryGet(fileIdent, out val) || _hiRes.TryGet(fileIdent, out val);
            case DatShelf.Local:
                return _language.TryGet(fileIdent, out val);
            default:
                val = default;
                return false;
        }
    }

    public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDatRecord
    {
        return _datFiles.ShelfOf<T>() switch
        {
            DatShelf.Cell => _chamber.GetAllIdsOfType<T>(),
            DatShelf.Portal => _gateway.GetAllIdsOfType<T>().Concat(_hiRes.GetAllIdsOfType<T>()),
            DatShelf.Local => _language.GetAllIdsOfType<T>(),
            _ => [],
        };
    }

    public bool TryFetchFileOctets(uint zoneIdent, uint fileIdent, ref byte[] octets, out int octetsScan) =>
        _chamber.TryGetFileBytes(fileIdent, ref octets, out octetsScan);

    /// <summary>Every database holding the id, high-res first, then portal, language, cell.</summary>
    public IEnumerable<IDatAccess.TagResolution> LocateIdent(uint ident)
    {
        var located = new List<IDatAccess.TagResolution>();
        foreach (DatDatabaseShim shim in new[] { _hiRes, _gateway, _language, _chamber })
        {
            if (Holds(shim, ident, out RecordKind kind))
                located.Add(new IDatAccess.TagResolution(shim, kind));
        }
        return located;
    }

    public bool TryLocatePreferred(uint ident, [NotNullWhen(true)] out IDatDatabase? database, out RecordKind kind)
    {
        // Portal wins outright, then high-res, language, cell - the same preference as IDatAccess's default.
        if (Holds(_gateway, ident, out kind)) { database = _gateway; return true; }
        if (Holds(_hiRes, ident, out kind)) { database = _hiRes; return true; }
        if (Holds(_language, ident, out kind)) { database = _language; return true; }
        if (Holds(_chamber, ident, out kind)) { database = _chamber; return true; }

        database = null;
        return false;
    }

    public bool TrySave<T>(T objRef, int iteration = 0) where T : IDatRecord =>
        throw new NotSupportedException("DatCollectionBridge is read-only");

    public bool TrySave<T>(uint zoneIdent, T objRef, int iteration = 0) where T : IDatRecord =>
        throw new NotSupportedException("DatCollectionBridge is read-only");

    public void Dispose()
    {
    }

    private static bool Holds(DatDatabaseShim shim, uint ident, out RecordKind kind)
    {
        DatArchive raw = shim.RawDatabase;
        if (raw.Contains(ident))
        {
            kind = raw.KindOf(ident);
            if (kind != RecordKind.Unknown)
                return true;
        }
        kind = RecordKind.Unknown;
        return false;
    }
}

public sealed class DatDatabaseShim(DatArchive db) : IDatDatabase
{
    private readonly DatArchive _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly DatObjectShelf _shelf = new();
    private readonly Lock _latch = new();
    private int _malformedReported;

    internal DatArchive RawDatabase => _db;

    public DatArchive Db => _db;

    public int Iteration => _db.Revision?.Current ?? 0;

    public ShelfStats ObjectCacheStats => _shelf.Stats;

    public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDatRecord => _db.IdsOf<T>();

    public bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord
    {
        if (_shelf.TryGet(fileIdent, out val))
            return true;

        bool presentButUnreadable;
        lock (_latch)
        {
            if (_shelf.TryGet(fileIdent, out val))
                return true;
            if (_db.TryFetch<T>(fileIdent, out val))
            {
                val = _shelf.FetchOrAppend(fileIdent, val);
                return true;
            }
            presentButUnreadable = _db.Contains(fileIdent);
        }

        if (presentButUnreadable && Interlocked.Exchange(ref _malformedReported, 1) is 0)
        {
            Console.Error.WriteLine(
                $"[dat-miss] {typeof(T).Name} 0x{fileIdent:X8} entry EXISTS but TryGet failed " +
                $"(thread={System.Environment.CurrentManagedThreadId}; further reports suppressed)");
        }

        return false;
    }

    public bool TryGetFileBytes(uint fileIdent, [MaybeNullWhen(false)] out byte[] val)
    {
        lock (_latch)
            return _db.TryReadFile(fileIdent, out val);
    }

    public bool TryGetFileBytes(uint fileIdent, ref byte[] octets, out int octetsScan)
    {
        lock (_latch)
            return _db.TryReadFile(fileIdent, ref octets, out octetsScan);
    }

    public bool TryPersist<T>(T objRef, int iteration = 0) where T : IDatRecord =>
        throw new NotSupportedException("DatDatabaseShim is read-only");

    public void Dispose()
    {
        // The underlying DatDatabase is owned by DatCollection - do not dispose here.
    }
}

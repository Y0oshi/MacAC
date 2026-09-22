using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;

namespace MacAC.Assets;

public static class LiveDatCollectionFactory
{
    public static IDatAccess OpenScanSole(string datFolder) =>
        new LiveDatCollection(new DatVault(CheckedFolder(datFolder)) { CacheEnabled = false });

    // The library keeps nothing itself: the shim shelves decoded records.
    public static string CheckedFolder(string datFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datFolder);
        return datFolder;
    }
}

public sealed class LiveDatCollection : IDatAccess
{
    private readonly DatVault _raw;
    private readonly DatCollectionBridge _bridge;
    private bool _bridgeClosed;
    private bool _rawClosed;

    internal LiveDatCollection(DatVault raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _raw = raw;
        _bridge = new DatCollectionBridge(raw);
    }

    public string SrcFolder => _bridge.SrcFolder;

    public ShelfStats ObjectStashStats => _bridge.ObjectStashStats;

    public IDatDatabase Portal => _bridge.Portal;
    public IDatDatabase Cell => _bridge.Cell;
    public ReadOnlyDictionary<uint, IDatDatabase> ChamberZones => _bridge.ChamberZones;
    public IDatDatabase HighRes => _bridge.HighRes;
    public IDatDatabase Language => _bridge.Language;
    public IDatDatabase Local => _bridge.Local;
    public ReadOnlyDictionary<uint, uint> ZoneFileLookup => _bridge.ZoneFileLookup;
    public int GatewayIteration => _bridge.GatewayIteration;
    public int ChamberIteration => _bridge.ChamberIteration;
    public int HiResIteration => _bridge.HiResIteration;
    public int LanguageIteration => _bridge.LanguageIteration;

    [return: MaybeNull]
    public T Get<T>(uint fileIdent) where T : IDatRecord => _bridge.Get<T>(fileIdent);

    public bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord => _bridge.TryGet(fileIdent, out val);

    public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDatRecord => _bridge.GetAllIdsOfType<T>();

    public bool TryFetchFileOctets(uint zoneIdent, uint fileIdent, ref byte[] octets, out int octetsScan) =>
        _bridge.TryFetchFileOctets(zoneIdent, fileIdent, ref octets, out octetsScan);

    public IEnumerable<IDatAccess.TagResolution> LocateIdent(uint ident) => _bridge.LocateIdent(ident);

    public bool TrySave<T>(T objRef, int iteration = 0) where T : IDatRecord => _bridge.TrySave(objRef, iteration);

    public bool TrySave<T>(uint zoneIdent, T objRef, int iteration = 0) where T : IDatRecord => _bridge.TrySave(zoneIdent, objRef, iteration);

    public void Dispose()
    {
        if (!_bridgeClosed)
        {
            _bridge.Dispose();
            _bridgeClosed = true;
        }
        if (!_rawClosed)
        {
            _raw.Dispose();
            _rawClosed = true;
        }
    }
}

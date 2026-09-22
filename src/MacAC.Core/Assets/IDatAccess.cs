using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Assets;

/// <summary>The whole installed dat set: the individual databases plus cross-database lookups.</summary>
public interface IDatAccess : IDisposable, IDatRecordSource
{
    /// <summary>Gets the source directory of the DAT files.</summary>
    string SrcFolder { get; }

    /// <summary>Tries to get the raw bytes of a file from a specific region database.</summary>
    bool TryFetchFileOctets(uint zoneIdent, uint fileIdent, ref byte[] octets, out int octetsScan);

    /// <summary>The portal database.</summary>
    IDatDatabase Portal { get; }

    IDatDatabase Cell { get; }

    ReadOnlyDictionary<uint, IDatDatabase> ChamberZones { get; }

    /// <summary>The high res database.</summary>
    IDatDatabase HighRes { get; }

    /// <summary>The language database.</summary>
    IDatDatabase Language { get; }

    IDatDatabase Local { get; }

    IEnumerable<uint> GetAllIdsOfType<T>() where T : IDatRecord;

    ReadOnlyDictionary<uint, uint> ZoneFileLookup { get; }

    int GatewayIteration { get; }

    int ChamberIteration { get; }

    int HiResIteration { get; }

    int LanguageIteration { get; }

    bool TrySave<T>(T objRef, int iteration = 0) where T : IDatRecord;

    bool TrySave<T>(uint zoneIdent, T objRef, int iteration = 0) where T : IDatRecord;

    /// <summary>Resolution of a data ID to a database and type.</summary>
    public record TagResolution(IDatDatabase Database, RecordKind Type);

    /// <summary>Resolves a data ID to all possible databases and types.</summary>
    public IEnumerable<TagResolution> LocateIdent(uint ident);

    bool TryLocatePreferred(uint ident, [NotNullWhen(true)] out IDatDatabase? database, out RecordKind kind)
    {
        TagResolution? hiRes = null;
        TagResolution? language = null;
        TagResolution? chamber = null;

        foreach (TagResolution resolution in LocateIdent(ident))
        {
            var db = resolution.Database;
            if (ReferenceEquals(db, Portal))
            {
                database = db;
                kind = resolution.Type;
                return true;
            }
            if (ReferenceEquals(db, HighRes)) hiRes = resolution;
            else if (ReferenceEquals(db, Language)) language = resolution;
            else if (ReferenceEquals(db, Cell)) chamber = resolution;
        }

        TagResolution? finest = hiRes ?? language ?? chamber;
        database = finest?.Database;
        kind = finest?.Type ?? RecordKind.Unknown;
        return database is not null;
    }
}

/// <summary>One dat database: typed object reads, raw file reads, and saves.</summary>
public interface IDatDatabase : IDisposable
{
    DatArchive Db { get; }

    int Iteration { get; }

    public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDatRecord;

    public bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord;

    bool TryGetFileBytes(uint fileIdent, [MaybeNullWhen(false)] out byte[] val);

    bool TryGetFileBytes(uint fileIdent, ref byte[] octets, out int octetsScan);

    bool TryPersist<T>(T objRef, int iteration = 0) where T : IDatRecord;
}

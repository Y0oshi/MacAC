using System.Collections.Frozen;
using System.Reflection;

namespace MacAC.Dat;

// Any decoded record: the marker generic code constrains on.
public interface IDatRecord
{
    uint Id { get; }
}

// A record that knows how to decode itself, which file it lives in and which ids it owns.
public interface IDatRecord<TSelf> : IDatRecord where TSelf : IDatRecord<TSelf>
{
    static abstract TSelf Read(ref DatCursor c);
    // Cell records live in their own file and their ids overlap portal ids, so the record type picks the shelf.
    static virtual DatShelf Shelf => DatShelf.Portal;
    static virtual RecordKind Kind => RecordKind.Unknown;
    static virtual (uint First, uint Last) IdRange => (0, 0);
}

public enum DatShelf { Portal, Cell, Local, HighRes }

// Decodes one record from the bytes of its file.
public delegate IDatRecord DecodeRecord(ReadOnlySpan<byte> bytes, uint id, PropertyCatalog? catalog);

// What a record type needs at runtime without static generic dispatch: its decoder, shelf, kind and id span.
public class RecordCodec
{
    public required Type Type { get; set; }
    public required DatShelf Shelf { get; set; }
    public required RecordKind Kind { get; set; }
    public required uint First { get; set; }
    public required uint Last { get; set; }
    public required DecodeRecord Decode { get; set; }

    public bool Owns(uint id) => Last != 0 && id >= First && id <= Last;
    public bool IsSingular => First == Last && First != 0;
}

public static class RecordCodecs
{
    // When set, a record that does not consume its file (beyond dword padding) is rejected.
    public static bool StrictLengths { get; set; } = Environment.GetEnvironmentVariable("MACAC_DAT_STRICT") == "1";

    static readonly FrozenDictionary<Type, RecordCodec> ByType = Build();
    public static IReadOnlyCollection<RecordCodec> All => ByType.Values;

    static FrozenDictionary<Type, RecordCodec> Build()
    {
        var d = new Dictionary<Type, RecordCodec>();
        var describe = typeof(RecordCodecs).GetMethod(nameof(Describe), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var t in typeof(RecordCodecs).Assembly.GetTypes())
        {
            if (t.IsAbstract || !t.IsClass) continue;
            var iface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDatRecord<>));
            if (iface is null) continue;
            d[t] = (RecordCodec)describe.MakeGenericMethod(t).Invoke(null, null)!;
        }
        return d.ToFrozenDictionary();
    }

    static RecordCodec Describe<T>() where T : class, IDatRecord<T>
    {
        var (first, last) = T.IdRange;
        return new RecordCodec
        {
            Type = typeof(T), Shelf = T.Shelf, Kind = T.Kind, First = first, Last = last,
            Decode = static (bytes, id, catalog) =>
            {
                var c = new DatCursor(bytes, id) { Catalog = catalog };
                var r = T.Read(ref c);
                if (StrictLengths && c.Remaining > 3) throw new DatFormatException($"{typeof(T).Name} 0x{id:X8} left {c.Remaining} of {bytes.Length} bytes unread");
                return r;
            },
        };
    }

    public static RecordCodec For(Type t) => ByType.TryGetValue(t, out var c) ? c : throw new ArgumentException($"{t.Name} is not a dat record type");
    public static RecordCodec For<T>() where T : IDatRecord => For(typeof(T));
    public static bool TryFor(Type t, out RecordCodec codec) => ByType.TryGetValue(t, out codec!);

    // The record kind an id denotes in a given file. Cell ids are structured by their low word; the others by range.
    public static RecordKind KindOf(DatShelf shelf, uint id)
    {
        if (id == RevisionStamp.FileId) return RecordKind.Iteration;
        if (shelf == DatShelf.Cell)
        {
            uint low = id & 0xFFFF;
            return low == 0xFFFF ? RecordKind.LandBlock : low == 0xFFFE ? RecordKind.LandBlockInfo : low >= 0x0100 ? RecordKind.EnvCell : RecordKind.Unknown;
        }
        var want = shelf == DatShelf.HighRes ? DatShelf.Portal : shelf;
        foreach (var c in ByType.Values)
            if (c.Shelf == want && c.Owns(id)) return c.Kind;
        return RecordKind.Unknown;
    }
}

// The fixed header at the front of every dat file.
public sealed record DatHeader(uint BlockSize, uint FileSize, uint DataSet, uint DataSubset, uint MasterMapId, uint EnginePackVersion, uint GamePackVersion);

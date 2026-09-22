using System.Collections.Concurrent;
using MacAC.Dat;
using MacAC.Mechanics.Effects;
using DatPhysicsScript =  MacAC.Dat.EffectScript;

namespace MacAC.Assets.Vfx;

public sealed class CanonKineticScriptLoader
{
    private readonly Func<uint, byte[]?> _rawOctets;
    private readonly DatArchive? _database;
    private readonly ConcurrentDictionary<uint, Lazy<DatPhysicsScript?>> _decoded = new();

    public CanonKineticScriptLoader(IDatAccess dats)
        : this((dats ?? throw new ArgumentNullException(nameof(dats))).Portal)
    {
    }

    public CanonKineticScriptLoader(IDatDatabase gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _database = gateway.Db;
        _rawOctets = ident => gateway.TryGetFileBytes(ident, out byte[]? octets) ? octets : null;
    }

    public DatPhysicsScript? PullKineticsProgram(uint ident)
    {
        if (!KineticScriptTableLookup.IsKineticsProgramDid(ident))
            return null;

        return _decoded.GetOrAdd(
            ident,
            tag => new Lazy<DatPhysicsScript?>(() => DecodeFromDat(tag), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Decodes one PhysicsScript record, orders its steps by start time and insists the whole file was consumed.
    public static DatPhysicsScript Interpret(ReadOnlyMemory<byte> octets, DatArchive? database = null)
    {
        var span = octets.Span;
        uint ident = span.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span) : 0u;
        var cursor = new DatCursor(span, ident);
        DatPhysicsScript program = DatPhysicsScript.Read(ref cursor);
        if (!cursor.AtEnd)
            throw new InvalidDataException($"{nameof(DatPhysicsScript)} 0x{program.Id:X8} consumed {cursor.Offset} of {span.Length} bytes.");
        for (int idx = 0; idx < program.Steps.Count; idx++)
        {
            if (!double.IsFinite(program.Steps[idx].StartTime))
                throw new InvalidDataException($"PhysicsScript 0x{program.Id:X8} hook {idx} has non-finite StartTime {program.Steps[idx].StartTime}.");
        }
        program.Steps.Sort(static (left, right) => left.StartTime.CompareTo(right.StartTime));
        return program;
    }

    private DatPhysicsScript? DecodeFromDat(uint ident)
    {
        byte[]? octets = _rawOctets(ident);
        if (octets is null)
            return null;

        EffectScript program = Interpret(octets, _database);
        if (program.Id != ident)
            throw new InvalidDataException($"KineticsProgram entry 0x{ident:X8} contained id 0x{program.Id:X8}.");
        return program;
    }
}

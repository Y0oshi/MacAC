using System.Collections.Concurrent;
using MacAC.Assets.Pak;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using DatEnvironment = MacAC.Dat.InteriorShell;

namespace MacAC.Assets;

/// <summary>
/// Packed collision built straight from the installed dats, with no pak in between. The payloads
/// are the ones <c>macac-bake</c> would have written: the same builders run, they just hand back
/// the object instead of a serialized blob.
///
/// Rooms sharing a shell share its structure: a dungeon of five hundred rooms cut from a dozen
/// cell shapes flattens a dozen structures, not five hundred.
/// </summary>
public sealed class DatBakedContactSource : IBakedContactSource
{
    private const uint SurroundingsDidStem = 0x0D00_0000u;

    private readonly IDatAccess _datFiles;
    private readonly ConcurrentDictionary<(uint Environment, ushort Cell), PackedCellStructContactAsset?> _shells = new();
    private long _sensors;
    private long _reads;
    private long _fetched;
    private long _absent;

    public DatBakedContactSource(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        _datFiles = datFiles;
    }

    /// <summary>Distinct cell shapes flattened so far.</summary>
    public int ShellsFlattened => _shells.Count;

    public BakedContactSourceStats ImpactStats => new(
        Volatile.Read(ref _sensors),
        Volatile.Read(ref _reads),
        Volatile.Read(ref _fetched),
        Volatile.Read(ref _absent),
        0);

    public BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent)
    {
        BakedContactTypeContract.Validate(kind);
        Interlocked.Increment(ref _sensors);

        RecordKind wanted = kind switch
        {
            PakAssetKind.GfxObjCollision => RecordKind.GfxObj,
            PakAssetKind.SetupCollision => RecordKind.Setup,
            _ => RecordKind.EnvCell,
        };
        bool present = _datFiles.TryLocatePreferred(srcFileIdent, out _, out RecordKind actual) && actual == wanted;
        return present ? BakedAssetPresence.Available : BakedAssetPresence.Missing;
    }

    public BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Build(abortTicket, () =>
            _datFiles.Portal.TryGet<PartMesh>(srcFileIdent, out PartMesh? mesh) && mesh is not null
                ? PackedContactAssetBuilder.FlattenGfxObj(mesh)
                : null);
    }

    public BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Build(abortTicket, () =>
            _datFiles.Portal.TryGet<RigSpec>(srcFileIdent, out RigSpec? rig) && rig is not null
                ? PackedContactAssetBuilder.FlattenSetup(rig)
                : null);
    }

    public BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Build(abortTicket, () => Room(srcFileIdent) is { } room ? Shell(room) : null);
    }

    public BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Build(abortTicket, () =>
        {
            // The wiring names portal polygons by index into the shell's table, so the shell has
            // to be flattened first; it is normally already interned by the structure read.
            if (Room(srcFileIdent) is not { } room || Shell(room) is not { } shell)
                return null;
            return PackedContactAssetBuilder.FlattenEnvCellTopology(srcFileIdent, room, shell.PortalPolygons);
        });
    }

    public void Dispose()
    {
        _shells.Clear();
    }

    private RoomCell? Room(uint fileIdent) =>
        _datFiles.Cell.TryGet<RoomCell>(fileIdent, out RoomCell? room) ? room : null;

    private PackedCellStructContactAsset? Shell(RoomCell room)
    {
        return _shells.GetOrAdd((room.ShellId, room.ShellCellIndex), static (key, dats) =>
        {
            uint environIdent = SurroundingsDidStem | key.Environment;
            return dats.Portal.TryGet<DatEnvironment>(environIdent, out DatEnvironment? surroundings)
                && surroundings is not null
                && surroundings.Cells.TryGetValue(key.Cell, out ShellCell? shape)
                && shape is not null
                ? PackedContactAssetBuilder.FlattenChamberStructure(shape)
                : null;
        }, _datFiles);
    }

    private BakedContactRead<T> Build<T>(CancellationToken abortTicket, Func<T?> flatten) where T : class
    {
        abortTicket.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);

        T? built = flatten();
        abortTicket.ThrowIfCancellationRequested();
        if (built is null)
        {
            Interlocked.Increment(ref _absent);
            return BakedContactRead<T>.Missing;
        }

        Interlocked.Increment(ref _fetched);
        return BakedContactRead<T>.Loaded(built);
    }
}

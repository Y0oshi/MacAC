using System.Collections.Concurrent;
using System.Numerics;
using MacAC.Assets.Vfx;
using BCnEncoder.Decoder;
using MacAC.Dat;
using Microsoft.Extensions.Logging;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Assets;

public sealed partial class MeshHarvester
{
    private const ulong ChamberGeoBit = 0x1_0000_0000UL;

    private readonly IDatAccess _datFiles;
    private readonly ILogger _logger;
    private readonly CanonKineticScriptLoader _programs;
    private readonly Action<HarvestedMesh>? _flankTap;
    private readonly TexturePixelShelf _px = new(ceilingOctets: 64L * 1024 * 1024, ceilingListings: 128);
    private readonly ThreadLocal<BcDecoder> _bc = new(static () => new BcDecoder());
    private readonly ConcurrentDictionary<uint, byte[]> _solids = new();

    public MeshHarvester(IDatAccess datFiles, ILogger logger, Action<HarvestedMesh>? flankLinedDrain)
    {
        _datFiles = datFiles;
        _logger = logger;
        _flankTap = flankLinedDrain;
        _programs = new CanonKineticScriptLoader(datFiles.Portal);
    }

    public ShelfStats DecodedTextureCacheStats => _px.Stats;

    public HarvestedMesh? Harvest(ulong ident, bool isRig, CancellationToken token = default)
    {
        try
        {
            uint datIdent = (uint)(ident & 0xFFFFFFFFu);
            if (!_datFiles.TryLocatePreferred(datIdent, out IDatDatabase? database, out RecordKind kind))
                return null;

            switch (kind)
            {
                case RecordKind.Setup:
                    return database.TryGet<RigSpec>(datIdent, out var rig) ? HarvestRig(ident, rig, token) : null;

                case RecordKind.GfxObj:
                    return database.TryGet<PartMesh>(datIdent, out var gfx) ? HarvestGfxObjRef(ident, gfx, Vector3.One, token) : null;

                case RecordKind.EnvCell:
                    {
                        if (!database.TryGet<RoomCell>(datIdent, out var chamber))
                            return null;
                        if ((ident & ChamberGeoBit) is 0)
                            return HarvestEnvironChamber(ident, chamber, token);
                        return StructureOf(chamber) is { } structure
                            ? HarvestChamberStruct(ident, structure, chamber.SkinIds, Matrix4x4.Identity, token)
                            : null;
                    }

                case RecordKind.Environment:
                    {
                        if (!database.TryGet<DatEnvironment>(datIdent, out var surroundings) || surroundings.Cells.Count is 0)
                            return null;
                        return HarvestOutlines(ident, surroundings.Cells, Matrix4x4.Identity, token);
                    }

                default:
                    return null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, "Error preparing mesh data for 0x{Id:X16}", ident);
            return null;
        }
    }

    /// <summary>Axis-aligned extent of a GfxObj's vertices after <paramref name="scaling"/>.</summary>
    public (Vector3 Min, Vector3 Max) ReachOf(PartMesh gfxObjRef, Vector3 scaling)
    {
        var lower = new Vector3(float.MaxValue);
        var upper = new Vector3(float.MinValue);
        foreach (MeshVertex vert in gfxObjRef.Vertices.ByIndex.Values)
        {
            Vector3 p = vert.Position * scaling;
            lower = Vector3.Min(lower, p);
            upper = Vector3.Max(upper, p);
        }
        return (lower, upper);
    }

    // The CellStruct an EnvCell instantiates, via its Environment record
    private ShellCell? StructureOf(RoomCell chamber)
    {
        uint environIdent = 0x0D000000u | chamber.ShellId;
        return _datFiles.Portal.TryGet<DatEnvironment>(environIdent, out var surroundings)
            && surroundings.Cells.TryGetValue(chamber.ShellCellIndex, out ShellCell? structure)
            ? structure
            : null;
    }

    // A running min/max box that starts empty
    private struct Extent
    {
        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);
        public bool Any;

        public Extent()
        {
        }

        public void Shield(Vector3 pt)
        {
            Min = Vector3.Min(Min, pt);
            Max = Vector3.Max(Max, pt);
        }

        // Absorbs another box's corners as-is (an empty box is absorbed without shrinking anything)
        public void Merge(Vector3 lower, Vector3 upper)
        {
            Min = Vector3.Min(Min, lower);
            Max = Vector3.Max(Max, upper);
            Any = true;
        }

        public readonly Bounds3 Box => Any ? new Bounds3(Min, Max) : default;
    }

    private static Matrix4x4 Posture(Pose cycle)
    {
        return Matrix4x4.CreateFromQuaternion(cycle.Orientation) * Matrix4x4.CreateTranslation(cycle.Origin);
    }
}

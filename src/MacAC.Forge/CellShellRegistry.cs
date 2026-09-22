using MacAC.Mechanics.Drawing.Batches;

namespace MacAC.Forge;

public sealed record CellShellSeed(
    uint FileId,
    uint EnvironmentId,
    ushort CellStructure,
    IReadOnlyList<ushort> Surfaces);

public sealed record CellShellFamily(
    ulong GeometryId,
    uint EnvironmentId,
    ushort CellStructure,
    ushort[] Surfaces,
    uint[] FileIds);

/// <summary>EnvCells grouped by shell geometry, in primary-file-id order.</summary>
public sealed class CellShellRegistry
{
    private CellShellRegistry(IReadOnlyList<CellShellFamily> clans, int chamberTally)
    {
        Groups = clans;
        ChamberTally = chamberTally;
    }

    public IReadOnlyList<CellShellFamily> Groups { get; }
    public int ChamberTally { get; }
    public int UniqueGeoTally => Groups.Count;
    public int AliasTally => ChamberTally - UniqueGeoTally;

    public static CellShellRegistry Build(IEnumerable<CellShellSeed> seeds) =>
        Build(seeds, EnvCellGeometryKey.Compute);

    public static CellShellRegistry Build(
        IEnumerable<CellShellSeed> seeds,
        Func<uint, ushort, IReadOnlyList<ushort>, ulong> persona)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(persona);

        CellShellAssembler assembler = new CellShellAssembler(persona);
        foreach (CellShellSeed seed in seeds)
            assembler.Add(seed);
        return assembler.Build();
    }

    internal static CellShellRegistry Of(IReadOnlyList<CellShellFamily> clans, int chamberTally) =>
        new(clans, chamberTally);
}

/// <summary>Accumulates seeds one at a time and seals them into a <see cref="CellShellRegistry"/>.</summary>
public sealed class CellShellAssembler
{
    private const string Sealed = "the EnvCell bake catalog is already complete";

    private readonly Func<uint, ushort, IReadOnlyList<ushort>, ulong> _identity;
    private readonly Dictionary<ulong, Draft> _drafts = new();
    private readonly HashSet<uint> _observed = new();
    private int _chambers;
    private bool _sealed;

    public CellShellAssembler()
        : this(EnvCellGeometryKey.Compute)
    {
    }

    public CellShellAssembler(Func<uint, ushort, IReadOnlyList<ushort>, ulong> persona)
    {
        ArgumentNullException.ThrowIfNull(persona);
        _identity = persona;
    }

    public void Add(CellShellSeed seed)
    {
        if (_sealed)
            throw new InvalidOperationException(Sealed);
        if (!_observed.Add(seed.FileId))
            throw new InvalidDataException($"duplicate EnvCell file id 0x{seed.FileId:X8}");

        ulong geoIdent = _identity(seed.EnvironmentId, seed.CellStructure, seed.Surfaces);
        if (_drafts.TryGetValue(geoIdent, out Draft? draft))
        {
            if (!draft.SameForm(seed))
            {
                throw new InvalidDataException(
                    $"EnvCell geometry identity collision 0x{geoIdent:X16}: "
                    + $"cell 0x{draft.FileIdents[0]:X8} and cell 0x{seed.FileId:X8} "
                    + "have different environment/cell-structure/surface tuples");
            }

            draft.FileIdents.Add(seed.FileId);
        }
        else
        {
            _drafts.Add(geoIdent, new Draft(geoIdent, seed));
        }

        ++_chambers;
    }

    public CellShellRegistry Build()
    {
        if (_sealed)
            throw new InvalidOperationException(Sealed);
        _sealed = true;

        var clans = _drafts.Values
            .Select(static draft => draft.Close())
            .OrderBy(static clan => clan.FileIds[0])
            .ToArray();
        return CellShellRegistry.Of(clans, _chambers);
    }

    private sealed class Draft(ulong geoIdent, CellShellSeed lead)
    {
        private readonly uint _surroundingsIdent = lead.EnvironmentId;
        private readonly ushort _chamberStructure = lead.CellStructure;
        private readonly ushort[] _canvases = lead.Surfaces.ToArray();

        public List<uint> FileIdents { get; } = [lead.FileId];

        public bool SameForm(CellShellSeed seed)
        {
            if (_surroundingsIdent != seed.EnvironmentId
                || _chamberStructure != seed.CellStructure
                || _canvases.Length != seed.Surfaces.Count)

                return false;

            for (int idx = 0; idx < _canvases.Length; ++idx)
            {
                if (_canvases[idx] != seed.Surfaces[idx])
                    return false;
            }

            return true;
        }

        public CellShellFamily Close()
        {
            FileIdents.Sort();
            return new CellShellFamily(geoIdent, _surroundingsIdent, _chamberStructure, _canvases, FileIdents.ToArray());
        }
    }
}

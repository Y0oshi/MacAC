using System.Collections.Concurrent;
using System.Numerics;
using UcgCellGraph = MacAC.Mechanics.Realm.Cells.ChamberGraph;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticAssetCache
{
    private const uint StemBitmask = 0xFFFF0000u;

    private readonly bool _readiedSole;
    private readonly ContactWorldStateSlot _world;
    private readonly ConcurrentDictionary<uint, GfxObjKinetics> _gfxObjs = new();
    private readonly ConcurrentDictionary<uint, GfxObjVisualExtent> _extents = new();
    private readonly ConcurrentDictionary<uint, SetupKinetics> _setups = new();
    private readonly ConcurrentDictionary<uint, PackedGfxObjContactAsset> _denseGfxObjs = new();
    private readonly ConcurrentDictionary<uint, PackedSetupContact> _denseSetups = new();
    private KineticAssetCache? _scanThrough;

    public KineticAssetCache() : this(readiedSole: false)
    {
    }

    private KineticAssetCache(bool readiedSole) : this(readiedSole, new ContactWorldStateSlot())
    {
    }

    private KineticAssetCache(bool readiedSole, ContactWorldStateSlot world)
    {
        _readiedSole = readiedSole;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ChamberGraph = new UcgCellGraph(_world);
        if (!readiedSole && KineticTelemetry.CollisionShadowSampleEvery > 0)
            ImpactShade = new ProxyShadowVerifier(KineticTelemetry.CollisionShadowSampleEvery, KineticTelemetry.ImpactShadeArtifactFolder);
    }

    public static KineticAssetCache CreateProduction() =>
        new(readiedSole: true) { LinkStrollManner = ContactWalkMode.Flat };

    public GfxObjKinetics? FetchGfxObjRef(uint ident) => Find(_gfxObjs, ident) ?? _scanThrough?.FetchGfxObjRef(ident);

    public SetupKinetics? FetchRig(uint ident) => Find(_setups, ident) ?? _scanThrough?.FetchRig(ident);

    private ContactWorldState World => _world.Current;
    private ConcurrentDictionary<uint, CellKinetics> Cells => World.CellStruct;
    private ConcurrentDictionary<uint, PackedCellStructContactAsset> DenseChambers => World.PlanarChamberStruct;
    private ConcurrentDictionary<uint, PackedEnvCellTopology> DenseEnvironChambers => World.PlanarEnvironChamber;
    private ConcurrentDictionary<uint, BuildingKinetics> Buildings => World.Buildings;

    internal ProxyShadowVerifier? ImpactShade { get; set; }

    public ContactProxyStats LinkProxyStats => ImpactShade?.Stats ?? default;

    internal ContactWalkMode LinkStrollManner { get; set; } = ContactWalkMode.Graph;

    public UcgCellGraph ChamberGraph { get; }

    internal ContactWorldStateSlot ImpactRealm => _world;

    public CellKinetics? FetchChamberStruct(uint ident) => Find(Cells, ident);

    public GfxObjVisualExtent? FetchVisualLimits(uint gfxObjRefIdent)
    {
        return Find(_extents, gfxObjRefIdent) ?? _scanThrough?.FetchVisualLimits(gfxObjRefIdent);
    }

    public PackedGfxObjContactAsset? FetchPlanarGfxObjRef(uint ident) => Find(_denseGfxObjs, ident) ?? _scanThrough?.FetchPlanarGfxObjRef(ident);

    public PackedSetupContact? FetchPlanarRig(uint ident) => Find(_denseSetups, ident) ?? _scanThrough?.FetchPlanarRig(ident);

    public PackedCellStructContactAsset? FetchPlanarChamberStruct(uint ident) => Find(DenseChambers, ident);

    public PackedEnvCellTopology? FetchPlanarEnvironChamber(uint ident) => Find(DenseEnvironChambers, ident);

    public BuildingKinetics? GetBuilding(uint landcellIdent) => Find(Buildings, landcellIdent);

    public void EnrollGfxObjRefForTest(uint gfxObjRefIdent, GfxObjKinetics kinetics) => _gfxObjs[gfxObjRefIdent] = kinetics;

    public void EnrollChamberStructForTest(uint environChamberIdent, CellKinetics kinetics) => World.AssignChamberStruct(environChamberIdent, kinetics);

    public void EnrollStructureForTest(uint landcellIdent, BuildingKinetics kinetics) => World.AssignStructure(landcellIdent, kinetics);

    public int GfxObjRefTally => _gfxObjs.Count;
    public int RigTally => _setups.Count;
    public int ChamberStructTally => Cells.Count;
    public int PlanarGfxObjRefTally => _denseGfxObjs.Count;
    public int PlanarRigTally => _denseSetups.Count;
    public int PlanarChamberStructTally => DenseChambers.Count;
    public int PlanarEnvironChamberTally => DenseEnvironChambers.Count;
    public int GraphGfxObjRefTally => Count(_gfxObjs.Values, static kinetics => kinetics.HoldsGraph);
    public int GraphRigTally => Count(_setups.Values, static kinetics => kinetics.HoldsGraph);
    public int GraphChamberStructTally => Count(Cells.Values, static kinetics => kinetics.HoldsGraph);

    public void StashStructure(uint landcellIdent, IReadOnlyList<BuildingPortalFacts> gateways, Matrix4x4 realmXform, uint modelIdent = 0u)
    {
        if (Buildings.ContainsKey(landcellIdent))
            return;

        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);
        World.AssignStructure(landcellIdent, new BuildingKinetics
        {
            WorldTransform = realmXform,
            InverseWorldTransform = inv,
            Portals = gateways,
            ModelId = modelIdent,
        });
    }

    public IReadOnlyCollection<uint> ChamberStructIdents => (IReadOnlyCollection<uint>)Cells.Keys;

    public IReadOnlyCollection<uint> StructureIdents => (IReadOnlyCollection<uint>)Buildings.Keys;

    public void DropStructuresForLb(uint lbIdent)
    {
        var realm = World;
        RetireStem(realm.StructureTags, lbIdent & StemBitmask, realm.DropStructure);
    }

    public void DropChambersForLb(uint lbIdent)
    {
        uint stem = lbIdent & StemBitmask;
        var realm = World;
        RetireStem(realm.ChamberStructTags, stem, realm.DropChamberStruct);
        RetireStem(realm.PlanarChamberStructTags, stem, realm.DropPlanarChamberStruct);
        RetireStem(realm.PlanarEnvironChamberTags, stem, realm.DropPlanarEnvironChamber);
    }

    internal static KineticAssetCache CreateProduction(ContactWorldStateSlot impactRealm)
    {
        return new(readiedSole: true, impactRealm) { LinkStrollManner = ContactWalkMode.Flat };
    }

    internal KineticAssetCache BuildVacantImpactLoading(ContactWorldStateSlot impactRealm)
    {
        return new(_readiedSole, impactRealm) { LinkStrollManner = LinkStrollManner, _scanThrough = this };
    }

    private static T? Find<T>(ConcurrentDictionary<uint, T> lookup, uint ident) where T : class =>
        lookup.TryGetValue(ident, out T? val) ? val : null;

    private static int Count<T>(ICollection<T> vals, Func<T, bool> keep)
    {
        int tally = 0;
        foreach (T val in vals)
        {
            if (keep(val))
                ++tally;
        }
        return tally;
    }

    // Removes every key the ledger lists for a landblock; slots are walked by index because removal
    // edits the list
    private static void RetireStem(PrefixIndex register, uint stem, Func<uint, bool> drop)
    {
        List<uint>? sockets = register.SocketsForStem(stem);
        if (sockets is null)
            return;

        int threshold = sockets.Count;
        for (int idx = 0; idx < threshold; ++idx)
        {
            uint tag = sockets[idx];
            if (tag is not 0u)
                drop(tag);
        }
    }

    private static InvalidOperationException AbsentReadiedImpact(string sort, uint srcIdent)
    {
        return new($"Production {sort} 0x{srcIdent:X8} has no prepared collision asset. Gameplay must not extract or fall back to a parsed DAT graph.");
    }
}

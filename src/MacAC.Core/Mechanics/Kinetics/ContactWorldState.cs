using System.Collections.Concurrent;
using MacAC.Mechanics.Realm.Cells;

namespace MacAC.Mechanics.Kinetics;

internal sealed class PrefixIndex
{
    private const uint StemBitmask = 0xFFFF0000u;

    private sealed class Group
    {
        public readonly List<uint> Slots = [];
        public readonly Dictionary<uint, int> SocketOf = new();
        public readonly Stack<int> Free = new();
    }

    private readonly Dictionary<uint, Group> _clusters = new();

    internal void Add(uint tag)
    {
        uint stem = tag & StemBitmask;
        if (!_clusters.TryGetValue(stem, out Group? cluster))
            _clusters[stem] = cluster = new Group();
        if (cluster.SocketOf.ContainsKey(tag))
            return;

        if (cluster.Free.TryPop(out int reused))
        {
            cluster.Slots[reused] = tag;
            cluster.SocketOf[tag] = reused;
            return;
        }
        cluster.SocketOf[tag] = cluster.Slots.Count;
        cluster.Slots.Add(tag);
    }

    internal void Drop(uint tag)
    {
        uint stem = tag & StemBitmask;
        if (!_clusters.TryGetValue(stem, out Group? cluster) || !cluster.SocketOf.Remove(tag, out int socket))
            return;

        cluster.Slots[socket] = 0u;
        cluster.Free.Push(socket);
        if (cluster.SocketOf.Count is 0)
            _clusters.Remove(stem);
    }

    internal List<uint>? SocketsForStem(uint stem)
    {
        return _clusters.TryGetValue(stem & StemBitmask, out Group? cluster) ? cluster.Slots : null;
    }

    internal int InstalledTagTallyForStem(uint stem)
    {
        return _clusters.TryGetValue(stem & StemBitmask, out Group? cluster) ? cluster.SocketOf.Count : 0;
    }
}

internal sealed class ContactWorldState
{
    internal Dictionary<uint, KineticEngine.LandblockKinetics> Landblocks { get; } = new();
    internal List<uint> LbSockets { get; } = [];
    internal Dictionary<uint, int> LbOrdinals { get; } = new();
    internal Stack<int> LbSpareSockets { get; } = new();
    internal ConcurrentDictionary<uint, CellKinetics> CellStruct { get; } = new();
    internal ConcurrentDictionary<uint, PackedCellStructContactAsset> PlanarChamberStruct { get; } = new();
    internal ConcurrentDictionary<uint, PackedEnvCellTopology> PlanarEnvironChamber { get; } = new();
    internal ConcurrentDictionary<uint, BuildingKinetics> Buildings { get; } = new();
    internal ConcurrentDictionary<uint, EnvCell> EnvCells { get; } = new();
    internal ConcurrentDictionary<uint, CellWebTerrain> Terrain { get; } = new();
    internal ConcurrentDictionary<uint, ObjRefChamber> ExteriorChambers { get; } = new();
    internal Dictionary<uint, List<ProxyEntry>> ShadeChambers { get; } = new();
    internal Dictionary<uint, List<uint>> ShadeActorChambers { get; } = new();
    internal HashSet<uint> SuspendedShadeActors { get; } = [];
    internal Dictionary<uint, List<uint>> SuspendedShadeActorChambers { get; } = new();
    internal Dictionary<uint, HashSet<uint>> WithdrawnStemsByHolder { get; } = new();
    internal Dictionary<uint, IReadOnlyList<ProxyShape>> ShadeActorForms { get; } = new();
    internal Dictionary<uint, ProxyRegistry.EnrolmentRecord> ShadeActorRegistrations { get; } = new();
    internal Dictionary<uint, ulong> ShadeHolderVersions { get; } = new();
    internal Dictionary<uint, HashSet<uint>> ShadeHolderStems { get; } = new();
    internal Dictionary<uint, List<uint>> ShadeStemHolderSockets { get; } = new();
    internal Dictionary<uint, Dictionary<uint, int>> ShadeStemHolderOrdinals { get; } = new();
    internal Dictionary<uint, Stack<int>> ShadeStemSpareSockets { get; } = new();
    internal List<uint> ShadeHolderSockets { get; } = [];
    internal Dictionary<uint, int> ShadeHolderOrdinals { get; } = new();
    internal Stack<int> ShadeHolderSpareSockets { get; } = new();

    internal Dictionary<uint, IReadOnlyList<ProxyShape>> ShadeActorCanonPieceArrs { get; } = new();
    internal Dictionary<uint, List<uint>> ShadeActorCanonChamberArrs { get; } = new();
    internal Dictionary<uint, CanonCellSetRoute> ShadeActorCanonChamberArrCourses { get; } = new();
    internal Dictionary<uint, List<CanonPartRow>> CanonPieceListingsByChamber { get; } = new();

    internal Dictionary<uint, uint> ShadeDescendantAncestor { get; } = new();
    internal Dictionary<uint, List<uint>> ShadeAncestorDescendants { get; } = new();
    internal Dictionary<uint, IReadOnlyList<ProxyShape>> ShadeDescendantPieceArrs { get; } = new();

    // Every mutation of the five landblock-scoped world maps goes through the typed helpers below so
    // these ledgers stay exact.
    internal PrefixIndex ChamberStructTags { get; } = new();
    internal PrefixIndex PlanarChamberStructTags { get; } = new();
    internal PrefixIndex PlanarEnvironChamberTags { get; } = new();
    internal PrefixIndex StructureTags { get; } = new();
    internal PrefixIndex EnvironChamberTags { get; } = new();

    internal void AssignChamberStruct(uint ident, CellKinetics val) => Install(CellStruct, ChamberStructTags, ident, val);
    internal bool TryAppendChamberStruct(uint ident, CellKinetics val) => TryInstall(CellStruct, ChamberStructTags, ident, val);
    internal bool DropChamberStruct(uint ident) => Evict(CellStruct, ChamberStructTags, ident);

    internal void AssignPlanarChamberStruct(uint ident, PackedCellStructContactAsset val) => Install(PlanarChamberStruct, PlanarChamberStructTags, ident, val);
    internal bool TryAppendPlanarChamberStruct(uint ident, PackedCellStructContactAsset val) => TryInstall(PlanarChamberStruct, PlanarChamberStructTags, ident, val);
    internal bool DropPlanarChamberStruct(uint ident) => Evict(PlanarChamberStruct, PlanarChamberStructTags, ident);

    internal void AssignPlanarEnvironChamber(uint ident, PackedEnvCellTopology val) => Install(PlanarEnvironChamber, PlanarEnvironChamberTags, ident, val);
    internal bool TryAppendPlanarEnvironChamber(uint ident, PackedEnvCellTopology val) => TryInstall(PlanarEnvironChamber, PlanarEnvironChamberTags, ident, val);
    internal bool DropPlanarEnvironChamber(uint ident) => Evict(PlanarEnvironChamber, PlanarEnvironChamberTags, ident);

    internal void AssignStructure(uint ident, BuildingKinetics val) => Install(Buildings, StructureTags, ident, val);
    internal bool TryAppendStructure(uint ident, BuildingKinetics val) => TryInstall(Buildings, StructureTags, ident, val);
    internal bool DropStructure(uint ident) => Evict(Buildings, StructureTags, ident);

    internal void AssignEnvironChamber(uint ident, EnvCell val) => Install(EnvCells, EnvironChamberTags, ident, val);
    internal bool TryAppendEnvironChamber(uint ident, EnvCell val) => TryInstall(EnvCells, EnvironChamberTags, ident, val);
    internal bool DropEnvironChamber(uint ident) => Evict(EnvCells, EnvironChamberTags, ident);

    private static void Install<T>(ConcurrentDictionary<uint, T> lookup, PrefixIndex tags, uint ident, T val)
    {
        lookup[ident] = val;
        tags.Add(ident);
    }

    private static bool TryInstall<T>(ConcurrentDictionary<uint, T> lookup, PrefixIndex tags, uint ident, T val)
    {
        if (!lookup.TryAdd(ident, val))
            return false;
        tags.Add(ident);
        return true;
    }

    private static bool Evict<T>(ConcurrentDictionary<uint, T> lookup, PrefixIndex tags, uint ident)
    {
        if (!lookup.TryRemove(ident, out _))
            return false;
        tags.Drop(ident);
        return true;
    }
}

// The single owner-token for a ContactWorldState
internal sealed class ContactWorldStateSlot
{
    private const string RevokedMsg = "Transferred collision generation";

    private ContactWorldState? _phase;
    private bool _revoked;

    internal ContactWorldStateSlot() : this(new ContactWorldState())
    {
    }

    internal ContactWorldStateSlot(ContactWorldState current)
    {
        _phase = current ?? throw new ArgumentNullException(nameof(current));
    }

    internal ContactWorldState Current
    {
        get
        {
            if (_revoked)
                throw new ObjectDisposedException(RevokedMsg);
            return Volatile.Read(ref _phase) ?? throw new ObjectDisposedException(RevokedMsg);
        }
    }

    internal ContactWorldState TransferTo(ContactWorldStateSlot dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        if (_revoked)
            throw new ObjectDisposedException(RevokedMsg);
        ContactWorldState moving = _phase ?? throw new ObjectDisposedException(RevokedMsg);

        _revoked = true;
        Volatile.Write(ref dest._phase, moving);
        _phase = null;
        return moving;
    }

    internal void Revoke()
    {
        _revoked = true;
        _phase = null;
    }

    internal ContactWorldState Capture() => Current;
}

using System.Collections.Concurrent;
using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Realm.Cells;

internal sealed record CellWebTerrain(LandCanvas Terrain, Vector3 Origin);

internal sealed record ReadiedChamberGraphLandblock(
    uint LandblockPrefix,
    IReadOnlyList<uint> EnvCellIdsToRemove,
    IReadOnlyList<KeyValuePair<uint, EnvCell>> EnvCells,
    bool HasTerrain,
    CellWebTerrain? Terrain);

public sealed class ChamberGraph
{
    private const uint StemBitmask = 0xFFFF0000u;
    private const uint LoBitmask = 0xFFFFu;
    private const uint LeadInside = 0x0100u;
    private const uint ExteriorChambersPerChunk = 0x40u;

    private readonly ContactWorldStateSlot _world;

    public ChamberGraph() : this(new ContactWorldStateSlot())
    {
    }

    internal ChamberGraph(ContactWorldStateSlot collisionWorld) =>
        _world = collisionWorld ?? throw new ArgumentNullException(nameof(collisionWorld));

    private ConcurrentDictionary<uint, EnvCell> Inside => _world.Current.EnvCells;

    private ConcurrentDictionary<uint, CellWebTerrain> Terrain => _world.Current.Terrain;

    private ConcurrentDictionary<uint, ObjRefChamber> Outdoor => _world.Current.ExteriorChambers;

    public ObjRefChamber? CurrChamber { get; internal set; }

    public bool Contains(uint environChamberIdent) => Inside.ContainsKey(environChamberIdent);

    public void Add(EnvCell chamber) => _world.Current.TryAppendEnvironChamber(chamber.Id, chamber);

    public void EnrollLand(uint lbStem, LandCanvas land, Vector3 realmOrigin)
    {
        uint stem = lbStem & StemBitmask;
        Terrain[stem] = new CellWebTerrain(land, realmOrigin);
        for (uint lo = 1u; lo <= ExteriorChambersPerChunk; ++lo)
        {
            int ordinal = (int)(lo - 1u);
            Outdoor[stem | lo] = GroundCell.Synthesize(stem | lo, land, realmOrigin, ordinal / 8, ordinal % 8);
        }
    }

    public bool TryFetchLandOrigin(uint ident, out Vector3 origin)
    {
        if (Terrain.TryGetValue(ident & StemBitmask, out CellWebTerrain? terrain))
        {
            origin = terrain.Origin;
            return true;
        }
        origin = Vector3.Zero;
        return false;
    }

    public void RemoveLandblock(uint lbStem)
    {
        uint stem = lbStem & StemBitmask;
        if (CurrChamber is { } latest && (latest.Id & StemBitmask) == stem)
            CurrChamber = null;
        Terrain.TryRemove(stem, out _);
        for (uint lo = 1u; lo <= ExteriorChambersPerChunk; ++lo)
            Outdoor.TryRemove(stem | lo, out _);
        DropInsideByStem(stem);
    }

    public void DropEnvironChambersForLb(uint lbStem)
    {
        uint stem = lbStem & StemBitmask;
        if (CurrChamber is { } latest && (latest.Id & StemBitmask) == stem && (latest.Id & LoBitmask) >= LeadInside)
            CurrChamber = null;
        DropInsideByStem(stem);
    }

    public ObjRefChamber? ObtainShown(uint ident)
    {
        if (ident is 0u)
            return null;
        uint lo = ident & LoBitmask;
        if (lo >= LeadInside)
            return Inside.GetValueOrDefault(ident);
        if (lo is < 1u or > ExteriorChambersPerChunk)
            return null;
        return Outdoor.GetValueOrDefault(ident);
    }

    public ObjRefChamber? Neighbor(ObjRefChamber chamber, in ChamberGateway gateway) => ObtainShown(gateway.OtherCellId);

    /// <summary>The root cell itself, or whichever of its stabbed cells contains the point.</summary>
    public EnvCell? FindVisibleChildCell(uint trunkIdent, Vector3 realmPt)
    {
        if (!Inside.TryGetValue(trunkIdent, out EnvCell? trunk))
            return null;
        if (trunk.PtInCell(realmPt))
            return trunk;
        foreach (uint stabIdent in trunk.StabList)
        {
            if (Inside.TryGetValue(stabIdent, out EnvCell? stab) && stab.PtInCell(realmPt))
                return stab;
        }
        return null;
    }

    internal LandblockSwapBuilder BuildLbSubstituteBuilder(ChamberGraph loading, uint lbIdent) =>
        new(this, loading, lbIdent);

    private void DropInsideByStem(uint stem)
    {
        var realm = _world.Current;
        if (realm.EnvironChamberTags.SocketsForStem(stem) is not { } sockets)
            return;
        int tally = sockets.Count;
        for (int idx = 0; idx < tally; ++idx)
        {
            uint ident = sockets[idx];
            if (ident is not 0u)
                realm.DropEnvironChamber(ident);
        }
    }

    internal sealed class LandblockSwapBuilder : IDisposable
    {
        private enum Step
        {
            CollectStaged,
            CollectStale,
            Done,
        }

        private readonly ChamberGraph _engaged;
        private readonly ChamberGraph _loading;
        private readonly uint _stem;
        private readonly List<KeyValuePair<uint, EnvCell>> _install = [];
        private readonly HashSet<uint> _linedIdents = [];
        private readonly List<uint> _discard = [];
        private readonly PrefixKeyWalk _stroll = new();
        private Step _hop = Step.CollectStaged;

        internal LandblockSwapBuilder(ChamberGraph engaged, ChamberGraph loading, uint lbIdent)
        {
            _engaged = engaged;
            _loading = loading;
            _stem = lbIdent & StemBitmask;
        }

        internal ReadiedChamberGraphLandblock? Prepared { get; private set; }

        // Returns true once the plan is complete
        internal bool Advance()
        {
            switch (_hop)
            {
                case Step.CollectStaged:
                    if (_stroll.TryUpcoming(_loading._world.Current.EnvironChamberTags, _stem, out uint linedIdent))
                    {
                        if ((linedIdent & LoBitmask) >= LeadInside && _loading.Inside.TryGetValue(linedIdent, out EnvCell? chamber))
                        {
                            _install.Add(new KeyValuePair<uint, EnvCell>(linedIdent, chamber));
                            _linedIdents.Add(linedIdent);
                        }
                        return false;
                    }
                    _hop = Step.CollectStale;
                    return false;

                case Step.CollectStale:
                    if (_stroll.TryUpcoming(_engaged._world.Current.EnvironChamberTags, _stem, out uint engagedIdent))
                    {
                        if ((engagedIdent & LoBitmask) >= LeadInside && !_linedIdents.Contains(engagedIdent) && _engaged.Inside.ContainsKey(engagedIdent))
                            _discard.Add(engagedIdent);
                        return false;
                    }
                    bool hasLand = _loading.Terrain.TryGetValue(_stem, out CellWebTerrain? land);
                    Prepared = new ReadiedChamberGraphLandblock(_stem, _discard, _install, hasLand, land);
                    _hop = Step.Done;
                    return true;

                default:
                    return true;
            }
        }

        public void Dispose() => _stroll.Reset();

        // Walks a ledger's slots for one prefix, one non-zero key per call, then resets
        private sealed class PrefixKeyWalk
        {
            private List<uint>? _sockets;
            private int _threshold;
            private int _cur;
            private bool _open;

            public bool TryUpcoming(PrefixIndex register, uint stem, out uint tag)
            {
                if (!_open)
                {
                    _sockets = register.SocketsForStem(stem);
                    _threshold = _sockets?.Count ?? 0;
                    _cur = 0;
                    _open = true;
                }
                while (_cur < _threshold)
                {
                    uint contender = _sockets![_cur++];
                    if (contender is not 0u)
                    {
                        tag = contender;
                        return true;
                    }
                }
                tag = 0u;
                Reset();
                return false;
            }

            public void Reset()
            {
                _sockets = null;
                _open = false;
            }
        }
    }
}

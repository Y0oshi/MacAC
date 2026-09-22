using System.Collections.Concurrent;
using UcgCellGraph = MacAC.Mechanics.Realm.Cells.ChamberGraph;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticAssetCache
{
    internal LandblockSwapBuilder BuildLbSubstituteBuilder(KineticAssetCache loading, uint lbIdent, uint[] gfxObjectIdents, uint[] rigIdents) =>
        new(this, loading, lbIdent, gfxObjectIdents, rigIdents);

    internal sealed class LandblockSwapBuilder : IDisposable
    {
        private enum Phase
        {
            GfxObjs,
            Setups,
            CellsInstall,
            CellsRetire,
            PackedCellsInstall,
            PackedCellsRetire,
            PackedEnvCellsInstall,
            PackedEnvCellsRetire,
            BuildingsInstall,
            BuildingsRetire,
            Graph,
            Done,
        }

        // The captured install list and retire list for one world map
        private sealed class Ledger<T>
        {
            public readonly List<KeyValuePair<uint, T>> Install = [];
            public readonly HashSet<uint> InstalledIdents = [];
            public readonly List<uint> Retire = [];

            public void GrabInstall(ConcurrentDictionary<uint, T> loading, uint ident)
            {
                if (!loading.TryGetValue(ident, out T? val))
                    return;
                Install.Add(new KeyValuePair<uint, T>(ident, val));
                InstalledIdents.Add(ident);
            }

            public void GrabRetire(ConcurrentDictionary<uint, T> engaged, uint ident)
            {
                if (!InstalledIdents.Contains(ident) && engaged.ContainsKey(ident))
                    Retire.Add(ident);
            }
        }

        private readonly KineticAssetCache _engaged;
        private readonly KineticAssetCache _loading;
        private readonly uint _stem;
        private readonly uint[] _gfxIdents;
        private readonly uint[] _rigIdents;
        private readonly List<KeyValuePair<uint, GfxObjKinetics>> _gfx = [];
        private readonly List<KeyValuePair<uint, GfxObjVisualExtent>> _extents = [];
        private readonly List<KeyValuePair<uint, PackedGfxObjContactAsset>> _denseGfx = [];
        private readonly List<KeyValuePair<uint, SetupKinetics>> _setups = [];
        private readonly List<KeyValuePair<uint, PackedSetupContact>> _denseSetups = [];
        private readonly Ledger<CellKinetics> _chambers = new();
        private readonly Ledger<PackedCellStructContactAsset> _denseChambers = new();
        private readonly Ledger<PackedEnvCellTopology> _denseEnvironChambers = new();
        private readonly Ledger<BuildingKinetics> _structures = new();
        private readonly UcgCellGraph.LandblockSwapBuilder _chamberGraph;
        private List<uint>? _tagSockets;
        private int _tagSocketThreshold;
        private bool _tagSocketsGrabbed;
        private Phase _stage;
        private int _cur;

        internal LandblockSwapBuilder(KineticAssetCache engaged, KineticAssetCache loading, uint lbIdent, uint[] gfxIdents, uint[] rigIdents)
        {
            _engaged = engaged;
            _loading = loading;
            _stem = lbIdent & StemBitmask;
            _gfxIdents = gfxIdents;
            _rigIdents = rigIdents;
            _chamberGraph = engaged.ChamberGraph.BuildLbSubstituteBuilder(loading.ChamberGraph, _stem);
        }

        internal int JobUnits { get; private set; }

        internal BakedKineticCacheLandblock? Prepared { get; private set; }

        private ContactWorldState LoadingRealm => _loading.World;

        private ContactWorldState EngagedRealm => _engaged.World;

        public void Dispose()
        {
            _tagSockets = null;
            _tagSocketsGrabbed = false;
            _chamberGraph.Dispose();
        }

        // Does one unit of work; true once the replacement is fully prepared
        internal bool Advance()
        {
            switch (_stage)
            {
                case Phase.GfxObjs:
                    if (_cur < _gfxIdents.Length)
                    {
                        uint ident = _gfxIdents[_cur++];
                        // Anything the staging cache already resolved is shared into the active one now.
                        Preinstall(_loading._gfxObjs, _engaged._gfxObjs, ident);
                        Preinstall(_loading._extents, _engaged._extents, ident);
                        Preinstall(_loading._denseGfxObjs, _engaged._denseGfxObjs, ident);
                        Capture(_loading._gfxObjs, ident, _gfx);
                        Capture(_loading._extents, ident, _extents);
                        Capture(_loading._denseGfxObjs, ident, _denseGfx);
                        ++JobUnits;
                        return false;
                    }
                    UpcomingStage();
                    return false;

                case Phase.Setups:
                    if (_cur < _rigIdents.Length)
                    {
                        uint ident = _rigIdents[_cur++];
                        Preinstall(_loading._setups, _engaged._setups, ident);
                        Preinstall(_loading._denseSetups, _engaged._denseSetups, ident);
                        Capture(_loading._setups, ident, _setups);
                        Capture(_loading._denseSetups, ident, _denseSetups);
                        ++JobUnits;
                        return false;
                    }
                    UpcomingStage();
                    return false;

                // Each world map: gather the staging prefix's keys to install,
                // then the active prefix's keys that were not re-installed.
                case Phase.CellsInstall:
                    return Sweep(LoadingRealm.ChamberStructTags, _loading.Cells, _chambers, install: true);
                case Phase.CellsRetire:
                    return Sweep(EngagedRealm.ChamberStructTags, _engaged.Cells, _chambers, install: false);
                case Phase.PackedCellsInstall:
                    return Sweep(LoadingRealm.PlanarChamberStructTags, _loading.DenseChambers, _denseChambers, install: true);
                case Phase.PackedCellsRetire:
                    return Sweep(EngagedRealm.PlanarChamberStructTags, _engaged.DenseChambers, _denseChambers, install: false);
                case Phase.PackedEnvCellsInstall:
                    return Sweep(LoadingRealm.PlanarEnvironChamberTags, _loading.DenseEnvironChambers, _denseEnvironChambers, install: true);
                case Phase.PackedEnvCellsRetire:
                    return Sweep(EngagedRealm.PlanarEnvironChamberTags, _engaged.DenseEnvironChambers, _denseEnvironChambers, install: false);
                case Phase.BuildingsInstall:
                    return Sweep(LoadingRealm.StructureTags, _loading.Buildings, _structures, install: true);
                case Phase.BuildingsRetire:
                    return Sweep(EngagedRealm.StructureTags, _engaged.Buildings, _structures, install: false);

                case Phase.Graph:
                    ++JobUnits;
                    if (!_chamberGraph.Advance())
                        return false;
                    Prepared = new BakedKineticCacheLandblock(
                        _stem,
                        _gfx,
                        _extents,
                        _denseGfx,
                        _setups,
                        _denseSetups,
                        _chambers.Retire,
                        _chambers.Install,
                        _denseChambers.Retire,
                        _denseChambers.Install,
                        _denseEnvironChambers.Retire,
                        _denseEnvironChambers.Install,
                        _structures.Retire,
                        _structures.Install,
                        _chamberGraph.Prepared!);
                    ++_stage;
                    return true;

                default:
                    return true;
            }
        }

        private void UpcomingStage()
        {
            _cur = 0;
            ++_stage;
        }

        // One key of one map phase; moves to the next phase when the prefix is exhausted
        private bool Sweep<T>(PrefixIndex register, ConcurrentDictionary<uint, T> lookup, Ledger<T> into, bool install)
        {
            if (TryGrabUpcomingStemTag(register, out uint ident))
            {
                if (install)
                    into.GrabInstall(lookup, ident);
                else
                    into.GrabRetire(lookup, ident);
                ++JobUnits;
                return false;
            }
            ++_stage;
            return false;
        }

        private static void Capture<T>(ConcurrentDictionary<uint, T> src, uint ident, List<KeyValuePair<uint, T>> dest)
        {
            if (src.TryGetValue(ident, out T? val))
                dest.Add(new KeyValuePair<uint, T>(ident, val));
        }

        private static void Preinstall<T>(ConcurrentDictionary<uint, T> src, ConcurrentDictionary<uint, T> dest, uint ident)
        {
            if (src.TryGetValue(ident, out T? val))
                dest.TryAdd(ident, val);
        }

        // Walks a ledger's prefix slots one key per call; the slot list is captured on first use and
        // released when exhausted
        private bool TryGrabUpcomingStemTag(PrefixIndex register, out uint tag)
        {
            if (!_tagSocketsGrabbed)
            {
                _tagSockets = register.SocketsForStem(_stem);
                _tagSocketThreshold = _tagSockets?.Count ?? 0;
                _tagSocketsGrabbed = true;
                _cur = 0;
            }

            while (_cur < _tagSocketThreshold)
            {
                uint contender = _tagSockets![_cur++];
                if (contender is not 0u)
                {
                    tag = contender;
                    return true;
                }
            }

            tag = 0u;
            _tagSockets = null;
            _tagSocketsGrabbed = false;
            return false;
        }
    }
}

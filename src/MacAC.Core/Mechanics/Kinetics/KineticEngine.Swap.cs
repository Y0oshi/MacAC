using MacAC.Mechanics.Realm.Cells;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticEngine
{
    internal readonly record struct LandblockSwapStep(bool Completed, bool Worked, bool HasOwner, uint OwnerId)
    {
        public static LandblockSwapStep DidJob => new(Completed: false, Worked: true, HasOwner: false, OwnerId: 0u);
        public static LandblockSwapStep Finished => new(Completed: true, Worked: false, HasOwner: false, OwnerId: 0u);
        public static LandblockSwapStep Owner(uint holderIdent) => new(Completed: false, Worked: true, HasOwner: true, holderIdent);
    }

    // A staging cache and engine that read through to the active ones
    internal sealed class ContactStagingBuilder : IDisposable
    {
        internal ContactStagingBuilder(KineticEngine engaged, KineticAssetCache engagedStash)
        {
            StagingCache = engagedStash.BuildVacantImpactLoading(new ContactWorldStateSlot());
            LoadingEngine = new KineticEngine { DataCache = StagingCache, Objects = engaged.Objects };
        }

        internal KineticAssetCache StagingCache { get; }

        internal KineticEngine LoadingEngine { get; }

        public void Dispose()
        {
        }
    }

    // Everything the active engine needs to swap one landblock in
    internal sealed class BakedKineticEngineLandblock(
        uint lbIdent,
        LandblockKinetics lb,
        KineticEngine loading,
        BakedKineticCacheLandblock blobStash,
        ProxyRegistry.BakedLandblockProxySwap shades)
    {
        internal uint LbIdent { get; } = lbIdent;
        internal LandblockKinetics Landblock { get; } = lb;
        internal KineticEngine Staging { get; } = loading;
        internal BakedKineticCacheLandblock DataCache { get; } = blobStash;
        internal ProxyRegistry.BakedLandblockProxySwap Shadows { get; } = shades;
    }

    // Runs the cache builder to completion, then the shadow builder, then bakes the result
    internal sealed class LandblockSwapBuilder(
        uint lbIdent,
        KineticEngine.LandblockKinetics lb,
        KineticEngine loading,
        KineticAssetCache.LandblockSwapBuilder blob,
        ProxyRegistry.LandblockSwapBuilder shades) : IDisposable
    {
        private int _stage;

        internal int JobUnits => blob.JobUnits + shades.JobUnits;

        internal BakedKineticEngineLandblock? Prepared { get; private set; }

        public void Dispose()
        {
            blob.Dispose();
            shades.Dispose();
        }

        internal void RenewKeptHolder(uint holderIdent) => shades.RenewHolder(holderIdent);

        internal bool Advance()
        {
            switch (_stage)
            {
                case 0:
                    if (blob.Advance())
                        ++_stage;
                    return false;
                case 1:
                    if (!shades.Advance())
                        return false;
                    if (blob.Prepared is { } baked && shades.Prepared is { } proxies)
                        Prepared = new BakedKineticEngineLandblock(lbIdent, lb, loading, baked, proxies);
                    ++_stage;
                    return true;
                default:
                    return true;
            }
        }
    }

    internal ContactStagingBuilder BuildImpactLoadingBuilder(uint markLbIdent)
    {
        _ = markLbIdent;
        return new ContactStagingBuilder(this, DemandStash("Active"));
    }

    internal LandblockSwapBuilder BuildLbSubstituteBuilder(
        KineticEngine loading,
        uint lbIdent,
        uint[] gfxObjectIdents,
        uint[] rigIdents,
        IReadOnlyList<uint> anticipatedKeptHolders)
    {
        ArgumentNullException.ThrowIfNull(loading);
        uint canon = (lbIdent & StemBitmask) | LoBitmask;
        if (!loading.Landblocks.TryGetValue(canon, out LandblockKinetics? lb))
            throw new InvalidOperationException($"Staging collision generation has no landblock 0x{canon:X8}.");

        var loadingStash = loading.DemandStash("Staging");
        return new LandblockSwapBuilder(
            canon,
            lb,
            loading,
            DemandStash("Active").BuildLbSubstituteBuilder(loadingStash, canon, gfxObjectIdents, rigIdents),
            ShadeObjects.BuildLbSubstituteBuilder(loading.ShadeObjects, canon, anticipatedKeptHolders));
    }

    internal void SealLbSubstitute(BakedKineticEngineLandblock substitute)
    {
        var engagedStash = DemandStash("Active");
        var loadingStash = substitute.Staging.DemandStash("Staging");
        var loadingShades = substitute.Staging.ShadeObjects;
        var engaged = engagedStash.ImpactRealm.Current;

        // Shadow cells for everything being retired go first, before any step runs.
        var blob = substitute.DataCache;
        foreach (uint chamberIdent in blob.CellIdsToRemove)
            engaged.ShadeChambers.Remove(chamberIdent);
        foreach (uint chamberIdent in blob.CellGraph.EnvCellIdsToRemove)
            engaged.ShadeChambers.Remove(chamberIdent);

        using (LandblockSwapCursor cur = BuildLbSubstituteEnactCur(substitute))
        {
            while (true)
            {
                var hop = cur.Advance();
                if (hop.HasOwner)
                    ShadeObjects.ImposeSealedHolderSubstitute(loadingShades, hop.OwnerId, substitute.LbIdent);
                if (hop.Completed)
                    break;
            }
        }

        ShadeObjects.RefloodStemHoldersFollowingSubstitute(substitute.LbIdent, substitute.Shadows.HolderIdents);
        loadingStash.ImpactRealm.Revoke();
    }

    internal LandblockSwapCursor BuildLbSubstituteEnactCur(BakedKineticEngineLandblock substitute) => new(this, substitute);

    // Applies a baked replacement to the active world, one element per Advance
    internal sealed class LandblockSwapCursor : IDisposable
    {
        private enum Juncture
        {
            RetireCells,
            InstallCells,
            RetirePackedCells,
            InstallPackedCells,
            RetirePackedEnvCells,
            InstallPackedEnvCells,
            RetireBuildings,
            InstallBuildings,
            RetireEnvCells,
            Terrain,
            InstallEnvCells,
            Landblock,
            Owners,
            CurrentCell,
            Done,
        }

        private const int LandChamberTally = 0x40;

        private readonly KineticEngine _engine;
        private readonly KineticAssetCache _stash;
        private readonly ContactWorldState _world;
        private readonly BakedKineticEngineLandblock _substitute;
        private Juncture _juncture;
        private int _ordinal;

        internal LandblockSwapCursor(KineticEngine dest, BakedKineticEngineLandblock substitute)
        {
            _engine = dest;
            _stash = dest.DataCache ?? throw new InvalidOperationException("Collision engine has no data cache");
            _world = _stash.ImpactRealm.Capture();
            _substitute = substitute;
        }

        internal uint LbIdent => _substitute.LbIdent;

        public void Dispose()
        {
        }

        internal LandblockSwapStep Advance()
        {
            var blob = _substitute.DataCache;
            var graph = blob.CellGraph;
            while (true)
            {
                switch (_juncture)
                {
                    case Juncture.RetireCells:
                        if (Next(blob.CellIdsToRemove, out uint chamberIdent)) { _world.DropChamberStruct(chamberIdent); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.InstallCells:
                        if (Next(blob.Cells, out KeyValuePair<uint, CellKinetics> chamber)) { _world.AssignChamberStruct(chamber.Key, chamber.Value); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.RetirePackedCells:
                        if (Next(blob.FlatCellIdsToRemove, out uint denseChamberIdent)) { _world.DropPlanarChamberStruct(denseChamberIdent); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.InstallPackedCells:
                        if (Next(blob.FlatCells, out KeyValuePair<uint, PackedCellStructContactAsset> denseChamber)) { _world.AssignPlanarChamberStruct(denseChamber.Key, denseChamber.Value); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.RetirePackedEnvCells:
                        if (Next(blob.FlatEnvCellIdsToRemove, out uint denseEnvironIdent)) { _world.DropPlanarEnvironChamber(denseEnvironIdent); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.InstallPackedEnvCells:
                        if (Next(blob.FlatEnvCells, out KeyValuePair<uint, PackedEnvCellTopology> denseEnviron)) { _world.AssignPlanarEnvironChamber(denseEnviron.Key, denseEnviron.Value); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.RetireBuildings:
                        if (Next(blob.BuildingIdsToRemove, out uint structureIdent)) { _world.DropStructure(structureIdent); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.InstallBuildings:
                        if (Next(blob.Buildings, out KeyValuePair<uint, BuildingKinetics> structure)) { _world.AssignStructure(structure.Key, structure.Value); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.RetireEnvCells:
                        if (Next(graph.EnvCellIdsToRemove, out uint environIdent)) { _world.DropEnvironChamber(environIdent); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.Terrain:
                        if (TickLand(graph))
                            return LandblockSwapStep.DidJob;
                        UpcomingJuncture();
                        continue;
                    case Juncture.InstallEnvCells:
                        if (Next(graph.EnvCells, out KeyValuePair<uint, EnvCell> environChamber)) { _world.AssignEnvironChamber(environChamber.Key, environChamber.Value); return LandblockSwapStep.DidJob; }
                        continue;
                    case Juncture.Landblock:
                        _engine.SetupLbReplicate(_substitute.LbIdent, _substitute.Landblock);
                        UpcomingJuncture();
                        return LandblockSwapStep.DidJob;
                    case Juncture.Owners:
                        if (Next(_substitute.Shadows.HolderIdents, out uint holderIdent))
                            return LandblockSwapStep.Owner(holderIdent);
                        continue;
                    case Juncture.CurrentCell:
                        RebindLatestChamber(graph.LandblockPrefix);
                        ++_juncture;
                        return LandblockSwapStep.Finished;
                    default:
                        return LandblockSwapStep.Finished;
                }
            }
        }

        private void UpcomingJuncture()
        {
            ++_juncture;
            _ordinal = 0;
        }

        // Takes the next item of a list stage; false when the list is exhausted (and the stage advanced)
        private bool Next<T>(IReadOnlyList<T> ranks, out T rank)
        {
            if (_ordinal < ranks.Count)
            {
                rank = ranks[_ordinal++];
                return true;
            }
            rank = default!;
            UpcomingJuncture();
            return false;
        }

        // Index 0 installs or removes the terrain record; 1..0x40 synthesize or drop each land cell
        private bool TickLand(ReadiedChamberGraphLandblock graph)
        {
            if (_ordinal > LandChamberTally)
                return false;

            uint stem = graph.LandblockPrefix;
            if (_ordinal is 0)
            {
                if (graph.HasTerrain)
                    _world.Terrain[stem] = graph.Terrain!;
                else
                    _world.Terrain.TryRemove(stem, out _);
                ++_ordinal;
                return true;
            }

            uint lo = (uint)_ordinal++;
            uint ident = stem | lo;
            if (graph.HasTerrain)
            {
                var land = graph.Terrain!;
                int chamberOrdinal = (int)(lo - 1u);
                _world.ExteriorChambers[ident] = GroundCell.Synthesize(ident, land.Terrain, land.Origin, chamberOrdinal / 8, chamberOrdinal % 8);
            }
            else
            {
                _world.ExteriorChambers.TryRemove(ident, out _);
            }
            return true;
        }

        // If the player's current cell was in this landblock, point it at the freshly installed copy
        private void RebindLatestChamber(uint stem)
        {
            uint latestChamberIdent = _stash.ChamberGraph.CurrChamber?.Id ?? 0u;
            if ((latestChamberIdent & StemBitmask) == stem)
                _stash.ChamberGraph.CurrChamber = _stash.ChamberGraph.ObtainShown(latestChamberIdent);
        }
    }
}

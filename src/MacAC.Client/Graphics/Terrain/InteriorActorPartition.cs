using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

public static class InteriorActorPartition
{
    internal enum ProjClass : byte
    {
        OutdoorStatic,
        CellStatic,
        Dynamic,
    }

    internal interface IWatcher
    {
        void BeginFrame();

        void Observe(
            uint lbIdent,
            RealmActor actor,
            ProjClass projClass);

        void Complete(ClientResult outcome);

        void ScrapCycle();
    }

    public sealed class ClientResult
    {
        public Dictionary<uint, List<RealmActor>> ByChamber { get; } = [];
        public List<RealmActor> OutdoorStatic { get; } = [];
        public List<RealmActor> Dynamics { get; } = [];

        private readonly List<uint> _vacantChamberTemp = [];

        internal void WipeForReuse()
        {
            foreach (var roster in ByChamber.Values)
                roster.Clear();
            OutdoorStatic.Clear();
            Dynamics.Clear();
        }

        internal void PruneVacantChamberBins()
        {
            _vacantChamberTemp.Clear();
            foreach (var (chamberIdent, roster) in ByChamber)
            {
                if (roster.Count is 0)
                    _vacantChamberTemp.Add(chamberIdent);
            }
            foreach (var chamberIdent in _vacantChamberTemp)
                ByChamber.Remove(chamberIdent);
        }
    }

    public static ClientResult Partition(
        HashSet<uint> shownChambers,
        IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                     IReadOnlyList<RealmActor> Entities,
                     IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        FrustumFacets? frustum = null,
        uint neverPruneLbIdent = 0u)
    {
        ClientResult outcome = new ClientResult();
        Partition(
            outcome,
            shownChambers,
            lbListings,
            frustum,
            neverPruneLbIdent);
        return outcome;
    }

    public static void Partition(
        ClientResult outcome,
        HashSet<uint> shownChambers,
        IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                     IReadOnlyList<RealmActor> Entities,
                     IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        FrustumFacets? frustum = null,
        uint neverPruneLbIdent = 0u)
    {
        outcome.WipeForReuse();
        foreach (var listing in lbListings)
        {
            if (!IsLbShown(
                    listing.LandblockId,
                    listing.AabbMin,
                    listing.AabbMax,
                    frustum,
                    neverPruneLbIdent))

                continue;

            foreach (var entity in listing.Entities)
            {
                if (entity.MeshRefs.Count is 0) continue;

                if (entity.ServerGuid is not 0)
                {
                    outcome.Dynamics.Add(entity);
                }
                else if (entity.ParentCellId is uint chamber && IsIndoorCellId(chamber))
                {
                    if (!shownChambers.Contains(chamber))
                        continue;
                    if (!outcome.ByChamber.TryGetValue(chamber, out var roster))
                        outcome.ByChamber[chamber] = roster = [];
                    roster.Add(entity);
                }
                else
                {
                    outcome.OutdoorStatic.Add(entity);
                }
            }
        }

        outcome.PruneVacantChamberBins();
    }

    public static bool IsIndoorCellId(uint chamberIdent)
    {
        uint lo = chamberIdent & 0xFFFFu;
        return lo is >= 0x0100u and not 0xFFFFu;
    }

    public static bool IsIndoorCellId(uint? chamberIdent) => chamberIdent is uint c && IsIndoorCellId(c);

    internal static void Partition(
        ClientResult outcome,
        HashSet<uint> shownChambers,
        IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                     IReadOnlyList<RealmActor> Entities,
                     IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        IWatcher? watcher,
        FrustumFacets? frustum = null,
        uint neverPruneLbIdent = 0u)
    {
        if (watcher is null)
        {
            Partition(
                outcome,
                shownChambers,
                lbListings,
                frustum,
                neverPruneLbIdent);
            return;
        }

        watcher.BeginFrame();
        try
        {
            outcome.WipeForReuse();
            foreach (var listing in lbListings)
            {
                if (!IsLbShown(
                        listing.LandblockId,
                        listing.AabbMin,
                        listing.AabbMax,
                        frustum,
                        neverPruneLbIdent))

                    continue;

                foreach (var entity in listing.Entities)
                {
                    if (entity.MeshRefs.Count is 0) continue;

                    if (entity.ServerGuid is not 0)
                    {
                        outcome.Dynamics.Add(entity);
                        watcher.Observe(
                            listing.LandblockId,
                            entity,
                            ProjClass.Dynamic);
                    }
                    else if (entity.ParentCellId is uint chamber && IsIndoorCellId(chamber))
                    {
                        if (!shownChambers.Contains(chamber))
                            continue;
                        if (!outcome.ByChamber.TryGetValue(chamber, out var roster))
                            outcome.ByChamber[chamber] = roster = [];
                        roster.Add(entity);
                        watcher.Observe(
                            listing.LandblockId,
                            entity,
                            ProjClass.CellStatic);
                    }
                    else
                    {
                        outcome.OutdoorStatic.Add(entity);
                        watcher.Observe(
                            listing.LandblockId,
                            entity,
                            ProjClass.OutdoorStatic);
                    }
                }
            }

            outcome.PruneVacantChamberBins();
            watcher.Complete(outcome);
        }
        catch
        {
            watcher.ScrapCycle();
            throw;
        }
    }

    private static bool IsLbShown(
        uint lbIdent,
        Vector3 aabbLower,
        Vector3 aabbUpper,
        FrustumFacets? frustum,
        uint neverPruneLbIdent)
    {
        return frustum is null
        || lbIdent == neverPruneLbIdent
        || FrustumPruner.IsAabbShown(frustum.Value, aabbLower, aabbUpper);
    }
}

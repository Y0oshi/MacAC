using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics.Stride;

// Keeps coarse landscape mesh groups until their source records change
internal sealed class FarLandscapeDrawShelf(
    RealmPaintRouter router, IStrideFrameRealmData realm)
{
    private readonly record struct ChamberTag(uint Block, int Side, int Index);
    private readonly record struct LotRef(Actor Entity, int PartIndex, int BatchIndex);

    private sealed class Actor(RenderMirrorRecord capture, uint chamberIdent)
    {
        public RenderMirrorRecord Record = capture;
        public readonly uint CellId = chamberIdent;
        public readonly List<RealmPaintRouter.StrideClassifiedBatch> Batches = [];
        public readonly List<RealmPaintRouter.StrideShelvedPart> Parts = [];
        public bool[] Visible = [];
        public RealmPaintRouter.InstLampGroup Lights;
        public uint Inside;
        public Vector2 Selection;
    }

    private sealed class Entry(uint[] chambers)
    {
        public readonly uint[] Cells = chambers;
        public readonly ulong[] Revisions = new ulong[chambers.Length];
        public readonly List<Actor> Entities = [];
        public readonly List<LotRef> Solid = [];
        public readonly List<LotRef> Alpha = [];
        public readonly Dictionary<ClusterTag, List<LotRef>> Clusters = [];
        public readonly List<List<LotRef>> ClusterRosters = [];
        public long TriMeshVer = -1;
        public bool Reattempt;
    }

    private readonly Dictionary<ChamberTag, Entry> _listings = [];
    private readonly List<ChamberTag> _expired = [];
    private readonly List<RealmPaintRouter.StrideClassifiedPickingPart> _pickTemp = [];
    private readonly List<RealmPaintRouter.StrideClassifiedBatch> _alphaTemp = [];
    private readonly int[] _alphaEnds = new int[16];
    private (RenderStageEpoch Generation, uint TupleLandblockId) _ctx;

    internal int ReassembleTally { get; private set; }
    internal int ActorTaxonomyTally { get; private set; }
    internal int ListingTally => _listings.Count;
    internal ReadOnlySpan<int> AlphaEnds => _alphaEnds;

    internal void Clear()
    {
        _listings.Clear();
        _expired.Clear();
        _alphaTemp.Clear();
    }

    internal void BeginFrame()
    {
        if (_ctx != realm.KeptCtx)
        {
            Clear();
            _ctx = realm.KeptCtx;
        }

        // Drop departed content even when the camera never revisits its cells
        _expired.Clear();
        foreach ((ChamberTag tag, Entry listing) in _listings)
        {
            bool populated = false;
            bool wasPopulated = false;
            foreach (ulong rev in listing.Revisions)
                wasPopulated |= rev != 0;
            if (!wasPopulated)
                continue;
            foreach (uint chamber in listing.Cells)
            {
                if (realm.FetchExteriorChamberRasterizeRev(chamber) is > 0)
                {
                    populated = true;
                    break;
                }
            }
            if (!populated)
                _expired.Add(tag);
        }
        foreach (ChamberTag tag in _expired)
            _listings.Remove(tag);
    }

    internal bool TryAffix(
        uint chunk, int flank, int ordinal,
        SequencedPaintFlow flow, IStrideLookInViewSource views, int course,
        Vector3 cam, List<RealmPaintRouter.StrideClassifiedBatch> alpha)
    {
        int span = 8 / flank;
        int leadX = ordinal / flank * span;
        int leadY = ordinal % flank * span;
        uint leadChamber = chunk | (uint)(leadX * 8 + leadY + 1);
        if (realm.FetchExteriorChamberRasterizeRev(leadChamber) is null)
            return false;

        ChamberTag tag = new(chunk, flank, ordinal);
        if (!_listings.TryGetValue(tag, out Entry? listing))
        {
            uint[] chambers = new uint[span * span];
            int cur = 0;
            for (int x = leadX; x < leadX + span; ++x)
                for (int y = leadY; y < leadY + span; ++y)
                    chambers[cur++] = chunk | (uint)(x * 8 + y + 1);
            listing = new Entry(chambers);
            _listings.Add(tag, listing);
        }

        if (NeedsReassemble(listing))
            Rebuild(listing);
        else
            RenewActors(listing);

        foreach (Actor actor in listing.Entities)
        {
            router.LocateStashedStrollIllumination(
                in actor.Record, _ctx.TupleLandblockId,
                out actor.Lights, out actor.Inside, out actor.Selection);
            for (int idx = 0; idx < actor.Parts.Count; ++idx)
            {
                var piece = actor.Parts[idx];
                bool shown = router.AdmitStashedStrollPiece(
                    in actor.Record, in piece, views, course);
                actor.Visible[idx] = shown;
                if (shown)
                {
                    var pick = piece.Selection;
                    router.BroadcastStrollPickPiece(in pick);
                }
            }
        }

        foreach (LotRef gear in listing.Solid)
        {
            Actor actor = gear.Entity;
            if (!actor.Visible[gear.PartIndex])
                continue;
            var lot = actor.Batches[gear.BatchIndex];
            StrollPaintJuncture juncture = IsDynamic(actor.Record)
                ? StrollPaintJuncture.Dynamic : StrollPaintJuncture.OutdoorStatic;
            flow.Tack(new OrderedDrawDirective(
                lot.Key, lot.Transform, juncture, leadChamber, lot.ClipSlot,
                actor.Lights, actor.Inside, lot.Alpha,
                actor.Selection, lot.DetailCategory, AllowInstanceMerge: true));
        }

        for (int chamberOrdinal = 0; chamberOrdinal < listing.Cells.Length; ++chamberOrdinal)
        {
            _alphaTemp.Clear();
            foreach (LotRef gear in listing.Alpha)
            {
                Actor actor = gear.Entity;
                if (actor.CellId != listing.Cells[chamberOrdinal] || !actor.Visible[gear.PartIndex])
                    continue;
                var lot = actor.Batches[gear.BatchIndex];
                _alphaTemp.Add(lot with
                {
                    Lights = actor.Lights,
                    IndoorFlag = actor.Inside,
                    SelectionLighting = actor.Selection,
                    SortDistanceSq = Vector3.DistanceSquared(
                        Vector3.Transform(lot.LocalSortCenter, lot.Transform), cam),
                });
            }
            _alphaTemp.Sort(static (a, b) => b.SortDistanceSq.CompareTo(a.SortDistanceSq));
            alpha.AddRange(_alphaTemp);
            _alphaEnds[chamberOrdinal] = alpha.Count;
        }
        return true;
    }

    private bool NeedsReassemble(Entry listing)
    {
        if (listing.Reattempt || listing.TriMeshVer != router.StrollTriMeshReadinessVer)
            return true;
        for (int idx = 0; idx < listing.Cells.Length; ++idx)
        {
            if (listing.Revisions[idx] != realm.FetchExteriorChamberRasterizeRev(listing.Cells[idx]))
                return true;
        }
        return false;
    }

    private void Rebuild(Entry listing)
    {
        listing.Entities.Clear();
        listing.Solid.Clear();
        listing.Alpha.Clear();
        listing.Reattempt = true;
        bool reattempt = false;
        listing.TriMeshVer = router.StrollTriMeshReadinessVer;
        var observed = new HashSet<RenderMirrorId>();
        for (int idx = 0; idx < listing.Cells.Length; ++idx)
        {
            uint chamberIdent = listing.Cells[idx];
            listing.Revisions[idx] = realm.FetchExteriorChamberRasterizeRev(chamberIdent) ?? 0;
            var records = realm.FetchExteriorObjects(chamberIdent);
            reattempt |= !records.IsComplete;
            foreach (RenderMirrorRecord capture in records.Records)
            {
                if (!observed.Add(capture.Id))
                    continue;
                Actor actor = new Actor(capture, chamberIdent);
                reattempt |= ClassifyActor(actor, records.TupleLandblockId);
                listing.Entities.Add(actor);
            }
        }
        Regroup(listing);
        listing.Reattempt = reattempt;
        ++ReassembleTally;
    }

    private void RenewActors(Entry listing)
    {
        bool altered = false;
        bool reattempt = false;
        foreach (Actor actor in listing.Entities)
        {
            if (!realm.TryFetchLatestProj(actor.Record.Source.LocalEntityId, out var latest)
                || latest.Id != actor.Record.Id
                || latest.OwnerIncarnation != actor.Record.OwnerIncarnation)
            {
                Rebuild(listing);
                return;
            }
            if (latest == actor.Record && !IsDynamic(latest))
                continue;
            listing.Reattempt = true;
            altered = true;
            actor.Record = latest;
            reattempt |= ClassifyActor(actor, _ctx.TupleLandblockId);
        }
        if (altered)
        {
            Regroup(listing);
            listing.Reattempt = reattempt;
        }
    }

    private bool ClassifyActor(Actor actor, uint tupleLbIdent)
    {
        actor.Batches.Clear();
        actor.Parts.Clear();
        _pickTemp.Clear();
        router.ClassifyActorForStroll(
            in actor.Record, tupleLbIdent,
            actor.Batches, _pickTemp, onlineDynamic: IsDynamic(actor.Record),
            keptPieces: actor.Parts);
        if (actor.Visible.Length < actor.Parts.Count)
            actor.Visible = new bool[actor.Parts.Count];
        ++ActorTaxonomyTally;
        return router.StrollTaxonomyQueued;
    }

    private static void Regroup(Entry listing)
    {
        listing.Solid.Clear();
        listing.Alpha.Clear();
        listing.Clusters.Clear();
        foreach (List<LotRef> cluster in listing.ClusterRosters)
            cluster.Clear();
        RegroupRest(listing);
    }

    private static void RegroupRest(Entry listing)
    {
        int clusterTally = 0;
        foreach (Actor actor in listing.Entities)
        {
            for (int pieceOrdinal = 0; pieceOrdinal < actor.Parts.Count; ++pieceOrdinal)
            {
                var piece = actor.Parts[pieceOrdinal];
                for (int b = piece.BatchStart; b < piece.BatchStart + piece.BatchCount; ++b)
                {
                    var lot = actor.Batches[b];
                    LotRef gear = new(actor, pieceOrdinal, b);
                    if (!lot.IsOpaque)
                    {
                        listing.Alpha.Add(gear);
                        continue;
                    }
                    if (!listing.Clusters.TryGetValue(lot.Key, out List<LotRef>? cluster))
                    {
                        if (clusterTally == listing.ClusterRosters.Count)
                            listing.ClusterRosters.Add([]);
                        cluster = listing.ClusterRosters[clusterTally++];
                        listing.Clusters.Add(lot.Key, cluster);
                    }
                    cluster.Add(gear);
                }
            }
        }
        for (int idx = 0; idx < clusterTally; ++idx)
            listing.Solid.AddRange(listing.ClusterRosters[idx]);
    }

    private static bool IsDynamic(in RenderMirrorRecord capture)
    {
        return capture.ProjectionClass is RenderMirrorClass.LiveDynamicRoot
            or RenderMirrorClass.EquippedChild;
    }
}

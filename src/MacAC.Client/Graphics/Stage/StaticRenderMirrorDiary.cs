using MacAC.Client.Graphics.Batching;
using MacAC.Client.Paging;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal interface IRenderStaticMirrorDiarySink
{
    void Reconcile(
        LandblockAssemble assemble,
        GpuLandblockSpatialBulletin bulletin);

    void Retire(GpuLandblockSunset sunset);
}

internal sealed class StaticRenderMirrorDiary(RenderMirrorDiary journal) :
    IRenderStaticMirrorDiarySink
{
    private const byte StaticActorDomain = 1;
    private const byte EnvironChamberShellDomain = 2;

    private readonly RenderMirrorDiary _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly Dictionary<uint, Dictionary<RenderMirrorId, TrackedMirror>>
        _byLb = [];
    private readonly Dictionary<RenderMirrorId, TrackedMirror>
        _followedByIdent = [];
    private readonly Dictionary<RealmActor, RenderMirrorId> _identByActor =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<uint, List<RealmActor>> _actorsByLb =
        [];
    private readonly List<RenderMirrorRecord> _contenders = [];
    private readonly List<RenderMirrorId> _removed = [];
    private ulong _upcomingIncarnation = 1;

    public int ProjCount { get; private set; }
    public int LbTally => _byLb.Count;

    public void Reconcile(
        LandblockAssemble build,
        GpuLandblockSpatialBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(bulletin);
        uint lbIdent = Canonicalize(bulletin.LandblockId);
        if (Canonicalize(build.LandblockIdent) != lbIdent)
        {
            throw new ArgumentException(
                "Static render projection publication belongs to another landblock",
                nameof(build));
        }

        if (!bulletin.RequiresActivation)
            return;

        if (bulletin.RenderTraversalOrder is 0)
        {
            throw new InvalidOperationException(
                $"Landblock 0x{lbIdent:X8} has no render traversal order");
        }
        _contenders.Clear();
        for (int idx = 0; idx < bulletin.Landblock.Entities.Count; ++idx)
        {
            RealmActor actor = bulletin.Landblock.Entities[idx];
            if (actor.ServerGuid is 0)
            {
                _contenders.Add(ProjectStaticActor(
                    lbIdent,
                    actor,
                    RasterizeTraversalOrderTag.Compose(
                        bulletin.RenderTraversalOrder,
                        idx)));
            }
        }

        if (build.EnvCells is { } environChambers)
        {
            for (int idx = 0; idx < environChambers.Shells.Length; ++idx)
                _contenders.Add(ProjectEnvironChamberShell(lbIdent, environChambers.Shells[idx]));
        }

        _contenders.Sort(MirrorRecordComparer.Instance);
        if (!_byLb.TryGetValue(
                lbIdent,
                out Dictionary<RenderMirrorId, TrackedMirror>? latest))
        {
            latest = [];
            _byLb.Add(lbIdent, latest);
        }

        _removed.Clear();
        foreach (RenderMirrorId ident in latest.Keys)
            _removed.Add(ident);

        for (int idx = 0; idx < _contenders.Count; ++idx)
        {
            var contender = _contenders[idx];
            if (latest.TryGetValue(
                    contender.Id,
                    out TrackedMirror? kept)
                && kept is not null)
            {
                _removed.Remove(contender.Id);
                var approved = contender with
                {
                    OwnerIncarnation = kept.Record.OwnerIncarnation,
                    PreviousTransform = new EarlierRasterizeTransform(
                        kept.Record.Transform.LocalToWorld),
                };
                if (approved.Source.GeometryFingerprint
                        == kept.Record.Source.GeometryFingerprint
                    && approved.Source.AppearanceFingerprint
                        == kept.Record.Source.AppearanceFingerprint
                    && approved.EntityPayload.IsBuildingShell
                        == kept.Record.EntityPayload.IsBuildingShell
                    && approved.EntityPayload.CasterIdentity
                        == kept.Record.EntityPayload.CasterIdentity)
                {
                    approved = approved with
                    {
                        EntityPayload = kept.Record.EntityPayload,
                    };
                }
                if (approved.ProjectionClass
                    != kept.Record.ProjectionClass)
                {
                    _journal.Unregister(
                        kept.Record.Id,
                        kept.Record.OwnerIncarnation);
                    approved = approved with
                    {
                        OwnerIncarnation = UpcomingIncarnation(),
                    };
                    _journal.Register(in approved);
                    kept.Record = approved;
                    continue;
                }

                if (approved != kept.Record)
                {
                    _journal.AffixDifference(kept.Record, approved);
                    kept.Record = approved;
                }
                continue;
            }

            var registered = contender with
            {
                OwnerIncarnation = UpcomingIncarnation(),
            };
            _journal.Register(in registered);
            TrackedMirror followed = new TrackedMirror(registered);
            latest.Add(registered.Id, followed);
            _followedByIdent.Add(registered.Id, followed);
            ++ProjCount;
        }

        _removed.Sort(MirrorIdComparer.Instance);
        for (int idx = 0; idx < _removed.Count; ++idx)
        {
            var ident = _removed[idx];
            var omitted = latest[ident];
            _journal.Unregister(ident, omitted.Record.OwnerIncarnation);
            latest.Remove(ident);
            _followedByIdent.Remove(ident);
            --ProjCount;
        }

        if (latest.Count is 0)
            _byLb.Remove(lbIdent);
        ReplaceStaticActorLookup(lbIdent, bulletin.Landblock.Entities);
    }

    public void Retire(GpuLandblockSunset sunset)
    {
        ArgumentNullException.ThrowIfNull(sunset);
        uint lbIdent = Canonicalize(sunset.LandblockId);
        if (!_byLb.Remove(
                lbIdent,
                out Dictionary<RenderMirrorId, TrackedMirror>? latest))

            return;

        _removed.Clear();
        foreach (RenderMirrorId ident in latest.Keys)
            _removed.Add(ident);
        _removed.Sort(MirrorIdComparer.Instance);
        for (int idx = 0; idx < _removed.Count; ++idx)
        {
            var followed = latest[_removed[idx]];
            _journal.Unregister(
                followed.Record.Id,
                followed.Record.OwnerIncarnation);
        }

        ProjCount -= latest.Count;
        foreach (RenderMirrorId ident in latest.Keys)
            _followedByIdent.Remove(ident);
        DropStaticActorLookup(lbIdent);
    }

    public void Clear(RenderStageEpoch substituteGen)
    {
        _byLb.Clear();
        _followedByIdent.Clear();
        _identByActor.Clear();
        _actorsByLb.Clear();
        _contenders.Clear();
        _removed.Clear();
        ProjCount = 0;
        _journal.Clear(substituteGen);
    }

    public void SynchronizeEngagedMovingSrcs(
        IReadOnlyList<RealmActor> engagedActors)
    {
        ArgumentNullException.ThrowIfNull(engagedActors);
        for (int idx = 0; idx < engagedActors.Count; ++idx)
        {
            RealmActor actor = engagedActors[idx];
            if (!_identByActor.TryGetValue(
                    actor,
                    out RenderMirrorId ident)
                || !_followedByIdent.TryGetValue(
                    ident,
                    out TrackedMirror? followed))

                continue;

            var latest =
                RenderMirrorRecordMint.ProjectActor(
                    ident,
                    RenderMirrorClass.ActiveAnimatedStatic,
                    followed.Record.OwnerIncarnation,
                    followed.Record.Residency.OwnerLandblockId,
                    followed.Record.Residency.FullCellId,
                    actor,
                    spatiallyShown: true,
                    invokerPersona: actor.IsStructureShell
                        ? RasterizeInvokerPersonaFlavor.Building
                        : RasterizeInvokerPersonaFlavor.OutdoorStatic) with
                {
                    PreviousTransform = new EarlierRasterizeTransform(
                        followed.Record.Transform.LocalToWorld),
                    SortKey = followed.Record.SortKey,
                };
            if (followed.Record.ProjectionClass
                != RenderMirrorClass.ActiveAnimatedStatic)
            {
                _journal.Register(in latest);
            }
            else
            {
                _journal.AffixDifference(followed.Record, latest);
            }
            followed.Record = latest;
        }
    }

    internal bool TryGet(
        RenderMirrorId ident,
        out RenderMirrorRecord capture)
    {
        foreach (Dictionary<RenderMirrorId, TrackedMirror> projections
                 in _byLb.Values)
        {
            if (projections.TryGetValue(
                    ident,
                    out TrackedMirror? followed)
                && followed is not null)
            {
                capture = followed.Record;
                return true;
            }
        }

        capture = default;
        return false;
    }

    internal static RenderMirrorId StaticActorIdent(
        uint lbIdent,
        uint ownActorIdent)
    {
        return BuildProjIdent(
            StaticActorDomain,
            Canonicalize(lbIdent),
            ownActorIdent);
    }

    internal static RenderMirrorId EnvironChamberShellIdent(
        uint lbIdent,
        uint chamberIdent)
    {
        return BuildProjIdent(
            EnvironChamberShellDomain,
            Canonicalize(lbIdent),
            chamberIdent);
    }

    private RenderMirrorRecord ProjectStaticActor(
        uint lbIdent,
        RealmActor actor,
        RasterizeOrderTag orderTag)
    {
        var ident = StaticActorIdent(lbIdent, actor.Id);
        uint wholeChamberIdent = actor.ParentCellId ?? lbIdent;
        return RenderMirrorRecordMint.ProjectActor(
            ident,
            InteriorActorPartition.IsIndoorCellId(actor.ParentCellId)
                ? RenderMirrorClass.IndoorCellStatic
                : RenderMirrorClass.OutdoorStatic,
            default,
            lbIdent,
            wholeChamberIdent,
            actor,
            spatiallyShown: true,
            invokerPersona: actor.IsStructureShell
                ? RasterizeInvokerPersonaFlavor.Building
                : RasterizeInvokerPersonaFlavor.OutdoorStatic) with
        {
            SortKey = orderTag,
        };
    }

    private static RenderMirrorRecord ProjectEnvironChamberShell(
        uint lbIdent,
        EnvironChamberShellStance shell)
    {
        StableRasterizeHash128 xformDigest = StableRasterizeHash128.Create();
        xformDigest.Add(shell.WorldPosition);
        xformDigest.Add(shell.Rotation);
        xformDigest.Add(1.0f);
        var xformFingerprint = xformDigest.Finish();

        StableRasterizeHash128 geoDigest = StableRasterizeHash128.Create();
        geoDigest.Add(shell.GeometryId);
        geoDigest.Add(shell.EnvironmentId);
        geoDigest.Add(shell.CellStructure);
        geoDigest.Add(shell.Surfaces.Length);
        for (int idx = 0; idx < shell.Surfaces.Length; ++idx)
            geoDigest.Add(shell.Surfaces[idx]);
        var geoFingerprint = geoDigest.Finish();

        var ident = EnvironChamberShellIdent(lbIdent, shell.CellId);
        return new RenderMirrorRecord(
            ident,
            RenderMirrorClass.IndoorCellStatic,
            default,
            new RasterizeTransform(
                shell.WorldPosition,
                shell.Rotation,
                1.0f,
                shell.Transform),
            new EarlierRasterizeTransform(shell.Transform),
            new RasterizeTriMeshGroup(
                RasterizeAssetHnd.FromRaw(shell.GeometryId),
                1,
                0),
            new RasterizeMatlVariant(0, 0, 1.0f),
            new RenderSpatialTenancy(
                RasterizeSpatialBin.FromRaw(shell.CellId),
                lbIdent,
                shell.CellId),
            new RenderRealmBounds(
                shell.WorldBounds.Min,
                shell.WorldBounds.Max),
            RenderMirrorFlags.Draw
                | RenderMirrorFlags.SpatiallyResident,
            default,
            ident.ToOrderTag(),
            RasterizeStaleBitmask.All,
            new RasterizeOriginMetadata(
                shell.CellId,
                0,
                unchecked((uint)shell.GeometryId),
                shell.CellId,
                shell.CellId,
                0,
                xformFingerprint,
                geoFingerprint,
                RenderStageHash128.Empty));
    }

    private RasterizeHolderIncarnation UpcomingIncarnation()
    {
        return _upcomingIncarnation == ulong.MaxValue
            ? throw new InvalidOperationException(
                "Static render-projection incarnation exhausted")
            : RasterizeHolderIncarnation.FromRaw(_upcomingIncarnation++);
    }

    private static RenderMirrorId BuildProjIdent(
        byte domain,
        uint canonLbIdent,
        uint ownIdent)
    {
        return RenderMirrorId.FromRaw(
            ((ulong)domain << 56)
            | ((ulong)(canonLbIdent >> 16) << 32)
            | ownIdent);
    }

    private static uint Canonicalize(uint lbIdent) =>
        (lbIdent & 0xFFFF0000u) | 0xFFFFu;

    private void ReplaceStaticActorLookup(
        uint lbIdent,
        IReadOnlyList<RealmActor> actors)
    {
        DropStaticActorLookup(lbIdent);
        List<RealmActor> kept = new List<RealmActor>();
        for (int idx = 0; idx < actors.Count; ++idx)
        {
            RealmActor actor = actors[idx];
            if (actor.ServerGuid is not 0)
                continue;
            var ident = StaticActorIdent(lbIdent, actor.Id);
            if (!_followedByIdent.ContainsKey(ident))
                continue;
            _identByActor.Add(actor, ident);
            kept.Add(actor);
        }
        if (kept.Count > 0)
            _actorsByLb.Add(lbIdent, kept);
    }

    private void DropStaticActorLookup(uint lbIdent)
    {
        if (!_actorsByLb.Remove(
                lbIdent,
                out List<RealmActor>? actors))

            return;
        for (int idx = 0; idx < actors.Count; ++idx)
            _identByActor.Remove(actors[idx]);
    }

    private sealed class TrackedMirror(RenderMirrorRecord capture)
    {
        public RenderMirrorRecord Record { get; set; } = capture;
    }

    private sealed class MirrorRecordComparer :
        IComparer<RenderMirrorRecord>
    {
        public static MirrorRecordComparer Instance { get; } = new();

        public int Compare(RenderMirrorRecord x, RenderMirrorRecord y) =>
            x.Id.CompareTo(y.Id);
    }

    private sealed class MirrorIdComparer : IComparer<RenderMirrorId>
    {
        public static MirrorIdComparer Instance { get; } = new();

        public int Compare(RenderMirrorId x, RenderMirrorId y) =>
            x.CompareTo(y);
    }
}

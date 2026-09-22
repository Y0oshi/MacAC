using MacAC.Mechanics.Landscape;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public abstract record LandblockFlowJob(uint LandblockId)
{
    public sealed record Pull(
        uint LandblockId,
        LandblockFlowJobFlavor Kind,
        ulong Generation = 0,
        LandblockAssembleOrigin Origin = default) : LandblockFlowJob(LandblockId)
    {
        public LandblockBuildAsk Request =>
            new(LandblockId, Kind, Generation, Origin);
    }
    public sealed record Drop(
        uint LandblockId,
        ulong Generation = 0) : LandblockFlowJob(LandblockId);

    public sealed record WipeLoads() : LandblockFlowJob(0);
}

public abstract record LandblockFlowOutcome(uint LandblockId, ulong Generation)
{
    public sealed record Fetched(
        uint LandblockId,
        LandblockFlowTier Tier,
        LandblockAssemble Build,
        LandblockTessellationData MeshData,
        ulong Generation = 0
    ) : LandblockFlowOutcome(LandblockId, Generation)
    {
        public Fetched(
            uint lbIdent,
            LandblockFlowTier tier,
            MountedLandblock lb,
            LandblockTessellationData triMeshBlob,
            ulong gen = 0)
            : this(lbIdent, tier, new LandblockAssemble(lb), triMeshBlob, gen)
        {
        }

        public MountedLandblock Landblock => Build.Landblock;
    }

    public sealed record Elevated(
        uint LandblockId,
        LandblockAssemble Build,
        LandblockTessellationData MeshData,
        ulong Generation = 0
    ) : LandblockFlowOutcome(LandblockId, Generation)
    {
        public Elevated(
            uint lbIdent,
            MountedLandblock lb,
            LandblockTessellationData triMeshBlob,
            ulong gen = 0)
            : this(lbIdent, new LandblockAssemble(lb), triMeshBlob, gen)
        {
        }

        public MountedLandblock Landblock => Build.Landblock;
        public IReadOnlyList<RealmActor> Entities => Landblock.Entities;
    }

    public sealed record Botched(
        uint LandblockId,
        string Error,
        ulong Generation = 0) : LandblockFlowOutcome(LandblockId, Generation);
    public sealed record Dropped(
        uint LandblockId,
        ulong Generation = 0) : LandblockFlowOutcome(LandblockId, Generation);

    public sealed record ClientWorkerCrashed(string Error) : LandblockFlowOutcome(0, 0);
}

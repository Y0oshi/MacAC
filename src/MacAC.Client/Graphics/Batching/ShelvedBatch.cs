using System.Numerics;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Batching;

/// <summary>
/// A claim on a draw cluster that outlives the frame it was taken in. A shelved batch keeps the
/// cluster it last drew into so a later frame can skip the lookup, but a cluster that goes a frame
/// unused is retired and its instance storage handed back. The registration is unique for the
/// cluster's lifetime and cleared when it retires, so the ticket can say whether the pointer is
/// still good.
/// </summary>
internal readonly record struct ClusterTicket(long Registration)
{
    /// <summary>A ticket on nothing: what a shelved batch holds before it has drawn.</summary>
    public static ClusterTicket None => default;

    public bool IsHeld => Registration is not 0;

    /// <summary>
    /// Whether <paramref name="cluster"/> is still the cluster this ticket was taken from. False
    /// once that cluster has retired, so a stale ticket can never revive one.
    /// </summary>
    public bool StillNames(RealmPaintRouter.InstCluster? cluster) =>
        cluster is not null && IsHeld && Registration == cluster.Enrollment;
}

internal readonly record struct ShelvedBatch(
    ClusterTag Key,
    GpuTextureSlot TextureSlot,
    Matrix4x4 RestPose,
    RealmPaintRouter.InstCluster? Group = null,
    ClusterTicket Ticket = default);

internal readonly record struct ShelvedPickingPart(
    int PartIndex,
    uint GfxObjId,
    Matrix4x4 RestPose);

internal sealed class ActorShelfEntry
{
    public required uint ActorIdent { get; init; }
    public required uint LbHint { get; init; }
    public required ShelvedBatch[] Batches { get; init; }
    public ShelvedPickingPart[] PickPieces { get; init; } = [];
}

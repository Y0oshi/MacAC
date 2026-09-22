namespace MacAC.Client.Graphics.Batching;

internal sealed class ActorTaxonomyStash
{
    private readonly Dictionary<(uint EntityId, uint LandblockHint), ActorShelfEntry> _listings = [];

    public int Count => _listings.Count;

    public bool TryGet(uint actorIdent, uint lbHint, out ActorShelfEntry? listing)
        => _listings.TryGetValue((actorIdent, lbHint), out listing);

    public void Fill(
        uint actorIdent,
        uint lbHint,
        ShelvedBatch[] lots,
        ShelvedPickingPart[]? pickPieces = null)
    {
        _listings[(actorIdent, lbHint)] = new ActorShelfEntry
        {
            ActorIdent = actorIdent,
            LbHint = lbHint,
            Batches = lots,
            PickPieces = pickPieces ?? [],
        };
    }

    public void DirtyActor(uint actorIdent)
    {
        if (_listings.Count == 0) return;
        List<(uint, uint)>? toDrop = null;
        foreach (var tag in _listings.Keys)
        {
            if (tag.EntityId == actorIdent)
            {
                toDrop ??= [];
                toDrop.Add(tag);
            }
        }
        if (toDrop is null) return;
        foreach (var kdx in toDrop) _listings.Remove(kdx);
    }

    public void DirtyLb(uint lbIdent)
    {
        if (_listings.Count == 0) return;

        List<(uint, uint)>? toDrop = null;
        foreach (var tag in _listings.Keys)
        {
            if (tag.LandblockHint == lbIdent)
            {
                toDrop ??= [];
                toDrop.Add(tag);
            }
        }
        if (toDrop is null) return;
        foreach (var kdx in toDrop) _listings.Remove(kdx);
    }

#if DEBUG
    // Asserts that the cached entry for actorIdent still matches what fresh classification would
    // produce
    public void DiagCrossVerify(uint actorIdent, uint lbHint, IReadOnlyList<ShelvedBatch> onlineLots)
    {
        if (!_listings.TryGetValue((actorIdent, lbHint), out var listing)) return;

        System.Diagnostics.Debug.Assert(
            listing.Batches.Length == onlineLots.Count,
            $"EntityClassificationCache: batch count mismatch for entity {actorIdent}: cached={listing.Batches.Length} live={onlineLots.Count}");

        for (int idx = 0; idx < listing.Batches.Length && idx < onlineLots.Count; idx++)
        {
            var stashed = listing.Batches[idx];
            var online = onlineLots[idx];
            System.Diagnostics.Debug.Assert(
                stashed.Key.Equals(online.Key),
                $"EntityClassificationCache: GroupKey drift for entity {actorIdent} batch {idx}");
            System.Diagnostics.Debug.Assert(
                stashed.TextureSlot == online.TextureSlot,
                $"EntityClassificationCache: texture slot drift for entity {actorIdent} batch {idx}");
            System.Diagnostics.Debug.Assert(
                MatrixApproxEqual(stashed.RestPose, online.RestPose, epsilon: 1e-5f),
                $"EntityClassificationCache: RestPose drift for entity {actorIdent} batch {idx}");
        }
    }

    private static bool MatrixApproxEqual(System.Numerics.Matrix4x4 a, System.Numerics.Matrix4x4 b, float epsilon)
    {
        return System.MathF.Abs(a.M11 - b.M11) <= epsilon && System.MathF.Abs(a.M12 - b.M12) <= epsilon &&
               System.MathF.Abs(a.M13 - b.M13) <= epsilon && System.MathF.Abs(a.M14 - b.M14) <= epsilon &&
               System.MathF.Abs(a.M21 - b.M21) <= epsilon && System.MathF.Abs(a.M22 - b.M22) <= epsilon &&
               System.MathF.Abs(a.M23 - b.M23) <= epsilon && System.MathF.Abs(a.M24 - b.M24) <= epsilon &&
               System.MathF.Abs(a.M31 - b.M31) <= epsilon && System.MathF.Abs(a.M32 - b.M32) <= epsilon &&
               System.MathF.Abs(a.M33 - b.M33) <= epsilon && System.MathF.Abs(a.M34 - b.M34) <= epsilon &&
               System.MathF.Abs(a.M41 - b.M41) <= epsilon && System.MathF.Abs(a.M42 - b.M42) <= epsilon &&
               System.MathF.Abs(a.M43 - b.M43) <= epsilon && System.MathF.Abs(a.M44 - b.M44) <= epsilon;
    }
#endif
}

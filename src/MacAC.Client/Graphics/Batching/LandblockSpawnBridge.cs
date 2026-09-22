using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

public sealed class LandblockSpawnBridge
{
    private sealed class RefEnrollment
    {
        public bool Wanted;
        public bool Pinned;
    }

    private sealed class LandblockEnrollment
    {
        public bool WantsFetched;
        public Dictionary<ulong, RefEnrollment> Ordinary { get; } = [];
        public Dictionary<ulong, RefEnrollment> Prepared { get; } = [];
    }

    private readonly IBatchMeshBridge _bridge;
    private readonly Dictionary<uint, LandblockEnrollment> _registrations = [];

    public LandblockSpawnBridge(IBatchMeshBridge bridge)
    {
        System.ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public void OnLbFetched(
        MountedLandblock lb,
        IEnumerable<ulong>? additionalReadinessIdents = null,
        IEnumerable<ulong>? additionalPlainIdents = null,
        bool replaceExtant = false)
    {
        System.ArgumentNullException.ThrowIfNull(lb);

        HashSet<ulong> unique = new HashSet<ulong>();
        foreach (var actor in lb.Entities)
        {
            if (actor.ServerGuid is not 0) continue;

            foreach (var triMeshRef in actor.MeshRefs)
                unique.Add((ulong)triMeshRef.GfxObjId);
        }
        if (additionalPlainIdents is not null)
            unique.UnionWith(additionalPlainIdents.Where(static ident => ident != 0));

        HashSet<ulong>? readiedIdents = additionalReadinessIdents is null
            ? null
            : [.. additionalReadinessIdents];

        if (!_registrations.TryGetValue(lb.LandblockId, out var enrollment))
        {
            enrollment = new LandblockEnrollment { WantsFetched = true };
            _registrations.Add(lb.LandblockId, enrollment);
        }
        else if (!enrollment.WantsFetched)
        {
            // This is a new load edge that arrived while a preceding unload still had unfinished releases. The
            // new snapshot replaces the old desired set.
            OnLbFetchedBranch(enrollment);
        }
        else if (replaceExtant)
        {
            FlagAllUndesired(enrollment.Ordinary);
            FlagAllUndesired(enrollment.Prepared);
        }

        FlagWanted(enrollment.Ordinary, unique);
        if (readiedIdents is not null)
            FlagWanted(enrollment.Prepared, readiedIdents);

        List<Exception>? misses = null;
        FreeUndesired(enrollment.Ordinary, ref misses);
        FreeUndesired(enrollment.Prepared, ref misses);
        PruneReleasedUndesired(enrollment.Ordinary);
        PruneReleasedUndesired(enrollment.Prepared);
        ObtainWanted(enrollment.Ordinary, readied: false, ref misses);
        ObtainWanted(enrollment.Prepared, readied: true, ref misses);
        HurlMisses(
            misses,
            $"Landblock 0x{lb.LandblockId:X8} mesh-reference acquisition did not fully converge.");
    }

    private void OnLbFetchedBranch(LandblockEnrollment enrollment)
    {
        FlagAllUndesired(enrollment.Ordinary);
        FlagAllUndesired(enrollment.Prepared);
        enrollment.WantsFetched = true;
    }

    public bool IsLbRasterizePrimed(uint lbIdent)
    {
        if (!_registrations.TryGetValue(lbIdent, out var enrollment)
            || !enrollment.WantsFetched)
            return false;

        foreach (var duo in enrollment.Ordinary)
            if (!duo.Value.Wanted
                || !duo.Value.Pinned
                || !_bridge.IsRasterizeBlobPrimed(duo.Key))
                return false;
        foreach (var duo in enrollment.Prepared)
            if (!duo.Value.Wanted
                || !duo.Value.Pinned
                || !_bridge.IsRasterizeBlobPrimed(duo.Key))
                return false;
        return true;
    }

    public void OnLbUnloaded(uint lbIdent)
    {
        if (!_registrations.TryGetValue(lbIdent, out var enrollment))
            return;

        enrollment.WantsFetched = false;
        FlagAllUndesired(enrollment.Ordinary);
        FlagAllUndesired(enrollment.Prepared);

        List<Exception>? misses = null;
        FreeUndesired(enrollment.Ordinary, ref misses);
        FreeUndesired(enrollment.Prepared, ref misses);
        PruneReleasedUndesired(enrollment.Ordinary);
        PruneReleasedUndesired(enrollment.Prepared);

        if (enrollment.Ordinary.Count is 0 && enrollment.Prepared.Count is 0)
            _registrations.Remove(lbIdent);

        HurlMisses(
            misses,
            $"Landblock 0x{lbIdent:X8} mesh-reference release did not fully converge.");
    }

    private static void FlagWanted(
        Dictionary<ulong, RefEnrollment> registrations,
        IEnumerable<ulong> idents)
    {
        foreach (ulong ident in idents)
        {
            if (!registrations.TryGetValue(ident, out var reference))
            {
                reference = new RefEnrollment();
                registrations.Add(ident, reference);
            }

            reference.Wanted = true;
        }
    }

    private static void FlagAllUndesired(
        Dictionary<ulong, RefEnrollment> registrations)
    {
        foreach (var reference in registrations.Values)
            reference.Wanted = false;
    }

    private void ObtainWanted(
        Dictionary<ulong, RefEnrollment> registrations,
        bool readied,
        ref List<Exception>? misses)
    {
        foreach (var duo in registrations)
        {
            var reference = duo.Value;
            if (!reference.Wanted || reference.Pinned)
                continue;

            try
            {
                if (readied)
                    _bridge.PinReadiedRasterizeBlob(duo.Key);
                else
                    _bridge.IncrementRefTally(duo.Key);
                reference.Pinned = true;
            }
            catch (Exception problem)
            {
                if (problem is TriMeshRefAlterationFault { AlterationSealed: true })
                    reference.Pinned = true;
                (misses ??= []).Add(problem);
            }
        }
    }

    private void FreeUndesired(
        Dictionary<ulong, RefEnrollment> registrations,
        ref List<Exception>? misses)
    {
        foreach (var duo in registrations)
        {
            var reference = duo.Value;
            if (reference.Wanted || !reference.Pinned)
                continue;

            try
            {
                _bridge.DecrementRefTally(duo.Key);
                reference.Pinned = false;
            }
            catch (Exception problem)
            {
                if (problem is TriMeshRefAlterationFault { AlterationSealed: true })
                    reference.Pinned = false;
                (misses ??= []).Add(problem);
            }
        }
    }

    private static void PruneReleasedUndesired(
        Dictionary<ulong, RefEnrollment> registrations)
    {
        List<ulong>? released = null;
        foreach (var duo in registrations)
        {
            if (!duo.Value.Wanted && !duo.Value.Pinned)
                (released ??= []).Add(duo.Key);
        }

        if (released is null)
            return;
        foreach (ulong ident in released)
            registrations.Remove(ident);
    }

    private static void HurlMisses(List<Exception>? misses, string msg)
    {
        if (misses is null)
            return;
        if (misses.Count is 1)
            throw misses[0];
        throw new AggregateException(msg, misses);
    }
}

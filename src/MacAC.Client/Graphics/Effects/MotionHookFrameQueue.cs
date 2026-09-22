using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics.Effects;

public sealed class MotionHookFrameQueue(
    AnimHookRouter router,
    IEffectPoseSource poses)
{
    private readonly AnimHookRouter _router = router ?? throw new ArgumentNullException(nameof(router));
    private readonly IEffectPoseSource _postures = poses ?? throw new ArgumentNullException(nameof(poses));
    private readonly IActorEffectPoseLifetimeSource? _postureLifetimes = poses as IActorEffectPoseLifetimeSource;
    private readonly List<Entry> _listings = [];

    public int Count => _listings.Count;

    public void Capture(uint holderOwnIdent, AnimSequencer scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        Capture(holderOwnIdent, scheduler, scheduler.AbsorbQueuedTaps());
    }

    public void Drain()
    {
        for (int idx = 0; idx < _listings.Count; ++idx)
        {
            Entry listing = _listings[idx];
            if (_postureLifetimes is not null
                && _postureLifetimes.FetchPostureHolderLifespanVer(
                    listing.OwnerLocalId) != listing.OwnerLifetimeVersion)

                continue;
            for (int hi = 0; hi < listing.Hooks.Count; ++hi)
            {
                if (_postureLifetimes is not null
                    && _postureLifetimes.FetchPostureHolderLifespanVer(
                        listing.OwnerLocalId) != listing.OwnerLifetimeVersion)

                    break;
                Cue? tap = listing.Hooks[hi];
                if (tap is null)
                    continue;
                if (_postures.TryFetchTrunkPosture(
                        listing.OwnerLocalId,
                        out Matrix4x4 trunkRealm))
                {
                    _router.OnTap(
                        listing.OwnerLocalId,
                        trunkRealm.Translation,
                        tap);
                }
            }
        }
        _listings.Clear();
    }

    public void Clear() => _listings.Clear();

    internal void Capture(
        uint holderOwnIdent,
        AnimSequencer scheduler,
        IReadOnlyList<Cue> taps)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(taps);
        ulong holderLifespanVer =
            _postureLifetimes?.FetchPostureHolderLifespanVer(holderOwnIdent) ?? 0UL;

        for (int idx = 0; idx < taps.Count; ++idx)
        {
            if (_postureLifetimes is not null
                && _postureLifetimes.FetchPostureHolderLifespanVer(
                    holderOwnIdent) != holderLifespanVer)

                break;
            if (taps[idx] is AnimationDoneCue)
                scheduler.Manager.AnimationDone(success: true);
        }

        if (taps.Count is 0)
            return;

        _listings.Add(new Entry(
            holderOwnIdent,
            holderLifespanVer,
            taps));
    }

    private readonly record struct Entry(
        uint OwnerLocalId,
        ulong OwnerLifetimeVersion,
        IReadOnlyList<Cue> Hooks);
}

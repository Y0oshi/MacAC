using MacAC.Sim.Presence;

namespace MacAC.Client.Paging;

internal sealed class GraphicalDistantStanceFacilityPane
    : ISimPeerPlacementServiceWindow
{
    private readonly GpuRealmPhase _phase;
    private readonly Func<uint, bool> _isExhibitPrimed;

    internal GraphicalDistantStanceFacilityPane(
        GpuRealmPhase state,
        Func<uint, bool> isPresentationReady)
    {
        _phase = state ?? throw new ArgumentNullException(nameof(state));
        _isExhibitPrimed = isPresentationReady
            ?? throw new ArgumentNullException(nameof(isPresentationReady));
    }

    public bool IsWithinServiceWindow(uint lbIdent)
    {
        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        return _isExhibitPrimed(canon)
            && _phase.IsNearbyTier(canon);
    }
}

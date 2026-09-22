using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed record GpuLandblockSunset(
    uint LandblockId,
    LandblockSunsetFlavor Kind,
    IReadOnlyList<RealmActor> Entities);

// Exact result of the atomic spatial-generation swap used before a shared world-origin recenter
internal sealed record GpuRealmRecenterRetirement(
    IReadOnlyList<GpuLandblockSunset> Landblocks,
    int SpatialOperationCount,
    Exception? ObserverFailure);

public enum LandblockSunsetFlavor
{
    Full,
    NearLayer,
}

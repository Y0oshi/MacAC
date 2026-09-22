namespace MacAC.Client.Paging;

internal readonly record struct ResidentPagingWindowFact(
    ulong Revision,
    int CenterX,
    int CenterY,
    int CompleteRadiusLandblocks,
    int PublishedLandblockCount,
    bool HasPublishedCenter)
{
    internal const float LbDimsMeters = 192f;

    internal float CeilingReachMeters
    {
        get
        {
            return HasPublishedCenter
        ? CompleteRadiusLandblocks * LbDimsMeters
        : 0f;
        }
    }

    internal static ResidentPagingWindowFact Unavailable(ulong rev)
    {
        return new(
            rev,
            CenterX: 0,
            CenterY: 0,
            CompleteRadiusLandblocks: 0,
            PublishedLandblockCount: 0,
            HasPublishedCenter: false);
    }
}

using MacAC.Client.Graphics;
using MacAC.Sim;
using MacAC.Sim.Realm;

namespace MacAC.Client.Paging;

internal interface IRealmRevealPagingRota
{
    void OpenDestReservation(
        long unveilGen,
        uint destChamber,
        int neededRasterizeRadius);

    void ConcludeDestReservation(long unveilGen);
}

internal interface IRealmRevealRenderResourceRota
{
    void CommenceDestUnveil(long unveilGen);
    void FinishDestUnveil(long unveilGen);
}

internal sealed partial class RealmRevealMarshal
{
    private sealed class HostMirror(
        SimRealmHarborMirrorTicket ticket,
        int neededRasterizeRadius,
        in RealmEpochQuiescenceEdge stillnessRim)
    {
        public SimRealmHarborMirrorTicket Token { get; } = ticket;

        public int NeededRasterizeRadius { get; set; } = neededRasterizeRadius;
        public RealmEpochQuiescenceEdge StillnessRim { get; } =
            stillnessRim;
        public bool StillnessSealed { get; set; }
        public bool PagingRegistered { get; set; }
        public bool RasterizeAssetListRegistered { get; set; }
        public bool SimulationFreeProjected { get; set; }
        public bool PagingReleased { get; set; }
        public bool RasterizeAssetListReleased { get; set; }
    }

    private readonly SimRealmCrossingLedger _passage;

    private readonly RealmRevealReadinessBarrier _readiness;

    private readonly RealmEpochQuiescence? _stillness;

    private readonly IRealmRevealPagingRota? _paging;

    private readonly IRealmRevealRenderResourceRota? _rasterizeAssetList;

    private readonly List<HostMirror> _hubProjections = [];

    private readonly UnveilTimingSensor? _timing;

    private bool _hubReattemptEngaged;

    private bool _hubReattemptAsked;

    public RealmRevealMarshal(
        SimRealmCrossingLedger transit,
        Func<PagingRevealWindow> unveilPane,
        Func<uint, int, int, bool> isRasterizeNeighborhoodPrimed,
        Func<uint, bool> isSummonChamberPrimed,
        Func<uint, int, bool> isLandNeighborhoodPrimed,
        Func<bool> areCompoundTexturesPrimed,
        Action<uint, int> readyCompoundTextures,
        Action staleCompoundTextures,
        Func<uint, bool> isSummonClaimUnhydratable,
        RealmEpochQuiescence? stillness = null,
        IRealmRevealPagingRota? paging = null,
        IRealmRevealRenderResourceRota? rasterizeAssetList = null,
        Func<int>? fetchedLbTally = null,
        IRenderFrameResourceTelemetrySource? rasterizeAssetTelemetry = null)
    {
        _passage = transit ?? throw new ArgumentNullException(nameof(transit));
        _readiness = new RealmRevealReadinessBarrier(
            unveilPane,
            isRasterizeNeighborhoodPrimed,
            isSummonChamberPrimed,
            isLandNeighborhoodPrimed,
            areCompoundTexturesPrimed,
            readyCompoundTextures,
            staleCompoundTextures,
            isSummonClaimUnhydratable);
        _stillness = stillness;
        _paging = paging;
        _rasterizeAssetList = rasterizeAssetList;
        if (PagingTelemetry.SensorUnveilTiming)
        {
            _timing = new UnveilTimingSensor(
                fetchedLbTally,
                rasterizeAssetTelemetry);
        }
    }
}

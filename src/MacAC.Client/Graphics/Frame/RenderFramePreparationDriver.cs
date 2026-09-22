namespace MacAC.Client.Graphics;

internal interface IRasterizeWeatherCycleStage
{
    void Tick(double diffSecs);
}

internal interface IDevToolsFrameLifespan : IRasterizeCycleMissRecovery
{
    void BeginFrame(float diffSecs);

    void Render(double diffSecs, int viewRectWidth, int viewRectHeight);
}

internal sealed class RenderFramePreparationDriver(
    IRasterizeCycleAssetStage resources,
    IDevToolsFrameLifespan? devTools,
    IRasterizeWeatherCycleStage weather,
    IPrivateActorViewportResourcePreparation? privateViewports = null) : IRasterizeCycleAssetStage
{
    private readonly IRasterizeCycleAssetStage _assetList = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly IDevToolsFrameLifespan? _devTools = devTools;
    private readonly IRasterizeWeatherCycleStage _weather = weather ?? throw new ArgumentNullException(nameof(weather));
    private readonly IPrivateActorViewportResourcePreparation? _privateViewports = privateViewports;

    public void Prepare(RasterizeCycleFeed feed)
    {
        _assetList.Prepare(feed);
        _privateViewports?.ReadyAssetList();
        _devTools?.BeginFrame((float)feed.DeltaSeconds);
        _weather.Tick(feed.DeltaSeconds);
    }
}

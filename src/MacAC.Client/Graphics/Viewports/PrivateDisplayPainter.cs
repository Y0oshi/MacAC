using System.Numerics;
using MacAC.Client.Paging;
using MacAC.Client.Shell;
using MacAC.Client.Telemetry;
using Silk.NET.Input;

namespace MacAC.Client.Graphics;

internal interface IPrivateGatewayViewRect
{
    void Draw(int width, int height);
}

internal interface IPrivateActorViewportFrame
{
    void Render();
}

internal interface IPrivateActorViewportResourcePreparation
{
    void ReadyAssetList();
}

internal interface IRetainedGameplayWidgetFrame
{
    void Render(double diffSecs, int width, int height);
}

internal interface IPrivateCycleScreenshot
{
    bool GrabQueued(int width, int height);
}

internal sealed class PrivateDisplayPainter(
    IPrivateGatewayViewRect portal,
    IRasterizeCycleFoundationOrigin foundation,
    IPrivateActorViewportFrame? actorViewports,
    IRetainedGameplayWidgetFrame? gameplayWidget,
    IDevToolsFrameLifespan? devTools) : IPrivateDisplayFramePhase
{
    private readonly IPrivateGatewayViewRect _gateway = portal ?? throw new ArgumentNullException(nameof(portal));
    private readonly IRasterizeCycleFoundationOrigin _foundation = foundation
            ?? throw new ArgumentNullException(nameof(foundation));
    private readonly IPrivateActorViewportFrame? _actorViewports = actorViewports;
    private readonly IRetainedGameplayWidgetFrame? _gameplayWidget = gameplayWidget;
    private readonly IDevToolsFrameLifespan? _devTools = devTools;

    public PrivateDisplayFrameVerdict Render(
        RasterizeCycleFeed feed,
        RealmRenderFrameVerdict realm)
    {
        _ = realm;
        bool gatewayViewRectShown =
            _foundation.Foundation.PortalViewportVisible;
        _gateway.Draw(feed.ViewportWidth, feed.ViewportHeight);
        _actorViewports?.Render();
        _gameplayWidget?.Render(
            feed.DeltaSeconds,
            feed.ViewportWidth,
            feed.ViewportHeight);
        _devTools?.Render(
            feed.DeltaSeconds,
            feed.ViewportWidth,
            feed.ViewportHeight);
        return new PrivateDisplayFrameVerdict(
            gatewayViewRectShown,
            ScreenshotCaptured: false);
    }
}

internal sealed class PrivateActorViewportFrameGroup(
    params IPrivateActorViewportFrame?[] cycles) :
    IPrivateActorViewportFrame
{
    private readonly IPrivateActorViewportFrame[] _cycles = [.. cycles.Where(static cycle => cycle is not null).Cast<IPrivateActorViewportFrame>()];

    public void Render()
    {
        foreach (IPrivateActorViewportFrame cycle in _cycles)
            cycle.Render();
    }
}

internal sealed class AvatarPortalViewport(
    AvatarWarpDriver teleport,
    CameraDriver camera) : IPrivateGatewayViewRect
{
    private readonly AvatarWarpDriver _warp = teleport ?? throw new ArgumentNullException(nameof(teleport));
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));

    public void Draw(int width, int height) =>
        _warp.SketchGatewayViewRect(width, height, _cam.Active.Projection);
}

internal sealed class RetainedGameplayWidgetFrame(
    CanonWidgetEngine runtime,
    IInputContext? feed) : IRetainedGameplayWidgetFrame
{
    private readonly CanonWidgetEngine _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly IInputContext? _feed = feed;

    public void Render(double diffSecs, int width, int height)
    {
        _runtime.Tick(diffSecs);
        if (_feed is not null)
            _runtime.RefreshCur(_feed.Mice);
        _runtime.Draw(new Vector2(width, height));
    }
}

internal sealed class PrivateCycleScreenshot(FrameScreenshotDriver screenshots) : IPrivateCycleScreenshot
{
    private readonly FrameScreenshotDriver _screenshots = screenshots
            ?? throw new ArgumentNullException(nameof(screenshots));

    public bool GrabQueued(int width, int height) =>
        width > 0 && height > 0 && _screenshots.SnapQueued(width, height);
}

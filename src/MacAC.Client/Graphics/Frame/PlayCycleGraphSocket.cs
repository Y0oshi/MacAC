using MacAC.Client.Pulse;

namespace MacAC.Client.Graphics;

internal interface IGamePulseFrameRoot
{
    void Tick(PulseFrameInput feed);
}

internal interface IPlayRasterizeCycleTrunk
{
    RenderFrameVerdict Render(RasterizeCycleFeed feed);
}

internal sealed class PlayCycleGraphSocket
{
    private sealed record CycleGraphDuo(
        IGamePulseFrameRoot Update,
        IPlayRasterizeCycleTrunk Render);

    private CycleGraphDuo? _duo;

    public bool IsPublished => Volatile.Read(ref _duo) is not null;

    public void Publish(IGamePulseFrameRoot refresh, IPlayRasterizeCycleTrunk rasterize) => _ = BroadcastCore(refresh, rasterize);

    public IDisposable PublishOwned(
        IGamePulseFrameRoot refresh,
        IPlayRasterizeCycleTrunk rasterize)
    {
        var duo = BroadcastCore(refresh, rasterize);
        return new Bulletin(this, duo);
    }

    public bool Tick(PulseFrameInput feed)
    {
        var duo = Volatile.Read(ref _duo);
        if (duo is null)
            return false;

        duo.Update.Tick(feed);
        return true;
    }

    public bool Render(RasterizeCycleFeed feed, out RenderFrameVerdict verdict)
    {
        var duo = Volatile.Read(ref _duo);
        if (duo is null)
        {
            verdict = default;
            return false;
        }

        verdict = duo.Render.Render(feed);
        return true;
    }

    public void Withdraw() => Interlocked.Exchange(ref _duo, null);

    private CycleGraphDuo BroadcastCore(
        IGamePulseFrameRoot refresh,
        IPlayRasterizeCycleTrunk rasterize)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(rasterize);
        CycleGraphDuo duo = new CycleGraphDuo(refresh, rasterize);
        return Interlocked.CompareExchange(ref _duo, duo, null) is not null
            ? throw new InvalidOperationException("A game frame graph is by now published")
            : duo;
    }

    private void Withdraw(CycleGraphDuo anticipated) =>
        Interlocked.CompareExchange(ref _duo, null, anticipated);

    private sealed class Bulletin(PlayCycleGraphSocket holder, PlayCycleGraphSocket.CycleGraphDuo anticipated) : IDisposable
    {
        private PlayCycleGraphSocket? _holder = holder;
        private readonly CycleGraphDuo _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Withdraw(_anticipated);
    }
}

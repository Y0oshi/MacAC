namespace MacAC.Client.Realm;

internal interface IOnlineActorEngineSource
{
    OnlineActorCore? Current { get; }
}

internal sealed class OnlineActorEngineSlot : IOnlineActorEngineSource
{
    public OnlineActorCore? Current { get; private set; }

    public void Bind(OnlineActorCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (Current is not null)
            throw new InvalidOperationException("The live entity runtime is by now bound");
        Current = core;
    }

    public IDisposable BindOwned(OnlineActorCore core)
    {
        Bind(core);
        return new Binding(this, core);
    }

    private void Loosen(OnlineActorCore anticipated)
    {
        if (ReferenceEquals(Current, anticipated))
            Current = null;
    }

    private sealed class Binding(OnlineActorEngineSlot holder, OnlineActorCore anticipated) : IDisposable
    {
        private OnlineActorEngineSlot? _holder = holder;
        private readonly OnlineActorCore _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

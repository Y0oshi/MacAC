namespace MacAC.Client.Shell;

internal sealed class CanonWidgetEngineLease : IDisposable
{
    private object? _hub;
    private Action? _quiesceFeed;
    private Action? _disengageFeed;
    private Action? _teardownHub;
    private Func<bool>? _hubDisposalDone;

    private object? _runtime;
    private Action? _teardownCore;
    private Func<bool>? _coreDisposalDone;
    private bool _feedQuiesced;
    private bool _feedDeactivated;
    private bool _opEngaged;

    public bool IsDisposalComplete { get; private set; }

    public bool IsAbandoned { get; private set; }

    internal bool HasDisposalMiss { get; private set; }

    internal bool RetainsAssetList => _hub is not null || _runtime is not null;

    public WidgetHub AcquireHost(Func<WidgetHub> maker)
    {
        return ObtainHubCore(
        maker,
        static hub => hub.QuiesceInput(),
        static hub => hub.DisarmFeed(),
        static hub => hub.Dispose(),
        static hub => hub.IsDisposalComplete);
    }

    public CanonWidgetEngine Mount(Func<CanonWidgetEngine> maker)
    {
        return AttachCore(
        maker,
        static core => core.BootstrapForTenancy(),
        static core => core.Dispose(),
        static core => core.IsDisposalComplete);
    }

    public void QuiesceFeed()
    {
        if (IsDisposalComplete || IsAbandoned || _feedQuiesced)
            return;
        if (_opEngaged)
            throw new InvalidOperationException("The retained UI lease is changing state");

        _opEngaged = true;
        _feedQuiesced = true;
        try
        {
            _quiesceFeed?.Invoke();
        }
        catch
        {
            _feedQuiesced = false;
            throw;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    public void DisengageFeed()
    {
        if (IsDisposalComplete || IsAbandoned || _feedDeactivated)
            return;
        if (_opEngaged)
            throw new InvalidOperationException("The retained UI lease is changing state");

        _opEngaged = true;
        _feedDeactivated = true;
        try
        {
            _disengageFeed?.Invoke();
        }
        catch
        {
            _feedDeactivated = false;
            throw;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    public void Dispose()
    {
        if (IsDisposalComplete || IsAbandoned)
            return;
        if (_opEngaged)
            throw new InvalidOperationException("The retained UI lease is changing state");

        _opEngaged = true;
        try
        {
            if (_runtime is not null)
            {
                _teardownCore!();
                if (_coreDisposalDone?.Invoke() != true)
                    throw new InvalidOperationException(
                        "The retained UI runtime returned without completing disposal");
                if (_hubDisposalDone?.Invoke() != true)
                    throw new InvalidOperationException(
                        "The retained UI runtime completed without retiring its host");
            }
            else if (_hub is not null)
            {
                _teardownHub!();
                if (_hubDisposalDone?.Invoke() != true)
                    throw new InvalidOperationException(
                        "The retained UI host returned without completing disposal");
            }

            _runtime = null;
            _teardownCore = null;
            _coreDisposalDone = null;
            _hub = null;
            _quiesceFeed = null;
            _disengageFeed = null;
            _teardownHub = null;
            _hubDisposalDone = null;
            HasDisposalMiss = false;
            IsDisposalComplete = true;
        }
        catch
        {
            HasDisposalMiss = true;
            throw;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    public void AbandonFollowingTerminalMiss()
    {
        if (IsDisposalComplete || IsAbandoned)
            return;
        if (_opEngaged)
            throw new InvalidOperationException("The retained UI lease is changing state");
        if (!HasDisposalMiss)
            throw new InvalidOperationException(
                "Retained UI resources may be abandoned only after disposal failed");

        IsAbandoned = true;
    }

    internal T ObtainHubCore<T>(
        Func<T> maker,
        Action<T> quiesceFeed,
        Action<T> disengageFeed,
        Action<T> teardown,
        Func<T, bool> disposalDone)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(maker);
        ArgumentNullException.ThrowIfNull(quiesceFeed);
        ArgumentNullException.ThrowIfNull(disengageFeed);
        ArgumentNullException.ThrowIfNull(teardown);
        ArgumentNullException.ThrowIfNull(disposalDone);
        HurlIfUnavailable();
        if (_opEngaged || _hub is not null)
            throw new InvalidOperationException("A retained UI host is by now owned");

        _opEngaged = true;
        try
        {
            T hub = maker()
                ?? throw new InvalidOperationException("The retained UI host factory returned null");
            _hub = hub;
            _quiesceFeed = () => quiesceFeed(hub);
            _disengageFeed = () => disengageFeed(hub);
            _teardownHub = () => teardown(hub);
            _hubDisposalDone = () => disposalDone(hub);
            return hub;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    internal T AttachCore<T>(
        Func<T> maker,
        Action<T> bootstrap,
        Action<T> teardown,
        Func<T, bool> disposalDone)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(maker);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(teardown);
        ArgumentNullException.ThrowIfNull(disposalDone);
        HurlIfUnavailable();
        if (_opEngaged)
            throw new InvalidOperationException("The retained UI lease is changing state");
        if (_hub is null)
            throw new InvalidOperationException("The retained UI host has to be acquired first");
        if (_runtime is not null)
            throw new InvalidOperationException("A retained UI runtime is by now owned");

        _opEngaged = true;
        T core;
        try
        {
            core = maker()
                ?? throw new InvalidOperationException("The retained UI runtime factory returned null");
        }
        finally
        {
            _opEngaged = false;
        }

        _runtime = core;
        _teardownCore = () => teardown(core);
        _coreDisposalDone = () => disposalDone(core);

        _opEngaged = true;
        try
        {
            bootstrap(core);
            _opEngaged = false;
            return core;
        }
        catch (Exception initializationMiss)
        {
            _opEngaged = false;
            try
            {
                Dispose();
            }
            catch (Exception tidyMiss)
            {
                throw new AggregateException(
                    "Retail UI initialization failed and its published partial runtime didn't cleanly retire",
                    initializationMiss,
                    tidyMiss);
            }

            throw;
        }
    }

    private void HurlIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(IsDisposalComplete, this);
        if (IsAbandoned)
            throw new InvalidOperationException("The retained UI lease was abandoned");
    }
}

namespace MacAC.Client.Graphics;

// Retryable one-owner slot for a disposable runtime resource
internal sealed class PossessedAssetSocket<T>
    where T : class, IDisposable
{
    private T? _asset;
    private bool _opEngaged;

    public bool HasAsset => _asset is not null;

    public T Acquire(Func<T> maker)
    {
        ArgumentNullException.ThrowIfNull(maker);
        if (_opEngaged || _asset is not null)
            throw new InvalidOperationException($"A {typeof(T).Name} is by now owned");

        _opEngaged = true;
        try
        {
            T asset = maker()
                ?? throw new InvalidOperationException($"The {typeof(T).Name} factory returned null");
            _asset = asset;
            return asset;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    public T Borrow()
    {
        return _opEngaged
            ? throw new InvalidOperationException($"The {typeof(T).Name} owner is changing state")
            : _asset
            ?? throw new InvalidOperationException($"No {typeof(T).Name} is currently owned");
    }

    public void Release()
    {
        T? asset = _asset;
        if (asset is null)
            return;
        if (_opEngaged)
            throw new InvalidOperationException($"The {typeof(T).Name} owner is changing state");

        _opEngaged = true;
        try
        {
            asset.Dispose();
            if (ReferenceEquals(_asset, asset))
                _asset = null;
        }
        finally
        {
            _opEngaged = false;
        }
    }

    internal void TransferOwnership(T anticipated)
    {
        ArgumentNullException.ThrowIfNull(anticipated);
        if (_opEngaged)
            throw new InvalidOperationException($"The {typeof(T).Name} owner is changing state");
        if (!ReferenceEquals(_asset, anticipated))
            throw new InvalidOperationException(
                $"The wanted {typeof(T).Name} isn't owned by this slot");
        _asset = null;
    }
}

internal sealed class TransferableAssetSocket<T>
    where T : class, IDisposable
{
    private readonly PossessedAssetSocket<T> _backup = new();
    private bool _opEngaged;
    private bool _readied;

    public bool HasBackup => _backup.HasAsset;

    public T Acquire(Func<T> maker)
    {
        return Perform(() =>
    {
        T asset = _backup.Acquire(maker);
        _readied = true;
        return asset;
    });
    }

    public T AcquirePrepared(Func<T> maker, Action<T> ready)
    {
        return Perform(() =>
    {
        ArgumentNullException.ThrowIfNull(maker);
        ArgumentNullException.ThrowIfNull(ready);

        T asset;
        if (_backup.HasAsset)
        {
            asset = _backup.Borrow();
        }
        else
        {
            asset = _backup.Acquire(maker);
            _readied = false;
        }

        if (!_readied)
        {
            ready(asset);
            _readied = true;
        }

        return asset;
    });
    }

    public T Borrow()
    {
        return _opEngaged
            ? throw new InvalidOperationException("The transferable resource owner is changing state")
            : _backup.Borrow();
    }

    public TOwner Transfer<TOwner>(Func<T, TOwner> destMaker)
        where TOwner : class
    {
        return Perform(() =>
        {
            ArgumentNullException.ThrowIfNull(destMaker);
            if (!_readied)
                throw new InvalidOperationException(
                    "The transferable resource hasn't completed preparation");
            T asset = _backup.Borrow();
            TOwner holder = destMaker(asset)
                ?? throw new InvalidOperationException("The destination owner factory returned null");

            _backup.TransferOwnership(asset);
            _readied = false;
            return holder;
        });
    }

    public void FreeBackup()
    {
        Perform(
        () =>
        {
            _backup.Release();
            if (!_backup.HasAsset)
                _readied = false;
            return true;
        });
    }

    private TResult Perform<TResult>(Func<TResult> op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (_opEngaged)
            throw new InvalidOperationException("The transferable resource owner is changing state");

        _opEngaged = true;
        try
        {
            return op();
        }
        finally
        {
            _opEngaged = false;
        }
    }
}

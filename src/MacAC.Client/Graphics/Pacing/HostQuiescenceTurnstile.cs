namespace MacAC.Client.Graphics;

internal sealed class HostQuiescenceTurnstile
{
    private readonly object _synchronize = new();
    private bool _accepting = true;

    public bool IsAccepting
    {
        get
        {
            lock (_synchronize)
                return _accepting;
        }
    }

    internal bool IsEnteredByLatestThread => Monitor.IsEntered(_synchronize);

    public void HaltAccepting()
    {
        lock (_synchronize)
            _accepting = false;
    }

    public void Invoke(Action hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        lock (_synchronize)
        {
            if (_accepting)
                hook();
        }
    }

    public void Invoke<T>(Action<T> hook, T val)
    {
        ArgumentNullException.ThrowIfNull(hook);
        lock (_synchronize)
        {
            if (_accepting)
                hook(val);
        }
    }
}

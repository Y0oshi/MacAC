namespace MacAC.Sim.Play;

public readonly record struct SimAvatarIdentityHoldingCapture(bool IsDisposed, uint ServerGuid, long Revision)
{
    public bool IsConverged => IsDisposed && ServerGuid is 0u;
}

/// <summary>The local player's server guid, with a revision that ticks on every change.</summary>
public sealed class SimAvatarIdentityLedger : IDisposable
{
    private uint _oid;
    private long _rev;
    private bool _destroyed;

    public uint ServerGuid
    {
        get => _oid;
        set
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (_oid != value)
                Store(value);
        }
    }

    public long Revision => Interlocked.Read(ref _rev);

    public bool IsDisposed => _destroyed;

    public void ResetSession()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ServerGuid = 0u;
    }

    public SimAvatarIdentityHoldingCapture CaptureOwnership() => new(_destroyed, _oid, Revision);

    public void Dispose()
    {
        if (_destroyed)
            return;
        Store(0u);
        _destroyed = true;
    }

    private void Store(uint oid)
    {
        _oid = oid;
        Interlocked.Increment(ref _rev);
    }
}

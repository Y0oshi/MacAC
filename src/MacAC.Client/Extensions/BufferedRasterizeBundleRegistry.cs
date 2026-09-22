using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Extensions;

internal sealed class BufferedRasterizeBundleRegistry : IRenderPackShelf, IDisposable
{
    private readonly object _synchronize = new();
    private readonly Dictionary<string, Enrollment> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private long _rev;
    private long _upcomingEnrollmentIdent;
    private bool _destroyed;

    internal long Rev
    {
        get
        {
            lock (_synchronize)
                return _rev;
        }
    }

    internal event Action<long>? Changed;

    public IDisposable Register(
        RenderPackCard descriptor,
        IRenderPackFiles holdings)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(holdings);

        Enrollment enrollment;
        long rev;
        lock (_synchronize)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (_registrations.ContainsKey(descriptor.Id))
            {
                throw new InvalidOperationException(
                    $"A render pack with id '{descriptor.Id}' is by now registered");
            }

            enrollment = new Enrollment(
                this,
                descriptor,
                holdings,
                checked(++_upcomingEnrollmentIdent));
            _registrations.Add(descriptor.Id, enrollment);
            rev = checked(++_rev);
        }

        BroadcastAltered(rev);
        return enrollment;
    }

    public void Dispose()
    {
        Enrollment[] registrations;
        long? rev = null;
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            registrations = [.. _registrations.Values];
            _registrations.Clear();
            if (registrations.Length is not 0)
                rev = checked(++_rev);
        }

        foreach (Enrollment enrollment in registrations)
            enrollment.WithdrawFromHolder();
        if (rev is { } alteredRev)
            BroadcastAltered(alteredRev);
    }

    internal IReadOnlyList<BufferedRasterizeBundleEnrollment> Freeze()
    {
        lock (_synchronize)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            return _registrations.Values
                .OrderBy(static val => val.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static val => new BufferedRasterizeBundleEnrollment(
                    val.Descriptor,
                    val.Assets,
                    val.EnrollmentIdent))
                .ToArray();
        }
    }

    private void Withdraw(Enrollment enrollment)
    {
        long? rev = null;
        lock (_synchronize)
        {
            if (_registrations.TryGetValue(
                    enrollment.Descriptor.Id,
                    out Enrollment? engaged)
                && ReferenceEquals(engaged, enrollment))
            {
                _registrations.Remove(enrollment.Descriptor.Id);
                rev = checked(++_rev);
            }
        }

        if (rev is { } alteredRev)
            BroadcastAltered(alteredRev);
    }

    private void BroadcastAltered(long rev)
    {
        Delegate[] subscribers = Changed?.GetInvocationList() ?? [];
        foreach (Delegate subscriber in subscribers)
        {
            try { ((Action<long>)subscriber)(rev); }
            catch
            {
            }
        }
    }

    private sealed class Enrollment : IDisposable
    {
        private BufferedRasterizeBundleRegistry? _holder;

        internal Enrollment(
            BufferedRasterizeBundleRegistry holder,
            RenderPackCard descriptor,
            IRenderPackFiles holdings,
            long enrollmentIdent)
        {
            _holder = holder;
            Descriptor = descriptor;
            Assets = holdings;
            EnrollmentIdent = enrollmentIdent;
        }

        internal RenderPackCard Descriptor { get; }

        internal IRenderPackFiles Assets { get; }

        internal long EnrollmentIdent { get; }

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Withdraw(this);

        internal void WithdrawFromHolder() =>
            Interlocked.Exchange(ref _holder, null);
    }
}

internal sealed record BufferedRasterizeBundleEnrollment(
    RenderPackCard Descriptor,
    IRenderPackFiles Assets,
    long RegistrationId);

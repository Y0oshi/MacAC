using MacAC.Client.Graphics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Rigging;

internal sealed class MotionHookRegistrationSet : IDisposable,
    IRetryableAssetTidy
{
    private sealed class Entry(IAnimHookTap drain)
    {
        public IAnimHookTap Drain { get; } = drain;
        public bool Removed { get; set; }
    }

    private readonly Action<IAnimHookTap> _enroll;
    private readonly Action<IAnimHookTap> _withdraw;
    private readonly List<Entry> _listings = [];
    private bool _disposing;
    private bool _closed;

    public MotionHookRegistrationSet(AnimHookRouter router)
        : this(
            (router ?? throw new ArgumentNullException(nameof(router))).Register,
            router.Unregister)
    {
    }

    internal MotionHookRegistrationSet(
        Action<IAnimHookTap> register,
        Action<IAnimHookTap> unregister)
    {
        _enroll = register ?? throw new ArgumentNullException(nameof(register));
        _withdraw = unregister ?? throw new ArgumentNullException(nameof(unregister));
    }

    public bool IsTidyDone =>
        _listings.All(static listing => listing.Removed);

    public int EngagedCount =>
        _listings.Count(static listing => !listing.Removed);

    public void Register(IAnimHookTap drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_listings.Any(listing =>
                !listing.Removed && ReferenceEquals(listing.Drain, drain)))

            return;

        _enroll(drain);
        _listings.Add(new Entry(drain));
    }

    public IDisposable EnrollPossessed(IAnimHookTap drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_listings.Any(listing =>
                !listing.Removed && ReferenceEquals(listing.Drain, drain)))
        {
            throw new InvalidOperationException(
                "The animation-hook sink is by now registered and can't be adopted twice");
        }
        _enroll(drain);
        _listings.Add(new Entry(drain));
        return new Binding(this, drain);
    }

    public void Dispose()
    {
        _closed = true;
        ReattemptTidy();
    }

    public void ReattemptTidy()
    {
        if (_disposing || IsTidyDone)
            return;

        _closed = true;
        _disposing = true;
        List<Exception>? misses = null;
        try
        {
            for (int idx = _listings.Count - 1; idx >= 0; --idx)
            {
                Entry listing = _listings[idx];
                if (listing.Removed)
                    continue;
                try
                {
                    _withdraw(listing.Drain);
                    listing.Removed = true;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new InvalidOperationException(
                        $"Animation-hook sink '{listing.Drain.GetType().Name}' could not be unregistered",
                        miss));
                }
            }
        }
        finally
        {
            _disposing = false;
        }

        if (misses is not null)
            throw new AggregateException(
                "Animation-hook registration cleanup remains incomplete",
                misses);
    }

    private void WithdrawPossessed(IAnimHookTap drain)
    {
        Entry? listing = _listings.LastOrDefault(contender =>
            !contender.Removed && ReferenceEquals(contender.Drain, drain));
        if (listing is null)
            return;
        _withdraw(listing.Drain);
        listing.Removed = true;
    }

    private sealed class Binding(MotionHookRegistrationSet holder, IAnimHookTap drain) : IDisposable
    {
        private MotionHookRegistrationSet? _holder = holder;
        private readonly IAnimHookTap _drain = drain;

        public void Dispose()
        {
            var holder = _holder;
            if (holder is null)
                return;
            holder.WithdrawPossessed(_drain);
            _holder = null;
        }
    }
}

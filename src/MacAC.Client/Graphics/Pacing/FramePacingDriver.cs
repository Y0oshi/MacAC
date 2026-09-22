using System.Diagnostics;

namespace MacAC.Client.Graphics;

internal sealed class FramePacingDriver : IDisposable
{
    private readonly ICyclePacingTimer _clock;
    private readonly ICyclePacingPauser _waiter;
    private readonly bool _ownsWaiter;
    private long _periodBeats;
    private long _upcomingDeadline;
    private bool _hasDeadline;
    private bool _destroyed;

    public FramePacingDriver()
        : this(PlatformFramePacingWaiterMint.ForCurrentProcess())
    {
    }

    internal FramePacingDriver(
        IFramePacingWaiterMint waiterFactory)
        : this(
            StopwatchCyclePacingTimer.Instance,
            (waiterFactory
                ?? throw new ArgumentNullException(nameof(waiterFactory)))
                .Create(),
            ownsWaiter: true)
    {
    }

    internal FramePacingDriver(
        ICyclePacingTimer timer,
        ICyclePacingPauser waiter)
        : this(timer, waiter, ownsWaiter: false)
    {
    }

    internal FramePacingDriver(
        ICyclePacingTimer clock,
        ICyclePacingPauser waiter,
        bool ownsWaiter)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _waiter = waiter ?? throw new ArgumentNullException(nameof(waiter));
        _ownsWaiter = ownsWaiter;
        if (_clock.Frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(clock), "Clock frequency has to be positive");
    }

    internal FramePacingRule Policy { get; private set; }

    public void Apply(FramePacingRule rule)
    {
        if (Policy == rule)
            return;

        Policy = rule;
        if (rule.UseVSync
            || rule.SoftwareLimitHz is not { } thresholdHz
            || !double.IsFinite(thresholdHz)
            || thresholdHz <= 0d)
        {
            _periodBeats = 0;
            _upcomingDeadline = 0;
            _hasDeadline = false;
            return;
        }

        _periodBeats = Math.Max(
            1L,
            checked((long)Math.Round(_clock.Frequency / thresholdHz)));
        _upcomingDeadline = AppendSaturating(_clock.GetTimestamp(), _periodBeats);
        _hasDeadline = true;
    }

    public void FinishCycle()
    {
        if (!_hasDeadline || _periodBeats <= 0)
            return;

        long deadline = _upcomingDeadline;
        long instant = _clock.GetTimestamp();

        if (instant < deadline)
        {
            do
            {
                long leftover = deadline - instant;
                _waiter.Wait(leftover, _clock.Frequency);
                instant = _clock.GetTimestamp();
            }
            while (instant < deadline);

            long followingDeadline = AppendSaturating(deadline, _periodBeats);
            _upcomingDeadline = followingDeadline > instant
                ? followingDeadline
                : AppendSaturating(instant, _periodBeats);
            return;
        }

        _upcomingDeadline = AppendSaturating(instant, _periodBeats);
    }

    private static long AppendSaturating(long val, long increment)
        => val > long.MaxValue - increment
            ? long.MaxValue
            : val + increment;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        if (_ownsWaiter && _waiter is IDisposable disposable)
            disposable.Dispose();
    }
}

internal interface ICyclePacingTimer
{
    long Frequency { get; }

    long GetTimestamp();
}

internal interface ICyclePacingPauser
{
    void Wait(long intervalBeats, long timerFrequency);
}

internal sealed class StopwatchCyclePacingTimer : ICyclePacingTimer
{
    public static StopwatchCyclePacingTimer Instance { get; } = new();

    private StopwatchCyclePacingTimer()
    {
    }

    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();
}

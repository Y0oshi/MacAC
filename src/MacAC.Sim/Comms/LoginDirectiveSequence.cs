using MacAC.Sim.Presence;

namespace MacAC.Sim.Comms;

public readonly record struct LoginDirectiveFailure(int CommandIndex, string Command, string Error);

public sealed class LoginDirectiveSequence
{
    private readonly string[] _commands;
    private readonly TimeSpan _delay;
    private readonly TimeProvider _clock;
    private readonly ICommsDirectiveFeedback _feedback;
    private readonly IDirectiveBus _bus;
    private readonly Action<LoginDirectiveFailure> _onMiss;
    private SimEpochTicket _epoch;
    private SimEpochTicket? _begunFor;
    private long _dueAt;
    private int _cur;
    private bool _running;

    public LoginDirectiveSequence(
        IEnumerable<string?>? directives,
        TimeSpan delay,
        ICommsDirectiveFeedback feedback,
        IDirectiveBus bus,
        Action<LoginDirectiveFailure>? onMiss = null,
        TimeProvider? momentSupplier = null)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(bus);

        _commands = directives?.Select(static c => c ?? string.Empty).ToArray() ?? [];
        _delay = delay;
        _feedback = feedback;
        _bus = bus;
        _onMiss = onMiss ?? (static _ => { });
        _clock = momentSupplier ?? TimeProvider.System;
    }

    public int DirectiveTally => _commands.Length;
    public bool IsActive => _running;
    public int UpcomingDirectiveOrdinal => _cur;

    public void EnteredRealm(SimEpochTicket gen)
    {
        if (_begunFor == gen)
            return;
        _begunFor = gen;
        _epoch = gen;
        _cur = 0;
        _running = _commands.Length > 0;
        _dueAt = _clock.GetTimestamp();
        ExecuteDue(gen, isInRealm: true);
    }

    public void Tick(SimEpochTicket gen, bool isInRealm) => ExecuteDue(gen, isInRealm);

    public void Cancel(SimEpochTicket gen)
    {
        if (_running && _epoch == gen)
            _running = false;
    }

    private void ExecuteDue(SimEpochTicket gen, bool isInRealm)
    {
        if (!_running || !isInRealm || gen != _epoch)
            return;

        long instant = _clock.GetTimestamp();
        while (_running && gen == _epoch && _cur < _commands.Length && instant >= _dueAt)
        {
            Submit(_cur, _commands[_cur]);
            if (!_running || gen != _epoch)
                return;

            ++_cur;
            if (_cur >= _commands.Length)
            {
                _running = false;
                return;
            }
            instant = _clock.GetTimestamp();
            _dueAt = Later(_clock, instant, _delay);
        }
    }

    private void Submit(int ordinal, string directive)
    {
        try
        {
            var upshot = CommsDirectiveRouter.Submit(directive, _feedback, _bus, CommsChannelKind.Say);
            if (upshot is SubmitUpshot.UnknownCommand or SubmitUpshot.Dropped)
                Fail(new LoginDirectiveFailure(ordinal, directive, $"Chat command routing returned {upshot}."));
        }
        catch (Exception problem)
        {
            Fail(new LoginDirectiveFailure(ordinal, directive, problem.GetBaseException().Message));
        }
    }

    private void Fail(LoginDirectiveFailure miss)
    {
        try
        {
            _onMiss(miss);
        }
        catch (Exception)
        {
        }
    }

    // A timestamp interval after stamp, saturating at the maximum
    private static long Later(TimeProvider timer, long stamp, TimeSpan interval)
    {
        double beats = interval.TotalSeconds * timer.TimestampFrequency;
        return beats >= long.MaxValue - stamp ? long.MaxValue : checked(stamp + (long)Math.Ceiling(beats));
    }
}

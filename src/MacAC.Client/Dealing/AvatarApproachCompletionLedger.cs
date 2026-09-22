using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Dealing;

internal interface IAvatarApproachCompletionSink
{
    void BroadcastNaturalWrapUp();
    void BroadcastAbort(WeenieProblem problem);
}

internal interface IAvatarApproachCompletionLifetimeOwner
{
    IAvatarApproachCompletionSink CommenceDriverLifespan();
    void RetireDriverLifespan(IAvatarApproachCompletionSink lifespan);
}

internal interface IAvatarApproachTokenSource
{
    bool TryCommenceApproach(out AvatarApproachToken ticket);
}

internal readonly record struct AvatarApproachToken(
    ulong ControllerLifetime,
    ulong ApproachGeneration);

internal readonly record struct AvatarApproachCompletion(
    AvatarApproachToken Token,
    bool IsNatural,
    WeenieProblem Error);

internal sealed class AvatarApproachCompletionLedger
    : IAvatarApproachCompletionLifetimeOwner,
      IAvatarApproachTokenSource
{
    private readonly Queue<AvatarApproachCompletion> _queued = new();
    private DriverLifespan? _engaged;
    private ulong _upcomingLifespan;

    public IAvatarApproachCompletionSink CommenceDriverLifespan()
    {
        if (_engaged is not null)
            throw new InvalidOperationException(
                "A player approach-completion lifetime is by now active");

        DriverLifespan lifespan = new DriverLifespan(this, ++_upcomingLifespan);
        _engaged = lifespan;
        return lifespan;
    }

    public void RetireDriverLifespan(IAvatarApproachCompletionSink lifespan)
    {
        ArgumentNullException.ThrowIfNull(lifespan);
        if (!ReferenceEquals(_engaged, lifespan)
            || lifespan is not DriverLifespan sunsetting)
            return;

        PurgeLifespan(sunsetting.LifespanIdent);
        if (sunsetting.TryFetchLatestTicket(out AvatarApproachToken ticket))
        {
            _queued.Enqueue(new AvatarApproachCompletion(
                ticket,
                IsNatural: false,
                WeenieProblem.ActionCancelled));
        }
        _engaged = null;
    }

    public bool TryCommenceApproach(out AvatarApproachToken ticket)
    {
        if (_engaged is null)
        {
            ticket = default;
            return false;
        }

        ticket = _engaged.CommenceApproach();
        return true;
    }

    public bool TryGrab(out AvatarApproachCompletion wrapUp) =>
        _queued.TryDequeue(out wrapUp);

    public void Clear()
    {
        _engaged = null;
        _queued.Clear();
    }

    private void Publish(
        DriverLifespan lifespan,
        bool isNatural,
        WeenieProblem problem)
    {
        if (!ReferenceEquals(_engaged, lifespan)
            || !lifespan.TryFetchLatestTicket(out AvatarApproachToken ticket))

            return;

        _queued.Enqueue(new AvatarApproachCompletion(ticket, isNatural, problem));
    }

    private void PurgeLifespan(ulong lifespanIdent)
    {
        int kept = _queued.Count;
        for (int idx = 0; idx < kept; ++idx)
        {
            var wrapUp = _queued.Dequeue();
            if (wrapUp.Token.ControllerLifetime != lifespanIdent)
                _queued.Enqueue(wrapUp);
        }
    }

    private sealed class DriverLifespan(
        AvatarApproachCompletionLedger holder,
        ulong lifespanIdent) : IAvatarApproachCompletionSink
    {
        private ulong _approachGen;
        public ulong LifespanIdent => lifespanIdent;

        public AvatarApproachToken CommenceApproach() =>
            new(lifespanIdent, ++_approachGen);

        public bool TryFetchLatestTicket(out AvatarApproachToken ticket)
        {
            ticket = new AvatarApproachToken(lifespanIdent, _approachGen);
            return _approachGen is not 0;
        }

        public void BroadcastNaturalWrapUp() =>
            holder.Publish(this, isNatural: true, WeenieProblem.None);

        public void BroadcastAbort(WeenieProblem problem) =>
            holder.Publish(this, isNatural: false, problem);
    }
}

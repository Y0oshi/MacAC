using MacAC.Mechanics.Realm;

namespace MacAC.Client.Realm;

internal sealed class CompositeOnlineActorResourceLifespan : IOnlineActorResourceLifespan
{
    internal readonly record struct OwnerDef(
        Action<RealmActor> Register,
        Action<RealmActor> Unregister);

    private sealed class HolderLedger(int tally)
    {
        public bool[] Held { get; } = new bool[tally];
        public bool WantedRegistered { get; set; }
        public bool Reconciling { get; set; }
    }

    private sealed record ReconcileMisses(
        List<Exception> Registration,
        List<Exception> Release);

    private readonly OwnerDef[] _holders;
    private readonly Dictionary<RealmActor, HolderLedger> _phases =
        new(ReferenceEqualityComparer.Instance);

    public CompositeOnlineActorResourceLifespan(params OwnerDef[] owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        if (owners.Length is 0)
            throw new ArgumentException("At least one resource owner is needed", nameof(owners));
        _holders = [.. owners];
    }

    public void Register(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!_phases.TryGetValue(actor, out HolderLedger? phase))
        {
            phase = new HolderLedger(_holders.Length);
            _phases.Add(actor, phase);
        }

        phase.WantedRegistered = true;
        var misses = Reconcile(actor, phase);
        if (misses is null)
            return;

        if (misses.Registration.Count is 1 && misses.Release.Count is 0)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(misses.Registration[0])
                .Throw();

        throw new AggregateException(
            "Live entity resource registration and owner rollback failed",
            misses.Registration.Concat(misses.Release));
    }

    public void Unregister(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!_phases.TryGetValue(actor, out HolderLedger? phase))
            return;

        phase.WantedRegistered = false;
        var misses = Reconcile(actor, phase);
        if (misses is not null)
            throw new AggregateException(
                "One or more live entity resource owners could not release",
                misses.Registration.Concat(misses.Release));
    }

    private ReconcileMisses? Reconcile(RealmActor actor, HolderLedger phase)
    {
        if (phase.Reconciling)
            return null;

        phase.Reconciling = true;
        List<Exception>? enrollmentMisses = null;
        List<Exception>? freeMisses = null;
        try
        {
            while (true)
            {
                bool dirAltered = false;
                if (phase.WantedRegistered)
                {
                    for (int idx = 0; idx < _holders.Length; ++idx)
                    {
                        if (!phase.WantedRegistered)
                        {
                            dirAltered = true;
                            break;
                        }
                        if (phase.Held[idx])
                            continue;

                        phase.Held[idx] = true;
                        try
                        {
                            _holders[idx].Register(actor);
                        }
                        catch (Exception problem)
                        {
                            (enrollmentMisses ??= []).Add(problem);
                            phase.WantedRegistered = false;
                            dirAltered = true;
                            break;
                        }
                    }
                }

                if (!phase.WantedRegistered)
                {
                    for (int idx = _holders.Length - 1; idx >= 0; --idx)
                    {
                        if (phase.WantedRegistered)
                        {
                            dirAltered = true;
                            break;
                        }
                        if (!phase.Held[idx])
                            continue;
                        try
                        {
                            _holders[idx].Unregister(actor);
                            phase.Held[idx] = false;
                        }
                        catch (Exception problem)
                        {
                            (freeMisses ??= []).Add(problem);
                        }
                    }

                    if (phase.WantedRegistered)
                        dirAltered = true;
                }

                if (!dirAltered)
                    break;
            }
        }
        finally
        {
            phase.Reconciling = false;
            if (!phase.WantedRegistered && !phase.Held.Contains(true))
                _phases.Remove(actor);
        }

        return enrollmentMisses is null && freeMisses is null
            ? null
            : new ReconcileMisses(
                enrollmentMisses ?? [],
                freeMisses ?? []);
    }
}

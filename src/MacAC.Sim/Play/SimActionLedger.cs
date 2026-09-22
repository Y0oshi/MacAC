using System.Diagnostics;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Arcana;

namespace MacAC.Sim.Play;

public readonly record struct SimActionHoldingCapture(
    bool IsDisposed,
    bool InternalSubscriptionsAttached,
    bool InteractionTransactionsDisposed,
    bool CombatAttackDisposed,
    bool CombatTargetDisposed,
    bool SpellCastReset,
    uint SelectedObjectId,
    uint PreviousObjectId,
    uint PreviousValidObjectId,
    FightingManner CombatMode,
    int TrackedTargetHealthCount,
    DealingMode DealingMode,
    SimDealingTransactionCapture InteractionTransactions,
    long SelectionRevision,
    long CombatRevision,
    long InteractionRevision)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed && !InternalSubscriptionsAttached
        && InteractionTransactionsDisposed && CombatAttackDisposed && CombatTargetDisposed && SpellCastReset
        && SelectedObjectId is 0u && PreviousObjectId is 0u && PreviousValidObjectId is 0u
        && CombatMode == FightingManner.NonCombat && TrackedTargetHealthCount is 0
        && DealingMode == DealingMode.None && InteractionTransactions.IsConverged;
        }
    }
}

public sealed class SimActionLedger : IDisposable
{
    private readonly Func<double> _instant;
    private readonly Dictionary<uint, (long Revision, double At)> _healthTouched = [];
    private long _healthTouchedRev;
    private long _pickRev;
    private long _fightingRev;
    private long _dealingRev;
    private long _assaultRev;
    private long _castingRev;
    private bool _hooked;
    private bool _destroyed;

    public SimActionLedger(
        PackTransactionState satchelTransactions,
        Grimoire grimoire,
        ISimFightingAttackOps fightingAssaultOps,
        ISimFightingTargetOps fightingMarkOps,
        ISimFightingModeOps fightingMannerOps,
        ISimArcanaCastOps arcanumCastingOps,
        Func<double>? instant = null)
    {
        ArgumentNullException.ThrowIfNull(satchelTransactions);
        ArgumentNullException.ThrowIfNull(grimoire);
        ArgumentNullException.ThrowIfNull(fightingAssaultOps);
        ArgumentNullException.ThrowIfNull(fightingMarkOps);
        ArgumentNullException.ThrowIfNull(fightingMannerOps);
        ArgumentNullException.ThrowIfNull(arcanumCastingOps);
        _instant = instant ?? (static () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

        Selection = new PickPhase();
        Combat = new FightingPhase();
        Interaction = new DealingLedger();
        Transactions = new SimDealingTransactionLedger(satchelTransactions);
        CombatAttack = new SimFightingAttackLedger(Combat, fightingAssaultOps, _instant);
        CombatTarget = new SimFightingTargetLedger(Combat, Selection, fightingMarkOps);
        CombatMode = new SimFightingModeLedger(Combat, fightingMannerOps);
        SpellCast = new SimArcanaCastLedger(grimoire, Selection, arcanumCastingOps);
        View = new Lens(this);

        Selection.Changed += OnPickShift;
        Combat.CombatModeChanged += OnFightingMannerShift;
        Combat.HealthChanged += OnHealthShift;
        Interaction.Changed += OnDealingShift;
        CombatAttack.StateChanged += OnAssaultShift;
        SpellCast.StateChanged += OnCastingShift;
        _hooked = true;
    }

    public PickPhase Selection { get; }
    public FightingPhase Combat { get; }
    public DealingLedger Interaction { get; }
    public SimDealingTransactionLedger Transactions { get; }
    public SimFightingAttackLedger CombatAttack { get; }
    public SimFightingTargetLedger CombatTarget { get; }
    public SimFightingModeLedger CombatMode { get; }
    public SimArcanaCastLedger SpellCast { get; }
    public ISimActionLens View { get; }
    public bool IsDisposed => _destroyed;

    internal event Action? CombatChanged;

    /// <summary>When a target's health was last reported, as a revision and an age in seconds.</summary>
    public bool TryFetchHealthActivity(uint objectIdent, out long rev, out double secsSinceRefresh)
    {
        if (!_healthTouched.TryGetValue(objectIdent, out (long Revision, double At) touch))
        {
            rev = 0;
            secsSinceRefresh = double.PositiveInfinity;
            return false;
        }
        rev = touch.Revision;
        secsSinceRefresh = Math.Max(0d, _instant() - touch.At);
        return true;
    }

    public SimActionHoldingCapture CaptureOwnership()
    {
        return new(
        _destroyed,
        _hooked,
        Transactions.IsDisposed,
        CombatAttack.IsDisposed,
        CombatTarget.IsDisposed,
        SpellCast.PreviousAskedArcanumIdent is null && SpellCast.PreviousAskedMarkIdent is null,
        Selection.ChosenObjectTag ?? 0u,
        Selection.EarlierObjectIdent ?? 0u,
        Selection.EarlierValidObjectIdent ?? 0u,
        Combat.LatestMode,
        Combat.FollowedMarkTally,
        Interaction.Current,
        Transactions.CaptureOwnership(),
        Interlocked.Read(ref _pickRev),
        Interlocked.Read(ref _fightingRev),
        Interlocked.Read(ref _dealingRev));
    }

    public void ResetSession()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        List<Exception>? misses = null;
        RestartFacets(Transactions.ResetSession, ref misses);
        if (misses is not null)
            throw new AggregateException("Runtime action state didn't converge during reset", misses);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        List<Exception>? misses = null;
        try
        {
            RestartFacets(Transactions.Dispose, ref misses);
        }
        finally
        {
            Selection.Changed -= OnPickShift;
            Combat.CombatModeChanged -= OnFightingMannerShift;
            Combat.HealthChanged -= OnHealthShift;
            Interaction.Changed -= OnDealingShift;
            CombatAttack.StateChanged -= OnAssaultShift;
            SpellCast.StateChanged -= OnCastingShift;
            foreach (Action hop in (Action[])[CombatAttack.Dispose, CombatTarget.Dispose])
            {
                try { hop(); }
                catch (Exception problem) { (misses ??= []).Add(problem); }
            }
            _hooked = false;
            _destroyed = true;
        }

        if (misses is not null)
            throw new AggregateException("Runtime action state didn't converge during disposal", misses);
    }

    // Resets every facet, collecting failures so one bad reset does not skip the rest
    private void RestartFacets(Action transactions, ref List<Exception>? misses)
    {
        foreach (Action hop in (Action[])[transactions, Interaction.ResetSession, SpellCast.Reset, CombatAttack.ResetSession, () => Selection.Reset(), Combat.Clear])
        {
            try { hop(); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }
        _healthTouched.Clear();
        _healthTouchedRev = 0;
    }

    private void OnPickShift(PickShift _) => Interlocked.Increment(ref _pickRev);

    private void OnFightingMannerShift(FightingManner _)
    {
        Interlocked.Increment(ref _fightingRev);
        CombatChanged?.Invoke();
    }

    private void OnHealthShift(uint objectIdent, float _)
    {
        _healthTouched[objectIdent] = (++_healthTouchedRev, _instant());
        Interlocked.Increment(ref _fightingRev);
        CombatChanged?.Invoke();
    }

    private void OnDealingShift(DealingModeShift _) => Interlocked.Increment(ref _dealingRev);

    private void OnAssaultShift()
    {
        Interlocked.Increment(ref _assaultRev);
        CombatChanged?.Invoke();
    }

    private void OnCastingShift() => Interlocked.Increment(ref _castingRev);

    private sealed class Lens(SimActionLedger holder) : ISimActionLens
    {
        public SimActionCapture Snapshot
        {
            get
            {
                return new(
            Interlocked.Read(ref holder._pickRev),
            holder.Selection.ChosenObjectTag ?? 0u,
            holder.Selection.EarlierObjectIdent ?? 0u,
            holder.Selection.EarlierValidObjectIdent ?? 0u,
            Interlocked.Read(ref holder._fightingRev),
            holder.Combat.LatestMode,
            holder.Combat.FollowedMarkTally,
            Interlocked.Read(ref holder._dealingRev),
            holder.Interaction.Current.Kind,
            holder.Interaction.Current.SourceObjectId,
            holder.Transactions.CaptureOwnership(),
            new SimFightingAttackCapture(
                Interlocked.Read(ref holder._assaultRev),
                holder.CombatAttack.AskedHeight,
                holder.CombatAttack.WantedStrength,
                holder.CombatAttack.StrengthBarTier,
                holder.CombatAttack.AssembleInHeadway,
                holder.CombatAttack.AssaultReqInHeadway,
                holder.CombatAttack.AskedAssaultStrength,
                holder.CombatAttack.RepeatAssaultInHeadway,
                holder.CombatAttack.AssaultSrvResponseQueued)
            {
                WrapUpRevision = holder.CombatAttack.CompletionRev,
                WrapUpSequence = holder.CombatAttack.CompletionSeries,
                WrapUpWeenieError = holder.CombatAttack.CompletionWeenieProblem,
            },
            new SimArcanaCastCapture(
                Interlocked.Read(ref holder._castingRev),
                holder.SpellCast.PreviousAskedArcanumIdent ?? 0u,
                holder.SpellCast.PreviousAskedMarkIdent ?? 0u));
            }
        }

        public bool TryFetchHealth(uint objectIdent, out float healthPct)
        {
            bool recognized = holder.Combat.HasHealth(objectIdent);
            healthPct = recognized ? holder.Combat.FetchHealthPct(objectIdent) : 0f;
            return recognized;
        }
    }
}

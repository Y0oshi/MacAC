using System.Collections.Concurrent;

namespace MacAC.Mechanics.Fighting;

public sealed class FightingPhase
{
    private readonly ConcurrentDictionary<uint, float> _healthByOid = new();

    public readonly record struct DamageIn(
        string AttackerName,
        uint AttackerGuid,
        uint DamageType,
        uint Damage,
        uint HitQuadrant,
        bool Critical,
        uint AttackType,
        double DamagePercent = 0.0,
        ulong AttackConditions = 0ul);

    public readonly record struct DamageOut(
        string DefenderName,
        uint DamageType,
        uint Damage,
        double DamagePercent,
        bool Critical = false,
        ulong AttackConditions = 0ul);

    public FightingManner LatestMode { get; private set; } = FightingManner.NonCombat;

    public int FollowedMarkTally => _healthByOid.Count;

    public event Action<uint, float>? HealthChanged;

    /// <summary>The player took damage.</summary>
    public event Action<DamageIn>? DamageTaken;

    /// <summary>The player dealt damage.</summary>
    public event Action<DamageOut>? DamageDealtAccepted;

    /// <summary>The player evaded an incoming hit.</summary>
    public event Action<string>? EvadedIncoming;

    /// <summary>The target evaded the player's hit.</summary>
    public event Action<string>? MissedOutgoing;

    public event Action<uint, uint>? AttackDone;

    /// <summary>The server accepted the attack; the power bar and animation may start.</summary>
    public event Action? AttackCommenced;

    public event Action<FightingManner>? CombatModeChanged;

    public event Action<string, uint>? KillLanded;

    /// <summary>Last known health fraction for an object, or 1.0 when unknown.</summary>
    public float FetchHealthPct(uint oid) => _healthByOid.GetValueOrDefault(oid, 1f);

    public bool HasHealth(uint oid) => _healthByOid.ContainsKey(oid);

    public void OnRefreshHealth(uint markOid, float healthPct)
    {
        _healthByOid[markOid] = healthPct;
        HealthChanged?.Invoke(markOid, healthPct);
    }

    public void ApplyFightingManner(FightingManner manner)
    {
        if (LatestMode == manner)
            return;
        LatestMode = manner;
        CombatModeChanged?.Invoke(manner);
    }

    public void OnVictimNotification(
        string attackerLabel, uint attackerOid, uint harmKind, uint harm,
        uint strikeQuadrant, uint critical, uint assaultKind,
        double harmPct = 0.0, ulong assaultConditions = 0ul)
    {
        DamageTaken?.Invoke(new DamageIn(
            attackerLabel, attackerOid, harmKind, harm, strikeQuadrant,
            critical is not 0, assaultKind, harmPct, assaultConditions));
    }

    public void OnDefenderNotification(
        string attackerLabel, uint attackerOid, uint harmKind, uint harm,
        uint strikeQuadrant, uint critical,
        double harmPct = 0.0, ulong assaultConditions = 0ul)
    {
        DamageTaken?.Invoke(new DamageIn(
            attackerLabel, attackerOid, harmKind, harm, strikeQuadrant,
            critical is not 0, AttackType: 0, harmPct, assaultConditions));
    }

    public void OnAttackerNotification(
        string defenderLabel, uint harmKind, uint harm, double harmPct,
        uint critical = 0u, ulong assaultConditions = 0ul)
    {
        DamageDealtAccepted?.Invoke(new DamageOut(
            defenderLabel, harmKind, harm, harmPct, critical is not 0, assaultConditions));
    }

    public void OnEvasionAttackerNotification(string defenderLabel) => MissedOutgoing?.Invoke(defenderLabel);

    public void OnEvasionDefenderNotification(string attackerLabel) => EvadedIncoming?.Invoke(attackerLabel);

    public void OnKillerNotification(string victimLabel, uint victimOid) => KillLanded?.Invoke(victimLabel, victimOid);

    public void OnAssaultDone(uint assaultSeries, uint weenieProblem) => AttackDone?.Invoke(assaultSeries, weenieProblem);

    public void OnFightingCommenceAssault() => AttackCommenced?.Invoke();

    public void Clear()
    {
        _healthByOid.Clear();
        LatestMode = FightingManner.NonCombat;

        if (CombatModeChanged is not { } watchers)
            return;

        ClearRest(watchers);
    }

    private void ClearRest(Action<FightingManner> watchers)
    {
        List<Exception>? misses = null;
        foreach (Action<FightingManner> watcher in watchers.GetInvocationList())
        {
            try
            {
                watcher(FightingManner.NonCombat);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        if (misses is not null)
            throw new AggregateException("One or more combat reset observers failed", misses);
    }
}

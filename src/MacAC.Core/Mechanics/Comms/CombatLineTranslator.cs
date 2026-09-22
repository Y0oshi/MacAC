using MacAC.Mechanics.Fighting;

namespace MacAC.Mechanics.Comms;

/// <summary>Turns combat events into the chat lines the retail client printed.</summary>
public sealed class CombatLineTranslator : IDisposable
{
    private readonly FightingPhase _fighting;
    private readonly ChatTranscript _comms;
    private readonly Func<bool>? _latch;

    private readonly Action<FightingPhase.DamageOut> _dealt;
    private readonly Action<FightingPhase.DamageIn> _taken;
    private readonly Action<string> _missed;
    private readonly Action<string> _evaded;
    private bool _destroyed;

    public CombatLineTranslator(FightingPhase combat, ChatTranscript chat, Func<bool>? accepting = null)
    {
        _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
        _comms = chat ?? throw new ArgumentNullException(nameof(chat));
        _latch = accepting;

        _dealt = e => { if (Open) Dealt(e); };
        _taken = e => { if (Open) Taken(e); };
        _missed = label => { if (Open) Say(FightNoticeText.EvasionAttackerStroke(label), CanonLogTextType.CombatSelf, FightLineKind.Info); };
        _evaded = label => { if (Open) Say(FightNoticeText.EvasionDefenderStroke(label), CanonLogTextType.CombatEnemy, FightLineKind.Info); };

        _fighting.DamageDealtAccepted += _dealt;
        _fighting.DamageTaken += _taken;
        _fighting.MissedOutgoing += _missed;
        _fighting.EvadedIncoming += _evaded;
    }

    private bool Open => _latch?.Invoke() != false;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _fighting.DamageDealtAccepted -= _dealt;
        _fighting.DamageTaken -= _taken;
        _fighting.MissedOutgoing -= _missed;
        _fighting.EvadedIncoming -= _evaded;
    }

    private void Dealt(FightingPhase.DamageOut e)
    {
        Say(
            FightNoticeText.AttackerStroke(e.DefenderName, e.DamageType, e.DamagePercent, e.Damage, e.Critical, e.AttackConditions),
            CanonLogTextType.CombatSelf,
            FightLineKind.Info);
    }

    private void Taken(FightingPhase.DamageIn e)
    {
        Say(
            FightNoticeText.DefenderStroke(
                e.AttackerName, e.DamageType, e.DamagePercent, e.Damage,
                unchecked((int)e.HitQuadrant), e.Critical, e.AttackConditions),
            CanonLogTextType.CombatEnemy,
            FightLineKind.Warning);
    }

    private void Say(string stroke, CanonLogTextType type, FightLineKind sort) =>
        _comms.OnFightingStroke(stroke, (uint)type, sort);
}

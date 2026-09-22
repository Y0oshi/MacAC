using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    private sealed partial class Binder
    {
        public void AttachFighting()
        {
            On(GameEventKind.UpdateHealth, PlaySignals.DecodeRefreshHealth, health => Combat.OnRefreshHealth(health.TargetGuid, health.HealthPercent));
            if (ItemMana is { } gauge)
                On(GameEventKind.QueryItemManaResponse, PlaySignals.DecodeAskGearManaResponse, health => gauge.OnAskGearManaResponse(health.ItemGuid, health.ManaPercent, health.Valid));

            // Death lines: retail logs both the victim's and the killer's under the default text type.
            On(GameEventKind.VictimNotification, PlaySignals.DecodeVictimNotification, health => Chat.OnFightingStroke(health.DeathMessage, tracePhraseKind: 0x00u, sort: FightLineKind.Error));
            On(GameEventKind.KillerNotification, PlaySignals.DecodeKillerNotification, health => Chat.OnFightingStroke(health.DeathMessage, tracePhraseKind: 0x00u, sort: FightLineKind.Info));

            On(GameEventKind.DefenderNotification, PlaySignals.DecodeDefenderNotification, health => Combat.OnDefenderNotification(
                health.AttackerName, 0u, health.DamageType, health.Damage, health.HitQuadrant, health.Critical, health.HealthPercent, health.AttackConditions));
            On(GameEventKind.AttackerNotification, PlaySignals.DecodeAttackerNotification, health => Combat.OnAttackerNotification(
                health.DefenderName, health.DamageType, health.Damage, health.HealthPercent, health.Critical, health.AttackConditions));
            OnRef(GameEventKind.EvasionAttackerNotification, PlaySignals.DecodeEvasionAttackerNotification, Combat.OnEvasionAttackerNotification);
            OnRef(GameEventKind.EvasionDefenderNotification, PlaySignals.DecodeEvasionDefenderNotification, Combat.OnEvasionDefenderNotification);
            On(GameEventKind.AttackDone, PlaySignals.DecodeAssaultDone, health => Combat.OnAssaultDone(health.AttackSequence, health.WeenieError));
            OnBit(GameEventKind.CombatCommenceAttack, PlaySignals.DecodeFightingCommenceAssault, Combat.OnFightingCommenceAssault);
        }
    }
}

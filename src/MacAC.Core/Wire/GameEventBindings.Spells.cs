using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Comms;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    private const uint VitaePenaltyArcanumIdent = 0x29Au;

    private sealed partial class Binder
    {
        public void AttachArcana()
        {
            On(GameEventKind.MagicUpdateSpell, PlaySignals.DecodeMagicRefreshArcanum, ident => Spellbook.OnArcanumLearned(ident));
            On(GameEventKind.MagicRemoveSpell, PlaySignals.DecodeMagicDropArcanum, Spellbook.OnArcanumForgotten);
            On(GameEventKind.MagicUpdateEnchantment, PlaySignals.DecodeMagicRefreshEnchantment, p => Spellbook.OnEnchantmentAdded(ToEngagedEnchantment(p, ClientMoment())));
            OnRef(GameEventKind.MagicUpdateMultipleEnchantments, PlaySignals.DecodeMagicRefreshMultipleEnchantments, listings =>
            {
                double receivedAt = ClientMoment();
                Spellbook.OnEnchantmentsAdded(listings.Select(listing => ToEngagedEnchantment(listing, receivedAt)));
            });
            On(GameEventKind.MagicRemoveEnchantment, PlaySignals.DecodeMagicDropEnchantment, enchantment => Spellbook.OnEnchantmentRemoved(enchantment.Layer, enchantment.SpellId));
            OnRef(GameEventKind.MagicRemoveMultipleEnchantments, PlaySignals.DecodeMagicLayeredArcanumRoster, listings => Spellbook.OnEnchantmentsRemoved(Duos(listings)));
            On(GameEventKind.MagicDispelEnchantment, PlaySignals.DecodeMagicDispelEnchantment, enchantment =>
            {
                Spellbook.OnEnchantmentRemoved(enchantment.Layer, enchantment.SpellId);
                ProclaimExpiry((uint)enchantment.SpellId);
            });
            OnRef(GameEventKind.MagicDispelMultipleEnchantments, PlaySignals.DecodeMagicLayeredArcanumRoster, listings =>
            {
                Spellbook.OnEnchantmentsRemoved(Duos(listings));
                foreach (var entry in listings)
                    ProclaimExpiry((uint)entry.SpellId);
            });
            Raw(GameEventKind.MagicPurgeEnchantments, _ => Spellbook.OnPurgeAll());
            Raw(GameEventKind.MagicPurgeBadEnchantments, _ => Spellbook.OnPurgeBadEnchantments());
        }

        private static IEnumerable<(uint, uint)> Duos(IEnumerable<PlaySignals.StackedSpellId> listings) =>
            listings.Select(gear => ((uint)gear.SpellId, (uint)gear.Layer));

        // "X has expired." for known, non-item spells; vitae reads as a penalty
        private void ProclaimExpiry(uint arcanumIdent)
        {
            if (OnInterfaceText is null || arcanumIdent >= 0x8000 || !Spellbook.TryFetchMetadata(arcanumIdent, out SpellMeta meta))
                return;
            string label = arcanumIdent == VitaePenaltyArcanumIdent ? meta.Name + " penalty" : meta.Name;
            OnInterfaceText($"{label} has expired.", CanonLogTextType.Magic);
        }
    }

    private static LiveEnchantmentRow ToEngagedEnchantment(PlayerDescReader.EnchantmentRow e, double receivedAt)
    {
        return new(
        SpellId: e.SpellId,
        LayerId: e.Layer,
        Duration: e.Duration,
        CasterGuid: e.CasterGuid,
        StatModType: e.StatModType,
        StatModKey: e.StatModKey,
        StatModValue: e.StatModValue,
        Bucket: e.Bucket == 0 ? BinFor(e.StatModType) : (uint)e.Bucket,
        StartTime: receivedAt + e.StartTime,
        SpellCategory: e.SpellCategory,
        PowerLevel: e.PowerLevel,
        DegradeModifier: e.DegradeModifier,
        DegradeLimit: e.DegradeLimit,
        LastTimeDegraded: receivedAt + e.LastTimeDegraded,
        SpellSetId: e.SpellSetId);
    }

    // Retail's enchantment bucket from the stat-mod flags when the wire did not say
    private static uint BinFor(uint statModKind)
    {
        const uint Multiplicative = 0x00004000u;
        const uint Additive = 0x00008000u;
        const uint Vitae = 0x00800000u;
        const uint Cooldown = 0x01000000u;
        if ((statModKind & Vitae) is not 0) return 4u;
        if ((statModKind & Cooldown) is not 0) return 8u;
        if ((statModKind & Multiplicative) is not 0) return 1u;
        return (statModKind & Additive) is not 0 ? 2u : 0u;
    }
}

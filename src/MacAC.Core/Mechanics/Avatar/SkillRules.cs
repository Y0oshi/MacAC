using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Traits;

namespace MacAC.Mechanics.Avatar;

public static class SkillRules
{
    private const uint Specialized = 3u;

    public readonly record struct AugBonuses(
        int AllSkills,
        bool JackOfAllTrades,
        int SkilledSpecialized,
        bool SkilledMelee,
        bool SkilledMissile,
        bool SkilledMagic)
    {
        private const int ClanBonus = 10;
        private const int JackBonus = 5;

        public static AugBonuses FromProps(TraitBundle props)
        {
            ArgumentNullException.ThrowIfNull(props);
            int Int(TraitInt tag) => props.FetchInt((uint)tag);
            return new AugBonuses(
                AllSkills: Math.Max(0, Int(TraitInt.LumAugAllSkills)),
                JackOfAllTrades: Int(TraitInt.AugmentationJackOfAllTrades) > 0,
                SkilledSpecialized: Math.Max(0, Int(TraitInt.LumAugSkilledSpec)),
                SkilledMelee: Int(TraitInt.AugmentationSkilledMelee) > 0,
                SkilledMissile: Int(TraitInt.AugmentationSkilledMissile) > 0,
                SkilledMagic: Int(TraitInt.AugmentationSkilledMagic) > 0);
        }

        public int PriorEnchantments(uint aptitudeIdent)
        {
            bool clan = aptitudeIdent switch
            {
                0x29u or 0x2Cu or 0x2Du or 0x2Eu or 0x31u => SkilledMelee,
                0x2Fu => SkilledMissile,
                0x1Fu or 0x20u or 0x21u or 0x22u or 0x2Bu => SkilledMagic,
                _ => false,
            };
            return Clamped((long)AllSkills + (clan ? ClanBonus : 0));
        }

        public int FollowingEnchantments(uint advancementClass)
        {
            long sum = JackOfAllTrades ? JackBonus : 0;
            if (advancementClass == Specialized)
                sum += Clamped((long)SkilledSpecialized * 2);
            return Clamped(sum);
        }
    }

    public readonly record struct Val(
        int UnenchantedLevel,
        int EffectiveLevel,
        int VitaeModifier);

    public static Val Derive(
        int intrinsicTier,
        int enchantedIntrinsicTier,
        uint aptitudeIdent,
        uint advancementClass,
        AugBonuses augmentations,
        EnchantmentRules.VitalTweak enchantment,
        float vitaeMultiplier)
    {
        int stem = augmentations.PriorEnchantments(aptitudeIdent);
        int unenchanted = Clamped((long)Math.Max(0, intrinsicTier) + stem);

        // InqSkill(…, 0): the same prefix over the enchanted-attribute
        // intrinsic, then EnchantSkill, then the post-enchantment bonuses.
        int enchantedBase = Clamped((long)Math.Max(0, enchantedIntrinsicTier) + stem);
        int enchanted = EnchantmentRules.EnchantAptitude(enchantment, (uint)enchantedBase);
        int net = Clamped((long)enchanted + augmentations.FollowingEnchantments(advancementClass));

        return new Val(
            unenchanted,
            net,
            EnchantmentRules.SkillVitaeModifier(vitaeMultiplier, (uint)unenchanted));
    }

    private static int Clamped(long val) =>
        (int)Math.Clamp(val, int.MinValue, int.MaxValue);
}

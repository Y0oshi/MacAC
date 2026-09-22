namespace MacAC.Extensibility.Automation;

public interface IAutomationCockpit
{
    bool IsAvailable { get; }

    ISelfSheet Character { get; }

    ISpellbook Spells { get; }

    ICastingControls Magic { get; }

    IChatControls Chat { get; }

    ICombatControls Combat => Idle.Combat;

    IEquipmentControls Equipment => Idle.Equipment;

    IItemControls Items => Idle.Items;

    ILootControls Loot => Idle.Loot;

    IFellowshipControls Fellowship => Idle.Fellowship;

    IEnchantmentControls Enchantments => Idle.Enchantments;

    INavigationControls Navigation => Idle.Navigation;

    IWorldObjectControls Objects => Idle.Objects;

    IWorldTimeControls RealmMoment => Idle.WorldTime;

    ILoginControls Login => Idle.Login;

    INetworkControls Network => Idle.Network;

    IRecoveryControls Recovery => Idle.Recovery;

    IProjectileControls Missiles => Idle.Projectiles;

    ISelectionControls Selection => Idle.Selection;
}

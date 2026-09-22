namespace MacAC.Extensibility.Automation;

public sealed class IdleCockpit : IAutomationCockpit
{
    public static IdleCockpit Instance { get; } = new();

    private IdleCockpit()
    {
    }

    public bool IsAvailable => false;

    public ISelfSheet Character => Idle.Self;

    public ISpellbook Spells => Idle.Spells;

    public ICastingControls Magic => Idle.Casting;

    public IChatControls Chat => Idle.Chat;
}

/// <summary>Inert singletons, one per control surface.</summary>
public static class Idle
{
    public static ISelfSheet Self { get; } = new IdleSelf();

    public static ISpellbook Spells { get; } = new IdleSpellbook();

    public static ICastingControls Casting { get; } = new IdleCasting();

    public static IChatControls Chat { get; } = new IdleChat();

    public static ICombatControls Combat { get; } = new IdleCombat();

    public static IEquipmentControls Equipment { get; } = new Quiet();

    public static IItemControls Items { get; } = new Quiet();

    public static ILootControls Loot { get; } = new Quiet();

    public static IFellowshipControls Fellowship { get; } = new Quiet();

    public static IEnchantmentControls Enchantments { get; } = new Quiet();

    public static INavigationControls Navigation { get; } = new IdleNavigation();

    public static IWorldObjectControls Objects { get; } = new Quiet();

    public static IWorldTimeControls WorldTime { get; } = new Quiet();

    public static ILoginControls Login { get; } = new Quiet();

    public static INetworkControls Network { get; } = new Quiet();

    public static IRecoveryControls Recovery { get; } = new Quiet();

    public static IProjectileControls Projectiles { get; } = new Quiet();

    public static ISelectionControls Selection { get; } = new Quiet();

    // Surfaces whose every member already has an inert default
    private sealed class Quiet
        : IEquipmentControls, IItemControls, ILootControls, IFellowshipControls,
          IEnchantmentControls, IWorldObjectControls, IWorldTimeControls,
          ILoginControls, INetworkControls, IRecoveryControls,
          IProjectileControls, ISelectionControls
    {
    }

    private sealed class IdleSelf : ISelfSheet
    {
        public bool IsInWorld => false;

        public uint ObjectId => 0;

        public uint LatestHealth => 0;

        public uint MaxHealth => 0;

        public uint LatestStamina => 0;

        public uint UpperStamina => 0;

        public uint LatestMana => 0;

        public uint UpperMana => 0;

        public IReadOnlyList<SkillFacts> Skills => [];

        public IReadOnlyList<AttributeFacts> Attributes => [];

        public IReadOnlyList<ActiveEnchantmentFacts> EngagedEnchantments => [];

        public bool TryFetchAptitude(uint aptitudeIdent, out SkillFacts aptitude)
        {
            aptitude = default;
            return false;
        }
    }

    private sealed class IdleSpellbook : ISpellbook
    {
        public IReadOnlyList<SpellFacts> RecognizedSelfBuffs => [];

        public bool TryGet(uint arcanumIdent, out SpellFacts details)
        {
            details = default;
            return false;
        }
    }

    private sealed class IdleCasting : ICastingControls
    {
        public bool IsCasting => false;

        public CastGate EvaluateGate(uint arcanumIdent) => CastGate.Unavailable;

        public CastGate EvaluateGate(uint arcanumIdent, uint markObjectIdent) =>
            CastGate.Unavailable;

        public bool Cast(uint arcanumIdent) => false;

        public bool Cast(uint arcanumIdent, uint markObjectIdent) => false;
    }

    private sealed class IdleChat : IChatControls
    {
        public void PostSysMsg(string phrase)
        {
        }
    }

    private sealed class IdleCombat : ICombatControls
    {
        private static CombatVerdict Unavailable => new(CombatOutcome.Unavailable);

        public CombatFrame Snapshot => default;

        public IReadOnlyList<HostileEntry> GrabHostileMarks(float ceilingGap) => [];

        public CombatVerdict JoinDefaultManner() => Unavailable;

        public CombatVerdict CommencePhysicalAssault(
            uint markObjectIdent,
            StrikeHeight height,
            float strength) => Unavailable;

        public CombatVerdict FreePhysicalAssault() => Unavailable;

        public CombatVerdict CancelPhysicalAssault() => Unavailable;
    }

    private sealed class IdleNavigation : INavigationControls
    {
        public NavigationFrame Snapshot => default;

        public bool TryFetchObject(uint objectIdent, out NavigationEntry val)
        {
            val = default;
            return false;
        }

        public NavigationOutcome AssignTravelIntent(in MovementIntent intent) =>
            NavigationOutcome.Unavailable;

        public NavigationOutcome WipeTravelIntent() =>
            NavigationOutcome.Unavailable;
    }
}

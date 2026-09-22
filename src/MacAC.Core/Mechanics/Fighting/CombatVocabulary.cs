namespace MacAC.Mechanics.Fighting;

[Flags]
public enum FightingManner
{
    Undef = 0,
    NonCombat = 0x01,
    Melee = 0x02,
    Missile = 0x04,
    Magic = 0x08,

    ValidCombat = NonCombat | Melee | Missile | Magic,
    CombatCombat = Melee | Missile | Magic,
}

public enum AssaultElevation
{
    Undef = 0,
    High = 1,
    Medium = 2,
    Low = 3,
}

public enum StrikeAction
{
    Low,
    Medium,
    High,
}

[Flags]
public enum AssaultKind : uint
{
    None = 0,
    Punch = 0x0001,
    Thrust = 0x0002,
    Slash = 0x0004,
    Kick = 0x0008,
    OffhandPunch = 0x0010,
    DoubleSlash = 0x0020,
    TripleSlash = 0x0040,
    DoubleThrust = 0x0080,
    TripleThrust = 0x0100,
    OffhandThrust = 0x0200,
    OffhandSlash = 0x0400,
    OffhandDoubleSlash = 0x0800,
    OffhandTripleSlash = 0x1000,
    OffhandDoubleThrust = 0x2000,
    OffhandTripleThrust = 0x4000,
    Unarmed = Punch | Kick | OffhandPunch,
    MultiStrike = DoubleSlash | TripleSlash | DoubleThrust | TripleThrust
        | OffhandDoubleSlash | OffhandTripleSlash
        | OffhandDoubleThrust | OffhandTripleThrust,
}

[Flags]
public enum HarmKind : uint
{
    Undef = 0,
    Slash = 0x0001,
    Pierce = 0x0002,
    Bludgeon = 0x0004,
    Cold = 0x0008,
    Fire = 0x0010,
    Acid = 0x0020,
    Electric = 0x0040,
    Health = 0x0080,
    Stamina = 0x0100,
    Mana = 0x0200,
    Nether = 0x0400,
    Base = 0x10000000,
}

public enum BodyZone
{
    Head = 0,
    Chest = 1,
    Abdomen = 2,
    UpperArm = 3,
    LowerArm = 4,
    Hand = 5,
    UpperLeg = 6,
    LowerLeg = 7,
    Foot = 8,
}

public enum FightLineKind
{
    /// <summary>Outgoing damage or a target evasion; yellowish in the panel.</summary>
    Info,

    /// <summary>Incoming damage; reddish in the panel.</summary>
    Warning,

    /// <summary>An AttackDone with a non-zero WeenieError; deep red in the panel.</summary>
    Error,
}

public readonly record struct DamageTick(
    uint AttackerGuid,
    uint TargetGuid,
    AssaultKind AttackType,
    HarmKind DamageType,
    BodyZone BodyZone,
    int DamageOut,
    int PostResistDamage,
    bool WasCritical,
    bool WasEvaded,
    bool WasResisted,
    float AccuracyModUsed,
    float PowerModUsed);

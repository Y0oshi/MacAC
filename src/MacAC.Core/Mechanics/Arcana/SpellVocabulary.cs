namespace MacAC.Mechanics.Arcana;

public enum MechMagicSchool : uint
{
    None = 0,
    WarMagic = 1,
    LifeMagic = 2,
    ItemEnchantment = 3,
    CreatureEnchantment = 4,
    VoidMagic = 5,
}

public enum SpellTargetSort : uint
{
    None = 0,
    Self = 1,
    Item = 2,
    Creature = 3,
    Object = 4,
    SelfOrItem = 5,
    Undef = 6,
    /// <summary>A targeted item that is not your own.</summary>
    OtherItem = 7,
}

public enum ArcanumBucket : uint
{
    Undef = 0,
}

[Flags]
public enum SpellBits : uint
{
    None = 0,
    Resistable = 0x00000001,
    PKSensitive = 0x00000002,
    Beneficial = 0x00000004,
    SelfTargeted = 0x00000008,
    Reversed = 0x00000010,
    NotIndoors = 0x00000020,
    NotOutdoors = 0x00000040,
    NotResearchable = 0x00000080,
    Projectile = 0x00000100,
    CreatureSpell = 0x00000200,
    ExcludedFromItemDescriptions = 0x00000400,
    IgnoresManaConversion = 0x00000800,
    NonTrackingProjectile = 0x00001000,
    FellowshipSpell = 0x00002000,
    FastCast = 0x00004000,
    IndoorLongRange = 0x00008000,
    DamageOverTime = 0x00010000,
}

/// <summary>A spell as the portal DAT describes it.</summary>
public sealed class SpellDatRow
{
    public uint SpellId { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public MechMagicSchool School { get; init; }

    /// <summary>Difficulty / mastery.</summary>
    public int Power { get; init; }

    public float CastingMoment { get; init; }

    public float Duration { get; init; }

    public int BaseMana { get; init; }

    /// <summary>Extra mana per additional target.</summary>
    public int ManaMod { get; init; }

    public int ManaConversionBase { get; init; }

    public float ManaConversionMod { get; init; }

    public SpellTargetSort TargetType { get; init; }

    public ArcanumBucket Category { get; init; }

    public SpellBits Flags { get; init; }

    public IReadOnlyList<int> EquationComponentIds { get; init; } = [];

    public int SpanConstant { get; init; }

    public int EconomyMod { get; init; }

    /// <summary>0x06xxxxxx.</summary>
    public uint Icon { get; init; }

    public uint ArcanumStatModTag { get; init; }

    public int ArcanumStatModValue { get; init; }
}

public sealed class SpellComponentRow
{
    public int ModuleIdent { get; init; }

    public string Name { get; init; } = "";

    public uint Icon { get; init; }

    public double CdmBonus { get; init; }

    public double ManaMod { get; init; }

    /// <summary>1 = scarab, 2 = herb, 3 = talisman, 4 = taper, 5 = pwax.</summary>
    public int Type { get; init; }
}

public enum CastPhase
{
    Idle,
    Preparing,
    Casting,
    Releasing,
    Fizzled,
    Complete,
}

/// <summary>Where one cast is, from request to completion or fizzle.</summary>
public sealed class CastMachine
{
    public CastPhase Phase { get; private set; } = CastPhase.Idle;

    public uint SpellId { get; private set; }

    public uint? MarkOid { get; private set; }

    public double BegunAt { get; private set; }

    public double CastingInterval { get; private set; }

    public event Action<CastMachine>? OnPhaseChanged;

    public void CommenceCasting(uint arcanumIdent, uint? markOid, double castingIntervalSec, double instantSec)
    {
        SpellId = arcanumIdent;
        MarkOid = markOid;
        BegunAt = instantSec;
        CastingInterval = castingIntervalSec;
        Enter(CastPhase.Preparing);
    }

    public void SrvAckCastingBegin() => Enter(CastPhase.Casting);

    public void SrvFreeCasting() => Enter(CastPhase.Releasing);

    public void SrvDoneCasting() => Enter(CastPhase.Complete);

    public void SrvFizzle() => Enter(CastPhase.Fizzled);

    private void Enter(CastPhase stage)
    {
        Phase = stage;
        OnPhaseChanged?.Invoke(this);
    }
}

public sealed class LiveBuff
{
    public uint SpellId { get; init; }

    public uint InvokerOid { get; init; }

    public ArcanumBucket Category { get; init; }

    public int Power { get; init; }

    public double StartedAt { get; init; }

    public double Duration { get; init; }

    public double EndsAt => StartedAt + Duration;

    public int StatModTag { get; init; }

    public int StatModVal { get; init; }
}

public static class SpellRules
{
    public static double ChanceOfSuccess(int aptitude, int difficulty)
    {
        if (aptitude < difficulty - 50)
            return 0.0;
        return 1.0 / (1.0 + Math.Exp(-0.07 * (aptitude - difficulty)));
    }

    /// <summary>Two mana-conversion rolls, each halving the cost: one at half difficulty, one at full.</summary>
    public static int CalculateManaPrice(SpellDatRow arcanum, int countMarks, int manaConvAptitude, Random rng)
    {
        double price = arcanum.BaseMana + arcanum.ManaMod * Math.Max(0, countMarks - 1);
        if (rng.NextDouble() < ChanceOfSuccess(manaConvAptitude, arcanum.Power / 2))
            price *= 0.5;
        if (rng.NextDouble() < ChanceOfSuccess(manaConvAptitude, arcanum.Power))
            price *= 0.5;
        return (int)Math.Max(1, Math.Round(price));
    }
}

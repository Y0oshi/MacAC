namespace MacAC.Client.Shell.Panels;

public sealed class ToonSheet
{

    public string Name { get; init; } = string.Empty;

    public int? Level { get; init; }

    public string? Gender { get; init; }

    public string? Race { get; init; }

    public string? Heritage { get; init; }

    public string? Title { get; init; }

    public long SumXp { get; init; }

    public long XpToUpcomingTier { get; init; }

    public float XpRatio { get; init; }

    public string? PkCondition { get; init; }

    public long AvailableLuminance { get; init; }

    public long MaximumLuminance { get; init; }

    public string? BirthDate { get; init; }

    public string? PlayMoment { get; init; }

    public int Deaths { get; init; }

    public int? BirthStamp { get; init; }

    public int? SumPlayMomentSecs { get; init; }

    public int HealthCurrent { get; init; }
    public int HealthMax { get; init; }
    public int StaminaLatest { get; init; }
    public int StaminaUpper { get; init; }
    public int ManaCurrent { get; init; }
    public int ManaMax { get; init; }

    /// <summary>Unenchanted max Health/Stamina/Mana in that order.</summary>
    public int[] VitalBaseUpperVals { get; init; } = [];

    public int[] VitalVitaeModifiers { get; init; } = [];

    public int Strength { get; init; }
    public int Endurance { get; init; }
    public int Quickness { get; init; }
    public int Coordination { get; init; }
    public int Focus { get; init; }
    public int Self { get; init; }

    public int[] AttrBaseVals { get; init; } = [];

    public int UnspentAptitudeCredits { get; init; }
    public int SpecializedAptitudeCredits { get; init; }

    public int ChessRank { get; init; }

    public int FishingAptitude { get; init; }

    public int AptitudeCredits { get; init; }

    public bool ExpectingEmit { get; init; }

    public long UnassignedXp { get; init; }

    public long[] AttrEmitPrices { get; init; } = [];

    public long[] AttrRaise10Prices { get; init; } = [];

    public IReadOnlyList<ToonSkill> Skills { get; init; } = Array.Empty<ToonSkill>();

    public string? AugmentationLabel { get; init; }

    public IReadOnlyDictionary<uint, int> ToonDetailsProps { get; init; }
        = new Dictionary<uint, int>();

    public int BurdenLatest { get; init; }
    public int BurdenUpper { get; init; }
    public int EncumbranceAugmentations { get; init; }
}

public enum ToonSkillAdvancementClass
{
    Inactive = 0,
    Untrained = 1,
    Trained = 2,
    Specialized = 3,
}

public sealed record ToonSkill(
    uint Id,
    string Name,
    uint IconDid,
    ToonSkillAdvancementClass AdvancementClass,
    int BaseLevel,
    int CurrentLevel,
    bool UsableUntrained,
    int TrainedCost,
    int SpecializedCost,
    long RaiseCost,
    long Raise10Cost = 0L,
    int VitaeModifier = 0,
    string? TooltipText = null);

namespace MacAC.Client.Shell.Panels;

public static class SpecimenBlob
{
    public static ToonSheet SampleCharacter() => SampleCharacter(null);

    public static ToonSheet SampleCharacter(string? label)
    {
        return new()
        {
            Name = string.IsNullOrWhiteSpace(label) ? "Studio Player" : label,
            Level = 126,
            Gender = "Female",
            Heritage = "Aluvian",
            Title = "the Adventurer",
            BirthDate = "January 5, 2001",
            PlayMoment = "2 years, 114 days, 4 hours",
            Deaths = 42,

            PkCondition = "Non-Player Killer",
            SumXp = 1_250_000_000,
            XpToUpcomingTier = 42_000_000,
            XpRatio = 0.63f,

            HealthCurrent = 5,
            HealthMax = 5,
            StaminaLatest = 10,
            StaminaUpper = 10,
            ManaCurrent = 10,
            ManaMax = 10,

            Strength = 200,
            Endurance = 10,
            Quickness = 200,
            Coordination = 10,
            Focus = 10,
            Self = 10,

            UnspentAptitudeCredits = 12,
            SpecializedAptitudeCredits = 4,
            ChessRank = 12,
            FishingAptitude = 4,

            AptitudeCredits = 96,

            UnassignedXp = 87_757_321_741L,

            AttrEmitPrices = [0L, 95L, 100L, 0L, 110L, 105L, 90L, 88L, 112L],
            AttrRaise10Prices = [0L, 950L, 1_000L, 0L, 1_100L, 1_050L, 900L, 880L, 1_120L],

            Skills = new ToonSkill[]
        {
            new( 6, "Melee Defense",   0x06000165u, ToonSkillAdvancementClass.Specialized, 350, 354, false, 10, 20, 18_250_000L, 182_500_000L),
            new(34, "War Magic",       0x06001365u, ToonSkillAdvancementClass.Specialized, 280, 285, false, 16, 28, 11_100_000L, 111_000_000L),

            new(14, "Arcane Lore",     0x0600016Eu, ToonSkillAdvancementClass.Trained,     260, 269, false,  4,  6,  7_500_000L,  75_000_000L),
            new(33, "Life Magic",      0x06001364u, ToonSkillAdvancementClass.Trained,     250, 252, false, 12, 20,  6_800_000L,  68_000_000L),
            new(47, "Missile Weapons", 0x0600015Fu, ToonSkillAdvancementClass.Trained,     220, 221, false,  6, 12,  5_250_000L,  52_500_000L),

            new(21, "Healing",         0x06000133u, ToonSkillAdvancementClass.Untrained,    10,  10, true,   6, 10, 0L),
            new(22, "Jump",            0x0600016Bu, ToonSkillAdvancementClass.Untrained,   210, 210, true,   0,  4, 0L),
            new(36, "Loyalty",         0x06001367u, ToonSkillAdvancementClass.Untrained,    10,  10, true,   0,  2, 0L),
            new(24, "Run",             0x06000173u, ToonSkillAdvancementClass.Untrained,   390, 390, true,   0,  4, 0L),

            new(38, "Alchemy",         0x060019E4u, ToonSkillAdvancementClass.Untrained,    10,  10, false,  6, 12, 0L),
            new(39, "Cooking",         0x06001A54u, ToonSkillAdvancementClass.Untrained,    10,  10, false,  4,  8, 0L),
            new(37, "Fletching",       0x06001A55u, ToonSkillAdvancementClass.Untrained,    10,  10, false,  4,  8, 0L),
        },

            ToonDetailsProps = new Dictionary<uint, int>
            {
                [0x162u] = 2, // Swords melee mastery
            },

            BurdenLatest = 1200,
            BurdenUpper = 4500,
        };
    }
}

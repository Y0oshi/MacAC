using System.Globalization;
using System.Text;

namespace MacAC.Mechanics.Fighting;

/// <summary>The retail damage verbs: four severities per damage type.</summary>
public static class HitAdjectives
{
    public const double LampThreshold = 0.1;
    public const double MediumThreshold = 0.25;
    public const double HeavyThreshold = 0.5;

    public const uint Slash = 0x1u;
    public const uint Pierce = 0x2u;
    public const uint Bludgeon = 0x4u;
    public const uint Cold = 0x8u;
    public const uint Fire = 0x10u;
    public const uint Acid = 0x20u;
    public const uint Electric = 0x40u;
    public const uint Health = 0x80u;
    public const uint Stamina = 0x100u;
    public const uint Mana = 0x200u;
    public const uint Nether = 0x400u;
    public const uint Base = 0x10000000u;

    private readonly record struct Verb(string First, string Third)
    {
        public static Verb Regular(string stem) => new(stem, stem + "s");
    }

    private static readonly Dictionary<uint, Verb[]> Ladders = new()
    {
        [Slash] = [new("scratch", "scratches"), new("cut", "cuts"), new("slash", "slashes"), new("mangle", "mangles")],
        [Pierce] = [Verb.Regular("nick"), Verb.Regular("stab"), Verb.Regular("impale"), Verb.Regular("gore")],
        [Bludgeon] = [new("graze", "grazes"), new("bash", "bashes"), new("smash", "smashes"), new("crush", "crushes")],
        [Cold] = [Verb.Regular("numb"), Verb.Regular("chill"), Verb.Regular("frost"), Verb.Regular("freeze")],
        [Fire] = [new("singe", "singes"), new("scorch", "scorches"), new("burn", "burns"), new("incinerate", "incinerates")],
        [Acid] = [Verb.Regular("blister"), Verb.Regular("sear"), Verb.Regular("corrode"), Verb.Regular("dissolve")],
        [Electric] = [new("spark", "sparks"), new("shock", "shocks"), new("jolt", "jolts"), new("blast", "blasts")],
        [Health] = [Verb.Regular("drain"), Verb.Regular("exhaust"), Verb.Regular("siphon"), Verb.Regular("deplete")],
        [Nether] = [Verb.Regular("scar"), Verb.Regular("twist"), Verb.Regular("wither"), Verb.Regular("eradicate")],
    };

    public static bool Inq(uint harmKind, double pct, out string verb, out string verbThirdPerson)
    {
        // NaN and negatives are not a hit at all
        if (!(pct >= 0.0))
        {
            verb = string.Empty;
            verbThirdPerson = string.Empty;
            return false;
        }

        if (!Ladders.TryGetValue(harmKind, out Verb[]? ladder))
        {
            verb = "hit";
            verbThirdPerson = "hits";
            return false;
        }

        Verb choose = ladder[Rung(pct)];
        verb = choose.First;
        verbThirdPerson = choose.Third;
        return true;
    }

    private static int Rung(double pct)
    {
        if (!(pct > LampThreshold)) return 0;
        if (!(pct > MediumThreshold)) return 1;
        return !(pct > HeavyThreshold) ? 2 : 3;
    }
}

public static class DamageKindText
{
    private static readonly (uint Bit, string Name)[] Names =
    [
        (HitAdjectives.Slash, "Slashing"),
        (HitAdjectives.Pierce, "Piercing"),
        (HitAdjectives.Bludgeon, "Bludgeoning"),
        (HitAdjectives.Cold, "Cold"),
        (HitAdjectives.Fire, "Fire"),
        (HitAdjectives.Acid, "Acid"),
        (HitAdjectives.Electric, "Electrical"),
        (HitAdjectives.Nether, "Nether"),
        (HitAdjectives.Base, "Prismatic"),
    ];

    /// <summary>"Slashing/Fire" style names for every bit set.</summary>
    public static string Describe(uint harmKind)
    {
        StringBuilder phrase = new StringBuilder(24);
        foreach ((uint bit, string label) in Names)
        {
            if ((harmKind & bit) is 0)
                continue;
            if (phrase.Length is not 0)
                phrase.Append('/');
            phrase.Append(label);
        }
        return phrase.ToString();
    }
}

public static class BodyZoneText
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [-1] = "UNDEFINED",
        [0] = "HEAD",
        [1] = "CHEST",
        [2] = "ABDOMEN",
        [3] = "UPPER_ARM",
        [4] = "LOWER_ARM",
        [5] = "HAND",
        [6] = "UPPER_LEG",
        [7] = "LOWER_LEG",
        [8] = "FOOT",
        [9] = "HORN",
        [10] = "FRONT_LEG",
        [12] = "FRONT_FOOT",
        [13] = "REAR_LEG",
        [15] = "REAR_FOOT",
        [16] = "TORSO",
        [17] = "TAIL",
        [18] = "ARM",
        [19] = "LEG",
        [20] = "CLAW",
        [21] = "WINGS",
        [22] = "BREATH",
        [23] = "TENTACLE",
        [24] = "UPPER_TENTACLE",
        [25] = "LOWER_TENTACLE",
        [26] = "CLOAK",
        [27] = "NUM",
    };

    public static string Describe(int corpusPiece) => Names.GetValueOrDefault(corpusPiece, "Unknown");

    public static string ToReadout(string raw)
    {
        return string.IsNullOrEmpty(raw) ? string.Empty : raw.Replace('_', ' ').ToLowerInvariant();
    }

    public static string DepictForReadout(int corpusPiece) => ToReadout(Describe(corpusPiece));
}

/// <summary>The chat lines the retail client printed for hits, misses and evasions.</summary>
public static class FightNoticeText
{
    public const ulong CriticalProtectionAugmentation = 0x1ul;
    public const ulong Recklessness = 0x2ul;
    public const ulong SneakAssault = 0x4ul;

    public static string AttackerStroke(
        string defenderLabel, uint harmKind, double pct,
        uint harm, bool critical, ulong assaultConditions)
    {
        HitAdjectives.Inq(harmKind, pct, out string verb, out _);

        StringBuilder stroke = new StringBuilder(96);
        if (critical)
            stroke.Append("Critical hit!  ");
        if ((assaultConditions & SneakAssault) is not 0)
            stroke.Append("Sneak Attack! ");
        if ((assaultConditions & Recklessness) is not 0)
            stroke.Append("Recklessness! ");

        stroke.Append("You ").Append(verb).Append(' ').Append(defenderLabel);
        Pts(stroke, harm, harmKind);

        if ((assaultConditions & CriticalProtectionAugmentation) is not 0)
            stroke.Append(" Your target's Critical Protection augmentation allows them to avoid your critical hit!");

        return stroke.ToString();
    }

    public static string DefenderStroke(
        string attackerLabel, uint harmKind, double pct, uint harm,
        int corpusPiece, bool critical, ulong assaultConditions)
    {
        return DefenderStrokeForCorpusPiecePhrase(
            attackerLabel, harmKind, pct, harm,
            BodyZoneText.DepictForReadout(corpusPiece), critical, assaultConditions);
    }

    public static string DefenderStrokeForCorpusPiecePhrase(
        string attackerLabel, uint harmKind, double pct, uint harm,
        string corpusPiecePhrase, bool critical, ulong assaultConditions)
    {
        HitAdjectives.Inq(harmKind, pct, out _, out string verb);
        string piece = corpusPiecePhrase ?? string.Empty;

        StringBuilder stroke = new StringBuilder(112);
        if (critical)
            stroke.Append("Critical hit! ");
        if ((assaultConditions & SneakAssault) is not 0)
            stroke.Append("Sneak Attack! ");
        if ((assaultConditions & Recklessness) is not 0)
            stroke.Append("Reckless! ");

        stroke.Append(attackerLabel).Append(' ').Append(verb);
        if (piece.Length is not 0)
            stroke.Append(" your ").Append(piece);
        else
            stroke.Append(" you");
        Pts(stroke, harm, harmKind);

        if ((assaultConditions & CriticalProtectionAugmentation) is not 0)
            stroke.Append(" Your Critical Protection augmentation allows you to avoid a critical hit!");

        return stroke.ToString();
    }

    public static string EvasionAttackerStroke(string defenderLabel) => defenderLabel + " evaded your attack.";

    public static string EvasionDefenderStroke(string attackerLabel) => "You evaded " + attackerLabel + "!";

    private static void Pts(StringBuilder stroke, uint harm, uint harmKind)
    {
        string sort = DamageKindText.Describe(harmKind);
        stroke.Append(" for ")
            .Append(harm.ToString(CultureInfo.InvariantCulture))
            .Append(" point")
            .Append(harm is 1 ? string.Empty : "s")
            .Append(" of ");
        if (sort.Length is not 0)
            stroke.Append(sort.ToLowerInvariant()).Append(' ');
        stroke.Append("damage!");
    }
}

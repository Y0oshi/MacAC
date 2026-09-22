using System.Globalization;
using System.Text;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public static class ToonDriver
{
    public const uint LayoutId = 0x2100006Eu;
    public const uint RootId = 0x10000183u;
    public const uint PrimaryPhraseTag = 0x1000011Du;
    public const uint ScrollerTag = 0x1000011Eu;
    public const uint CloseId = 0x100000FCu;

    private static readonly AugmentationSpec[] Augmentations =
    [
        new(0xDAu, "ID_CharacterInfo_Augmentation_Attribute_Strength", true),
        new(0xDBu, "ID_CharacterInfo_Augmentation_Attribute_Endurance", true),
        new(0xDCu, "ID_CharacterInfo_Augmentation_Attribute_Coordination", true),
        new(0xDDu, "ID_CharacterInfo_Augmentation_Attribute_Quickness", true),
        new(0xDEu, "ID_CharacterInfo_Augmentation_Attribute_Focus", true),
        new(0xDFu, "ID_CharacterInfo_Augmentation_Attribute_Self", true),
        new(0xF0u, "ID_CharacterInfo_Augmentation_Resist_Slash", true),
        new(0xF1u, "ID_CharacterInfo_Augmentation_Resist_Pierce", true),
        new(0xF2u, "ID_CharacterInfo_Augmentation_Resist_Blunt", true),
        new(0xF3u, "ID_CharacterInfo_Augmentation_Resist_Acid", true),
        new(0x147u, "ID_CharacterInfo_Augmentation_Resist_Nether", true),
        new(0xF4u, "ID_CharacterInfo_Augmentation_Resist_Fire", true),
        new(0xF5u, "ID_CharacterInfo_Augmentation_Resist_Frost", true),
        new(0xF6u, "ID_CharacterInfo_Augmentation_Resist_Lightning", true),
        new(0xE0u, "ID_CharacterInfo_Augmentation_Spec_Gearcraft", false),
        new(0xE1u, "ID_CharacterInfo_Augmentation_Spec_WeaponTinkering", false),
        new(0xE2u, "ID_CharacterInfo_Augmentation_Spec_MagicItemTinkering", false),
        new(0xE3u, "ID_CharacterInfo_Augmentation_Spec_ArmorTinkering", false),
        new(0xE4u, "ID_CharacterInfo_Augmentation_Spec_ItemTinkering", false),
        new(0x125u, "ID_CharacterInfo_Augmentation_Spec_Salvaging", false),
        new(0xE5u, "ID_CharacterInfo_Augmentation_ExtraPackSlot", false),
        new(0xE6u, "ID_CharacterInfo_Augmentation_IncreasedCarryingCapacity", true),
        new(0xE7u, "ID_CharacterInfo_Augmentation_LessDeathItemLoss", true),
        new(0xE8u, "ID_CharacterInfo_Augmentation_SpellsRemainPastDeath", false),
        new(0xE9u, "ID_CharacterInfo_Augmentation_CriticalDefense", false),
        new(0xEAu, "ID_CharacterInfo_Augmentation_BonusXP", false),
        new(0xEBu, "ID_CharacterInfo_Augmentation_BonusSalvage", true),
        new(0xECu, "ID_CharacterInfo_Augmentation_BonusImbueChance", false),
        new(0xEDu, "ID_CharacterInfo_Augmentation_FasterRegen", true),
        new(0xEEu, "ID_CharacterInfo_Augmentation_IncreasedSpellDuration", true),
        new(0x126u, "ID_CharacterInfo_Augmentation_Infused_CreatureMagic", true),
        new(0x127u, "ID_CharacterInfo_Augmentation_Infused_ItemMagic", true),
        new(0x128u, "ID_CharacterInfo_Augmentation_Infused_LifeMagic", true),
        new(0x129u, "ID_CharacterInfo_Augmentation_Infused_WarMagic", true),
        new(0x148u, "ID_CharacterInfo_Augmentation_Infused_VoidMagic", true),
        new(0x12Cu, "ID_CharacterInfo_Augmentation_SkilledMelee", true),
        new(0x12Du, "ID_CharacterInfo_Augmentation_SkilledMissile", true),
        new(0x12Eu, "ID_CharacterInfo_Augmentation_SkilledMagic", true),
        new(0x135u, "ID_CharacterInfo_Augmentation_DamageBonus", true),
        new(0x136u, "ID_CharacterInfo_Augmentation_DamageResist", true),
        new(0x12Au, "ID_CharacterInfo_Augmentation_CriticalExpertise", false),
        new(0x12Bu, "ID_CharacterInfo_Augmentation_CriticalPower", false),
        new(0x146u, "ID_CharacterInfo_Augmentation_JackOfAllTrades", false),
    ];

    internal static IReadOnlyList<string> AugmentationStringTags
        => Augmentations.Select(descriptor => descriptor.StringKey).ToArray();

    public static ToonInformationWidgetDriver Bind(
        ImportedArrangement arrangement,
        Func<ToonSheet> blob,
        WidgetDatFont? datTypeface = null,
        Action? shut = null,
        ToonInfoStrings? texts = null,
        Func<Action, IDisposable>? enlistAltered = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(blob);
        WidgetElem trunk = arrangement.Root;
        WidgetPhrase phrase = arrangement.SeekElem(PrimaryPhraseTag) as WidgetPhrase ?? new WidgetPhrase
        {
            SignalIdent = PrimaryPhraseTag,
            Name = "m_pMainText",
            Left = 12f,
            Top = 44f,
            Width = Math.Max(40f, trunk.Width - 24f),
            Height = Math.Max(40f, trunk.Height - 56f),
            ZOrder = 1_000_000,
            DatFont = datTypeface,
            ClickThrough = false,
        };
        if (phrase.Ancestor is null)
            trunk.AddChild(phrase);
        else if (phrase.DatFont is null && datTypeface is not null)
            phrase.DatFont = datTypeface;

        ToonInfoStrings duplicate = texts ?? ToonInfoStrings.English;
        phrase.PreserveFinishOnArrangement = false;
        WidgetScroller? scroller = arrangement.SeekElem(ScrollerTag) as WidgetScroller;
        scroller?.Model = phrase.Scroll;
        WidgetBtn? shutBtn = arrangement.SeekElem(CloseId) as WidgetBtn;
        shutBtn?.OnClick = shut;
        return new ToonInformationWidgetDriver(
            phrase,
            blob,
            duplicate,
            enlistAltered,
            scroller,
            shutBtn);
    }

    internal static string AssembleDossier(ToonSheet sheet, ToonInfoStrings texts)
    {
        StringBuilder corpus = new StringBuilder();

        string? birth = sheet.BirthStamp is int stamp
            ? ComposeCanonDate(stamp)
            : sheet.BirthDate;
        if (!string.IsNullOrEmpty(birth))
            AffixTickets(corpus, texts.Birth, birth);

        string? played = sheet.SumPlayMomentSecs is int secs
            ? ComposeCanonInterval(secs)
            : sheet.PlayMoment;
        if (!string.IsNullOrEmpty(played))
            AffixTickets(corpus, texts.Played, played);

        switch (sheet.Deaths)
        {
            case 0: corpus.Append(texts.DeathsNone); break;
            case 1: corpus.Append(texts.DeathsOne); break;
            case 2: corpus.Append(texts.DeathsTwo); break;
            default: AffixTickets(corpus, texts.DeathsMany, sheet.Deaths); break;
        }
        AffixSectionBreak(corpus);

        string resist = ResistanceGrade(sheet.Strength + sheet.Endurance,
            200, 260, 320, 380, 440);
        string regeneration = ResistanceGrade(
            sheet.Strength + (2 * sheet.Endurance),
            200, 346, 470, 580, 690);
        corpus.Append(texts.Resists[0]).Append(resist)
            .Append(texts.Resists[1]).Append(resist)
            .Append(texts.Resists[2]).Append(regeneration)
            .Append(texts.Resists[3]);
        AffixSectionBreak(corpus);

        AffixTickets(corpus, texts.Innates,
            sheet.Strength, sheet.Endurance, sheet.Coordination,
            sheet.Quickness, sheet.Focus, sheet.Self);
        AffixSectionBreak(corpus);

        AffixTickets(corpus, texts.Chess, sheet.ChessRank);
        AffixTickets(corpus, texts.Fishing, sheet.FishingAptitude);
        AffixSectionBreak(corpus);

        AffixMastery(corpus, texts.MeleeMastery,
            Get(sheet, 0x162u), MeleeMasteryLabel);
        AffixMastery(corpus, texts.RangedMastery,
            Get(sheet, 0x163u), RangedMasteryLabel);
        AffixMastery(corpus, texts.SummoningMastery,
            Get(sheet, 0x16Au), SummoningMasteryLabel);
        foreach (AugmentationSpec descriptor in Augmentations)
        {
            int val = Get(sheet, descriptor.PropertyId);
            if (val <= 0
                || !texts.AugmentationText.TryGetValue(
                    descriptor.StringKey, out string[]? tickets))
                continue;
            if (descriptor.IncludeValue)
                AffixTickets(corpus, tickets, val);
            else
                corpus.Append(LocatePlural(tickets[0], val));
        }
        AffixSectionBreak(corpus);

        int cap = BurdenRules.EncumbranceCapacity(
            sheet.Strength, sheet.EncumbranceAugmentations);
        float pull = BurdenRules.PullRatio(cap, sheet.BurdenLatest);
        if (pull < 1f)
            corpus.Append(texts.LoadNone);
        else
            AffixTickets(corpus, texts.LoadBurdened,
                sheet.BurdenLatest - cap,
                BurdenRules.PullPenaltyPct(pull));
        if (sheet.EncumbranceAugmentations > 0)
            AffixTickets(corpus, texts.LoadAugmentations,
                sheet.EncumbranceAugmentations,
                sheet.EncumbranceAugmentations * 20f);

        return corpus.ToString().TrimEnd('\r', '\n');
    }

    internal static string ResistanceGrade(
        int val,
        int none,
        int poor,
        int mediocre,
        int hardy,
        int resilient)
    {
        return val <= none ? "None"
                : val <= poor ? "Poor"
                : val <= mediocre ? "Mediocre"
                : val <= hardy ? "Hardy"
                : val <= resilient ? "Resilient"
                : "Indomitable";
    }

    internal static string ComposeCanonInterval(int sumSecs)
    {
        long leftover = Math.Max(0, sumSecs);
        (long seconds, string singular, string plural)[] units =
        [
            (31_536_000, "year", "years"),
            (2_592_000, "month", "months"),
            (604_800, "week", "weeks"),
            (86_400, "day", "days"),
            (3_600, "hour", "hours"),
            (60, "minute", "minutes"),
            (1, "second", "seconds"),
        ];
        List<string> pieces = new List<string>();
        foreach (var unit in units)
        {
            long tally = leftover / unit.seconds;
            leftover %= unit.seconds;
            if (tally is not 0)
                pieces.Add($"{tally} {(tally is 1 ? unit.singular : unit.plural)}");
        }
        return string.Join(' ', pieces);
    }

    private static int Get(ToonSheet sheet, uint ident)
    {
        return sheet.ToonDetailsProps.TryGetValue(ident, out int val) ? val : 0;
    }

    private static void AffixMastery(
        StringBuilder corpus,
        string[] tickets,
        int val,
        Func<int, string> label)
    {
        if (val > 0)
            AffixTickets(corpus, tickets, label(val));
    }

    private static string MeleeMasteryLabel(int val)
    {
        return val switch
        {
            1 => "Unarmed Weapons",
            2 => "Swords",
            3 => "Axes",
            4 => "Maces",
            5 => "Spears",
            6 => "Daggers",
            7 => "Staves",
            11 => "Two Handed Weapons",
            _ => "Unknown",
        };
    }

    private static string RangedMasteryLabel(int val)
    {
        return val switch
        {
            8 => "Bows",
            9 => "Crossbows",
            10 => "Thrown Weapons",
            12 => "Magical Spells",
            _ => "Unknown",
        };
    }

    private static string SummoningMasteryLabel(int val)
    {
        return val switch
        {
            1 => "Primalist",
            2 => "Necromancer",
            3 => "Naturalist",
            _ => "Unknown",
        };
    }

    private static string ComposeCanonDate(int unixSecs)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSecs)
                .LocalDateTime.ToString("G", CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return unixSecs.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void AffixTickets(
        StringBuilder corpus,
        IReadOnlyList<string> tickets,
        params object[] vals)
    {
        for (int idx = 0; idx < tickets.Count; ++idx)
        {
            int pluralVal = vals.Length is 0 ? 0
                : NumericVal(vals[Math.Min(idx, vals.Length - 1)]);
            corpus.Append(LocatePlural(tickets[idx], pluralVal));
            if (idx < vals.Length)
                corpus.Append(Convert.ToString(vals[idx], CultureInfo.InvariantCulture));
        }
    }

    private static string LocatePlural(string phrase, int val)
    {
        return phrase.Replace("{time[1]|times}", val is 1 ? "time" : "times",
                StringComparison.Ordinal);
    }

    private static int NumericVal(object val)
    {
        return val switch
        {
            byte v => v,
            short v => v,
            int v => v,
            long v => checked((int)v),
            float v => (int)v,
            double v => (int)v,
            _ => 0,
        };
    }

    private static void AffixSectionBreak(StringBuilder corpus)
    {
        if (corpus.Length is not 0 && corpus[^1] != '\n') corpus.Append('\n');
        corpus.Append('\n');
    }

    private readonly record struct AugmentationSpec(
        uint PropertyId,
        string StringKey,
        bool IncludeValue);
}

public sealed class ToonInformationWidgetDriver : IRetainedPaneDriver
{
    private readonly Func<ToonSheet> _blob;
    private readonly ToonInfoStrings _texts;
    private readonly WidgetTextArrangementShelf<string> _arrangement;
    private readonly IDisposable? _editMapping;
    private readonly WidgetScroller? _scroller;
    private readonly WidgetBtn? _shut;
    private bool _substanceStale = true;
    private bool _destroyed;

    internal ToonInformationWidgetDriver(
        WidgetPhrase phrase,
        Func<ToonSheet> blob,
        ToonInfoStrings texts,
        Func<Action, IDisposable>? enlistAltered,
        WidgetScroller? scroller,
        WidgetBtn? shut)
    {
        _blob = blob;
        _texts = texts;
        _scroller = scroller;
        _shut = shut;
        _arrangement = new WidgetTextArrangementShelf<string>(
            phrase,
            static (mark, val) => IndicatorSpecificsPhrase.Shape(mark, val),
            string.Empty,
            StringComparer.Ordinal);
        phrase.StrokesSupplier = FetchStrokes;
        _editMapping = enlistAltered?.Invoke(DirtySubstance);
    }

    public void OnShown() => DirtySubstance();

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _editMapping?.Dispose();
        _scroller?.Model = null;
        _shut?.OnClick = null;
    }

    private IReadOnlyList<WidgetPhrase.Line> FetchStrokes()
    {
        if (_substanceStale)
        {
            _arrangement.SetValue(ToonDriver.AssembleDossier(_blob(), _texts));
            _substanceStale = false;
        }
        return _arrangement.FetchStrokes();
    }

    private void DirtySubstance() => _substanceStale = true;
}

public sealed record ToonInfoStrings(
    string[] Birth,
    string[] Played,
    string DeathsNone,
    string DeathsOne,
    string DeathsTwo,
    string[] DeathsMany,
    string[] Resists,
    string[] Innates,
    string[] Chess,
    string[] Fishing,
    string LoadNone,
    string[] LoadBurdened,
    string[] LoadAugmentations,
    string[] MeleeMastery,
    string[] RangedMastery,
    string[] SummoningMastery,
    IReadOnlyDictionary<string, string[]> AugmentationText)
{
    public static ToonInfoStrings FromDat(
        Func<string, string[]?> locate)
    {
        ArgumentNullException.ThrowIfNull(locate);
        var backup = English;
        string[] Fetch(string tag, string[] defaultVal)
            => locate(tag) is { Length: > 0 } val ? val : defaultVal;
        string FetchOne(string tag, string defaultVal)
            => Fetch(tag, [defaultVal])[0];

        var augmentations = new Dictionary<string, string[]>();
        foreach (string lookupKey in ToonDriver.AugmentationStringTags)
            if (locate(lookupKey) is { Length: > 0 } val)
                augmentations[lookupKey] = val;

        return new ToonInfoStrings(
            Fetch("ID_CharacterInfo_Birth", backup.Birth),
            Fetch("ID_CharacterInfo_Played", backup.Played),
            FetchOne("ID_CharacterInfo_Deaths_None", backup.DeathsNone),
            FetchOne("ID_CharacterInfo_Deaths_One", backup.DeathsOne),
            FetchOne("ID_CharacterInfo_Deaths_Two", backup.DeathsTwo),
            Fetch("ID_CharacterInfo_Deaths_Many", backup.DeathsMany),
            Fetch("ID_CharacterInfo_Resists", backup.Resists),
            Fetch("ID_CharacterInfo_Innates", backup.Innates),
            Fetch("ID_CharacterInfo_Chess", backup.Chess),
            Fetch("ID_CharacterInfo_Fishing", backup.Fishing),
            FetchOne("ID_CharacterInfo_Load_None", backup.LoadNone),
            Fetch("ID_CharacterInfo_Load_Burdened", backup.LoadBurdened),
            Fetch("ID_CharacterInfo_Load_Augmentations", backup.LoadAugmentations),
            Fetch("ID_CharacterInfo_Mastery_Melee", backup.MeleeMastery),
            Fetch("ID_CharacterInfo_Mastery_Ranged", backup.RangedMastery),
            Fetch("ID_CharacterInfo_Mastery_Summoning", backup.SummoningMastery),
            augmentations);
    }

    public static ToonInfoStrings English { get; } = new(
        ["You were born on ", ".\n"],
        ["You have played for ", ".\n"],
        "You have never died!\n",
        "You have died only once!\n",
        "You have died twice.\n",
        ["You have died ", " times.\n"],
        ["Natural Resistances: ", "\nDrain Resistances: ", "\nRegeneration Bonus: ", "\n"],
        ["Innate Strength: ", "\nInnate Endurance: ", "\nInnate Coordination: ",
            "\nInnate Quickness: ", "\nInnate Focus: ", "\nInnate Self: ", "\n"],
        ["Chess Rank: ", "\n"],
        ["Fishing Skill: ", "\n"],
        "You are not overburdened at this time.\n",
        ["You are currently overburdened by ",
            " burden units. This is reducing your Run, Jump, Melee Defense and Missile Defense skills by ",
            "%.\n"],
        ["You have been augmented ",
            " times with the Might of the Seventh Mule. This has increased your carrying capacity by ",
            "%.\n"],
        ["Your melee mastery is ", ".\n"],
        ["Your ranged mastery is ", ".\n\n"],
        ["Your summoning mastery is ", ".\n\n"],
        new Dictionary<string, string[]>());
}

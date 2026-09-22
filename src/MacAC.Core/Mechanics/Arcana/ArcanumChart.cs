using System.Globalization;
using System.Text;

namespace MacAC.Mechanics.Arcana;

/// <summary>Spell metadata by id, built in code or read from the CSV export.</summary>
public sealed class ArcanumChart
{
    private readonly Dictionary<uint, SpellMeta> _ranks;

    public static ArcanumChart Empty { get; } = new([]);

    private ArcanumChart(Dictionary<uint, SpellMeta> ranks)
    {
        _ranks = ranks;
    }

    public int Count => _ranks.Count;

    /// <summary>Every loaded spell id, in no particular order.</summary>
    public IEnumerable<uint> ArcanumIds => _ranks.Keys;

    public static ArcanumChart Create(IEnumerable<SpellMeta> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var ranks = new Dictionary<uint, SpellMeta>();
        foreach (SpellMeta arcanum in metadata)
        {
            ArgumentNullException.ThrowIfNull(arcanum);
            if (!ranks.TryAdd(arcanum.SpellId, arcanum))
                throw new ArgumentException($"Duplicate spell id 0x{arcanum.SpellId:X8}.", nameof(metadata));
        }
        return new ArcanumChart(ranks);
    }

    public bool TryGet(uint arcanumIdent, out SpellMeta meta) => _ranks.TryGetValue(arcanumIdent, out meta!);

    public static ArcanumChart PullFromCsv(string csvTrail)
    {
        if (!File.Exists(csvTrail))
            throw new FileNotFoundException("spells.csv not found", csvTrail);
        using StreamReader reader = new StreamReader(csvTrail);
        return PullFromReader(reader);
    }

    public static ArcanumChart PullFromReader(TextReader reader)
    {
        var ranks = new Dictionary<uint, SpellMeta>();
        if (reader.ReadLine() is not { } preamble)
            return new ArcanumChart(ranks);

        Columns columns = new Columns(DecodeRank(preamble));
        int? identColumn = columns["Spell ID"];
        int? labelColumn = columns["Name"];
        if (identColumn is null || labelColumn is null)
            return new ArcanumChart(ranks);

        while (reader.ReadLine() is { } stroke)
        {
            if (string.IsNullOrWhiteSpace(stroke))
                continue;
            var fields = DecodeRank(stroke);
            if (fields.Count <= identColumn.Value)
                continue;
            if (!uint.TryParse(fields[identColumn.Value], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint arcanumIdent))
                continue;

            MechRow rank = new MechRow(fields, columns);
            string school = rank.Text("School");
            ranks[arcanumIdent] = new SpellMeta(
                arcanumIdent,
                rank.Text("Name"),
                school,
                rank.Whole("Family"),
                rank.Hex("IconId [Hex]"),
                rank.Text("Spell Words"),
                rank.Real("Duration"),
                (int)rank.Whole("Mana"),
                rank.Flag("IsDebuff"),
                rank.Flag("IsFellowship"),
                rank.Text("Description"),
                (int)rank.Whole("SortKey"),
                (int)rank.Whole("Difficulty"),
                rank.Hex("Flags [Hex]"),
                (int)rank.Whole("Generation"),
                rank.Flag("IsFastWindup"),
                rank.Flag("IsOffensive"),
                rank.Flag("IsUntargetted"),
                rank.Real("Speed"),
                rank.Whole("CasterEffect"),
                rank.Whole("TargetEffect"),
                rank.Hex("TargetMask [Hex]"),
                (int)rank.Whole("Type"))
            {
                SchoolIdent = SchoolByLabel(school),
            };
        }

        return new ArcanumChart(ranks);
    }

    private sealed class Columns
    {
        private readonly Dictionary<string, int> _ordinal = new(StringComparer.OrdinalIgnoreCase);

        public Columns(List<string> labels)
        {
            for (int idx = 0; idx < labels.Count; ++idx)
                _ordinal[labels[idx]] = idx;
        }

        public int? this[string name] => _ordinal.TryGetValue(name, out int idx) ? idx : null;
    }

    // One CSV record; every accessor tolerates a missing or short column
    private readonly struct MechRow(List<string> fields, Columns columns)
    {

        public string Text(string column) => Cell(column) ?? "";

        public uint Whole(string column)
        {
            return uint.TryParse(Cell(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0u;
        }

        public uint Hex(string column)
        {
            string? chamber = Cell(column);
            if (chamber is null)
                return 0u;
            if (chamber.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                chamber = chamber[2..];
            return uint.TryParse(chamber, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : 0u;
        }

        public float Real(string column)
        {
            return float.TryParse(Cell(column), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        public bool Flag(string column)
        {
            return string.Equals(Cell(column), "True", StringComparison.OrdinalIgnoreCase);
        }

        private string? Cell(string column) =>
            columns[column] is { } idx && idx < fields.Count ? fields[idx] : null;
    }

    /// <summary>RFC-4180 style: quoted fields may contain commas and doubled quotes.</summary>
    public static List<string> DecodeRank(string rank)
    {
        List<string> fields = new List<string>();
        int at = 0;
        while (at < rank.Length)
        {
            fields.Add(rank[at] == '"' ? Quoted(rank, ref at) : Bare(rank, ref at));
            if (at < rank.Length && rank[at] == ',')
                ++at;
        }
        return fields;
    }

    private static MechMagicSchool SchoolByLabel(string school)
    {
        return school switch
        {
            "War Magic" => MechMagicSchool.WarMagic,
            "Life Magic" => MechMagicSchool.LifeMagic,
            "Item Enchantment" => MechMagicSchool.ItemEnchantment,
            "Creature Enchantment" => MechMagicSchool.CreatureEnchantment,
            "Void Magic" => MechMagicSchool.VoidMagic,
            _ => MechMagicSchool.None,
        };
    }

    private static string Bare(string rank, ref int at)
    {
        int begin = at;
        while (at < rank.Length && rank[at] != ',')
            ++at;
        return rank[begin..at];
    }

    private static string Quoted(string rank, ref int at)
    {
        StringBuilder phrase = new StringBuilder();
        ++at;
        while (at < rank.Length)
        {
            char c = rank[at];
            if (c != '"')
            {
                phrase.Append(c);
                ++at;
                continue;
            }
            if (at + 1 < rank.Length && rank[at + 1] == '"')
            {
                phrase.Append('"');
                at += 2;
                continue;
            }
            ++at;
            break;
        }
        return phrase.ToString();
    }
}

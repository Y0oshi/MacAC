namespace MacAC.Mechanics.Gear;

/// <summary>Every typed property table an object can carry, keyed by property id.</summary>
public sealed class TraitBundle
{
    public Dictionary<uint, int> Ints { get; } = [];

    public Dictionary<uint, long> Int64s { get; } = [];

    public Dictionary<uint, bool> Bools { get; } = [];

    public Dictionary<uint, double> Floats { get; } = [];

    public Dictionary<uint, string> Texts { get; } = [];

    public Dictionary<uint, uint> BlobIdents { get; } = [];

    public Dictionary<uint, uint> InstIdents { get; } = [];

    public int FetchInt(uint kdx, int def = 0) => Ints.GetValueOrDefault(kdx, def);

    public long FetchInt64(uint kdx, long def = 0) => Int64s.GetValueOrDefault(kdx, def);

    public bool FetchBool(uint kdx, bool def = false) => Bools.GetValueOrDefault(kdx, def);

    public double FetchFloat(uint kdx, double def = 0) => Floats.GetValueOrDefault(kdx, def);

    public string ObtainString(uint kdx, string def = "") => Texts.GetValueOrDefault(kdx, def);

    public TraitBundle Clone()
    {
        TraitBundle duplicate = new TraitBundle();
        Duplicate(Ints, duplicate.Ints);
        Duplicate(Int64s, duplicate.Int64s);
        Duplicate(Bools, duplicate.Bools);
        Duplicate(Floats, duplicate.Floats);
        Duplicate(Texts, duplicate.Texts);
        Duplicate(BlobIdents, duplicate.BlobIdents);
        Duplicate(InstIdents, duplicate.InstIdents);
        return duplicate;
    }

    private static void Duplicate<T>(Dictionary<uint, T> from, Dictionary<uint, T> to)
    {
        foreach ((uint tag, T val) in from)
            to[tag] = val;
    }
}

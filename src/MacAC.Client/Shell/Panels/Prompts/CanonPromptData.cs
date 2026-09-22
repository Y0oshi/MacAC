namespace MacAC.Client.Shell.Panels;

public static class CanonPromptProperty
{
    public const uint Priority = 0x8Du;
    public const uint Type = 0x8Eu;
    public const uint AdmitCaption = 0x90u;
    public const uint RejectCaption = 0x91u;
    public const uint AckOutcome = 0x92u;
    public const uint PhraseFeedAdmitCaption = 0x9Au;
    public const uint PhraseFeedRejectCaption = 0x9Bu;
    public const uint PhraseFeedOutcome = 0x9Cu;
    public const uint MenuGearList = 0xA6u;
    public const uint MenuGear = 0xA7u;
    public const uint MenuAdmitCaption = 0xA8u;
    public const uint MenuRejectCaption = 0xA9u;
    public const uint MenuPick = 0xABu;
    public const uint ElemAttribute40 = 0xACu;
    public const uint FifoTag = 0xC3u;
    public const uint Message = 0xC5u;
    public const uint UsageObjectIdent = 0x1000003Du;
    public const uint TrainAptitudeIdent = 0x10000040u;
    public const uint TrainAptitudeCredits = 0x10000041u;
}

public enum CanonPromptType : uint
{
    Confirmation = 1,
    Wait = 2,
    Message = 3,
    TextInput = 4,
    ConfirmationTextInput = 5,
    Menu = 6,
    ConfirmationMenu = 7,
}

public sealed class CanonPromptData
{
    private readonly Dictionary<uint, object> _vals = [];

    public IReadOnlyDictionary<uint, object> Values => _vals;

    public CanonPromptData Set<T>(uint propIdent, T val) where T : notnull
    {
        _vals[propIdent] = val;
        return this;
    }

    public bool Contains(uint propIdent) => _vals.ContainsKey(propIdent);

    public bool TryGet<T>(uint propIdent, out T val)
    {
        if (_vals.TryGetValue(propIdent, out object? raw) && raw is T typed)
        {
            val = typed;
            return true;
        }

        val = default!;
        return false;
    }

    public bool FetchBoolean(uint propIdent, bool defaultVal = false)
    {
        return _vals.TryGetValue(propIdent, out object? raw)
                ? raw switch
                {
                    bool val => val,
                    byte val => val is not 0,
                    int val => val is not 0,
                    uint val => val is not 0,
                    _ => defaultVal,
                }
                : defaultVal;
    }

    public uint FetchUInt32(uint propIdent, uint defaultVal = 0u)
    {
        return _vals.TryGetValue(propIdent, out object? raw)
                ? raw switch
                {
                    byte val => val,
                    ushort val => val,
                    int val when val >= 0 => (uint)val,
                    uint val => val,
                    Enum val => Convert.ToUInt32(val),
                    _ => defaultVal,
                }
                : defaultVal;
    }

    public int FetchInt32(uint propIdent, int defaultVal = 0)
    {
        return _vals.TryGetValue(propIdent, out object? raw)
                ? raw switch
                {
                    byte val => val,
                    ushort val => val,
                    int val => val,
                    uint val when val <= int.MaxValue => (int)val,
                    Enum val => Convert.ToInt32(val),
                    _ => defaultVal,
                }
                : defaultVal;
    }

    public string? FetchString(uint propIdent)
    {
        return _vals.TryGetValue(propIdent, out object? raw) ? raw as string : null;
    }

    public CanonPromptData Clone()
    {
        CanonPromptData replicate = new CanonPromptData();
        foreach ((uint propIdent, object val) in _vals)
            replicate._vals.Add(propIdent, val);
        return replicate;
    }

    public static CanonPromptData Confirmation(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new CanonPromptData()
            .Set(CanonPromptProperty.Type, CanonPromptType.Confirmation)
            .Set(CanonPromptProperty.ElemAttribute40, true)
            .Set(CanonPromptProperty.Message, msg);
    }

    public static CanonPromptData Wait(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new CanonPromptData()
            .Set(CanonPromptProperty.Type, CanonPromptType.Wait)
            .Set(CanonPromptProperty.ElemAttribute40, true)
            .Set(CanonPromptProperty.Message, msg);
    }

    public static CanonPromptData Message(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new CanonPromptData()
            .Set(CanonPromptProperty.Type, CanonPromptType.Message)
            .Set(CanonPromptProperty.Message, msg);
    }

    public static CanonPromptData AckPhraseFeed(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new CanonPromptData()
            .Set(CanonPromptProperty.Type, CanonPromptType.ConfirmationTextInput)
            .Set(CanonPromptProperty.ElemAttribute40, true)
            .Set(CanonPromptProperty.Message, msg);
    }

    public static CanonPromptData AckMenu(
        IReadOnlyList<string> gearList,
        int chosenOrdinal = 0)
    {
        ArgumentNullException.ThrowIfNull(gearList);
        return new CanonPromptData()
            .Set(CanonPromptProperty.Type, CanonPromptType.ConfirmationMenu)
            .Set(CanonPromptProperty.ElemAttribute40, true)
            .Set(CanonPromptProperty.MenuGearList, gearList.ToArray())
            .Set(CanonPromptProperty.MenuPick, chosenOrdinal);
    }
}

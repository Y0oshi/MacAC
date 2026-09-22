using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ToonSheetSupplier
{
    public SkillBook? SkillTable { get; set; }

    public IDisposable EnlistAltered(Action altered)
    {
        ArgumentNullException.ThrowIfNull(altered);
        return new ChangeWiring(this, altered);
    }

    public XpTable? ExperienceTable { get; set; }

    public string ToonLabel()
    {
        string? toon = _engagedToonLabel?.Invoke();
        if (!string.IsNullOrWhiteSpace(toon) && toon != "default")
            return toon;
        uint oid = _avatarOid();
        return oid is not 0u && _objects.Get(oid)?.Name is { Length: > 0 } objectLabel ? objectLabel : "Player";
    }

    public static XpTable? PullExperienceChart(
        IDatAccess datFiles, Action<string>? trace = null)
    {
        if (datFiles is null) return null;

        try
        {
            var chart = datFiles.Get<XpTable>(0x0E000018u);
            if (chart is not null) return chart;
        }
        catch (Exception exc)
        {
            trace?.Invoke($"[UI] ExperienceTable 0x0E000018 read failed ({exc.GetType().Name}: {exc.Message}); trying type scan.");
        }

        try
        {
            foreach (uint ident in datFiles.GetAllIdsOfType<XpTable>())
            {
                var chart = datFiles.Get<XpTable>(ident);
                if (chart is not null) return chart;
            }
        }
        catch (Exception exc)
        {
            trace?.Invoke($"[UI] ExperienceTable type scan failed ({exc.GetType().Name}: {exc.Message}); raise costs unavailable.");
        }

        return null;
    }

    public void ProcessEmitReq(ToonStatDriver.RaiseAsk req)
    {
        if (_expectingEmit) return;
        if (req.Cost <= 0) return;
        if (_canTransmitEmit is not null && !_canTransmitEmit()) return;

        bool sent = false;
        switch (req.Kind)
        {
            case ToonStatDriver.EmitMarkFlavor.Attribute:
                if (_transmitEmitAttr is not null)
                {
                    _transmitEmitAttr(req.StatId, (ulong)req.Cost);
                    sent = true;
                }
                break;
            case ToonStatDriver.EmitMarkFlavor.Vital:
                if (_transmitEmitVital is not null)
                {
                    _transmitEmitVital(req.StatId, (ulong)req.Cost);
                    sent = true;
                }
                break;
            case ToonStatDriver.EmitMarkFlavor.Skill:
                if (_transmitEmitAptitude is not null)
                {
                    _transmitEmitAptitude(req.StatId, (ulong)req.Cost);
                    sent = true;
                }
                break;
            case ToonStatDriver.EmitMarkFlavor.TrainSkill:
                if (_transmitTrainAptitude is not null && req.Cost <= uint.MaxValue)
                {
                    _transmitTrainAptitude(req.StatId, (uint)req.Cost);
                    sent = true;
                }
                break;
        }

        if (sent)
            _expectingEmit = true;
    }

    internal void FreeExpectingEmit() => _expectingEmit = false;

    private static long AptitudeEmitPrice(
        XpTable? table,
        ToonSkillAdvancementClass advancement,
        SelfState.SkillFrame aptitude,
        int quantity)
    {
        if (table is null) return 0L;
        uint[] curve = advancement == ToonSkillAdvancementClass.Specialized
            ? table.SpecializedSkills
            : table.TrainedSkills;
        return FirePriceFromXpCurve(curve, aptitude.Ranks, aptitude.Xp, quantity);
    }

    private bool HasOnlineBlob()
    {
        TraitBundle props = LatestAvatarProps();
        return props.Ints.Count > 0
            || props.Int64s.Count > 0
            || _ownAvatar.Skills.Count > 0
            || _ownAvatar.FetchAttr(SelfState.StatKind.Strength) is not null
            || _ownAvatar.Get(SelfState.VitalSort.Health) is not null;
    }

    private TraitBundle LatestAvatarProps()
    {
        uint oid = _avatarOid();
        return oid is not 0u && _objects.Get(oid) is { } avatar
            ? avatar.Properties
            : _ownAvatar.Properties;
    }

    private uint LatestAvatarBitfield()
    {
        uint oid = _avatarOid();
        return oid is not 0u && _objects.Get(oid) is { } avatar
            ? avatar.PublicWeenieBitfield ?? 0u
            : 0u;
    }

    private (long toNext, float fraction) CalculateTierXp(int tier, long sumXp)
    {
        ulong[]? tiers = ExperienceTable?.Levels;
        if (tiers is null || tier < 0 || tier + 1 >= tiers.Length)
            return (0L, 0f);

        long latest = LimitToLong(tiers[tier]);
        long upcoming = LimitToLong(tiers[tier + 1]);
        if (upcoming <= latest) return (0L, 0f);

        long clampedXp = sumXp < latest ? latest : sumXp > upcoming ? upcoming : sumXp;
        long toUpcoming = upcoming - clampedXp;
        float ratio = (float)(clampedXp - latest) / (upcoming - latest);
        return (toNext: toUpcoming, fraction: ratio);
    }

    private static string? PkConditionPhrase(uint publicWeenieBitfield, Func<string, string?>? locateWidgetString)
    {
        PublicWeenieBits bitfield = (PublicWeenieBits)publicWeenieBitfield;
        string tag = (bitfield & PublicWeenieBits.PlayerKiller) != 0
            ? "ID_StatManagement_Header_PKStatus_PK"
            : (bitfield & PublicWeenieBits.PlayerKillerLite) != 0
                ? "ID_StatManagement_Header_PKStatus_PKL"
                : "ID_StatManagement_Header_PKStatus_NPK";
        return locateWidgetString?.Invoke(tag);
    }

    private static ToonSkillAdvancementClass AdvancementFromCondition(uint condition)
    {
        return condition switch
        {
            1u => ToonSkillAdvancementClass.Untrained,
            2u => ToonSkillAdvancementClass.Trained,
            3u => ToonSkillAdvancementClass.Specialized,
            _ => ToonSkillAdvancementClass.Inactive,
        };
    }

    private static bool IsUsableUntrained(uint aptitudeIdent)
    {
        return aptitudeIdent switch
        {
            18u or 37u or 38u or 39u or 40u => false,
            _ => true,
        };
    }

    private static long FirePriceFromXpCurve(uint[]? curve, uint ranks, uint spentXp, int quantity)
    {
        if (curve is null || quantity <= 0) return 0L;
        long upperOrdinal = curve.Length - 1L;
        if (upperOrdinal <= ranks) return 0L;
        long markLong = Math.Min((long)ranks + quantity, upperOrdinal);
        long markXp = curve[(int)markLong];
        long price = markXp - spentXp;
        return price > 0 ? price : 0L;
    }

    private static long LimitToLong(ulong val) =>
        val > long.MaxValue ? long.MaxValue : (long)val;

    private int AttrLatest(SelfState.StatKind sort)
    {
        return _ownAvatar.FetchAttr(sort) is { } attr ? checked((int)Math.Min(int.MaxValue, attr.Current)) : 0;
    }

    private int AttrNet(SelfState.StatKind sort) =>
        _ownAvatar.FetchNetAttr(sort) ?? 0;

    private int VitalLatest(SelfState.VitalSort sort)
    {
        return _ownAvatar.Get(sort) is { } vital ? checked((int)Math.Min(int.MaxValue, vital.Current)) : 0;
    }

    private int VitalUpper(SelfState.VitalSort sort)
    {
        return _ownAvatar.FetchUpperApprox(sort) is { } upper ? checked((int)Math.Min(int.MaxValue, upper)) : 0;
    }

    private int VitalBaseUpper(SelfState.VitalSort sort)
    {
        return _ownAvatar.FetchBaseUpperApprox(sort) is { } upper
            ? checked((int)Math.Min(int.MaxValue, upper))
            : 0;
    }
}

using System.Collections.Frozen;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Arcana;
using MacAC.Dat;
using CoreSpellTable = MacAC.Mechanics.Arcana.ArcanumChart;
using DatSpellTable =  MacAC.Dat.SpellBook;

namespace MacAC.Assets;

public sealed record SpellComponentCard(uint WeenieClassId, string Name, uint Category, uint IconId)
{
    public uint ArcanumModuleIdent { get; init; }
    public double BurnRate { get; init; }
    public uint GestureIdent { get; init; }
    public double GesturePace { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Word { get; init; } = string.Empty;
}

public sealed class ArcanaCatalog
{
    private const uint ArcanumChartDid = 0x0E00000Eu;
    private const uint ArcanumModuleChartDid = 0x0E00000Fu;
    private const uint ModuleIdentLookupDid = 0x27000002u;
    private const uint MagicBundleEnumVal = 0x4u;
    private const uint MagicBundleEnumBucket = 0x10000001u;

    private readonly FrozenDictionary<uint, SpellFormulaShape> _equationForms;
    private readonly FrozenDictionary<uint, uint> _wcidByModuleIdent;
    private readonly FrozenDictionary<uint, uint> _magicBundleWcidBySchool;
    private readonly FrozenDictionary<uint, int> _tiers;

    private ArcanaCatalog(
        CoreSpellTable arcanumChart,
        FrozenDictionary<uint, SpellComponentCard> modules,
        FrozenDictionary<uint, SpellFormulaShape> equationForms,
        FrozenDictionary<uint, uint> wcidByModuleIdent,
        FrozenDictionary<uint, uint> magicBundleWcidBySchool,
        FrozenDictionary<uint, int> tiers)
    {
        SpellTable = arcanumChart;
        Components = modules;
        _equationForms = equationForms;
        _wcidByModuleIdent = wcidByModuleIdent;
        _magicBundleWcidBySchool = magicBundleWcidBySchool;
        _tiers = tiers;
    }

    public static ArcanaCatalog Empty { get; } = new(
        CoreSpellTable.Empty,
        FrozenDictionary<uint, SpellComponentCard>.Empty,
        FrozenDictionary<uint, SpellFormulaShape>.Empty,
        FrozenDictionary<uint, uint>.Empty,
        FrozenDictionary<uint, uint>.Empty,
        FrozenDictionary<uint, int>.Empty);

    public CoreSpellTable SpellTable { get; }

    public IReadOnlyDictionary<uint, SpellComponentCard> Components { get; }

    public bool TryFetchModuleByArcanumModuleIdent(uint arcanumModuleIdent, out SpellComponentCard descriptor)
    {
        if (_wcidByModuleIdent.TryGetValue(arcanumModuleIdent, out uint wcid) && Components.TryGetValue(wcid, out SpellComponentCard? card))
        {
            descriptor = card;
            return true;
        }
        descriptor = null!;
        return false;
    }

    public bool IsModuleBundle(uint weenieClassIdent) => Components.ContainsKey(weenieClassIdent);

    public uint MagicBundleWcidForSchool(uint school)
    {
        return _magicBundleWcidBySchool.TryGetValue(school, out uint wcid) ? wcid : 0u;
    }

    public int FetchArcanumTier(uint arcanumIdent) => _tiers.TryGetValue(arcanumIdent, out int tier) ? tier : 0;

    public ComponentNeedsService BuildRequirementService(ClientThingChart objects, Func<uint> avatarOid, Func<string> acctLabel)
    {
        return new(objects, avatarOid, acctLabel, _equationForms, _wcidByModuleIdent, _magicBundleWcidBySchool);
    }

    public static ArcanaCatalog Load(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        var moduleChart = datFiles.Get<ComponentBook>(ArcanumModuleChartDid);
        (Dictionary<uint, SpellComponentCard> modules, Dictionary<uint, uint> wcidByModuleIdent) =
            ScanModules(moduleChart, datFiles.Get<DualIdNameMap>(ModuleIdentLookupDid));

        DatSpellTable arcanumChart = datFiles.Get<DatSpellTable>(ArcanumChartDid)
            ?? throw new InvalidOperationException($"Required retail ArcanumChart 0x{ArcanumChartDid:X8} is absent from portal.dat");

        List<SpellMeta> metadata = new List<SpellMeta>();
        var equationForms = new Dictionary<uint, SpellFormulaShape>();
        var tiers = new Dictionary<uint, int>();
        foreach ((uint arcanumIdent, SpellSpec arcanumBase) in arcanumChart.Spells)
        {
            SpellMeta arcanum = CanonSpellMetaProjector.Project(arcanumIdent, arcanumBase, moduleChart);
            metadata.Add(arcanum);
            equationForms[arcanumIdent] = new SpellFormulaShape(arcanum.EquationVer, arcanum.EquationModules.ToArray(), (uint)arcanum.SchoolIdent);
            tiers[arcanumIdent] = arcanum.Generation;
        }

        return new ArcanaCatalog(
            CoreSpellTable.Create(metadata),
            modules.ToFrozenDictionary(),
            equationForms.ToFrozenDictionary(),
            wcidByModuleIdent.ToFrozenDictionary(),
            ScanMagicBundles(datFiles).ToFrozenDictionary(),
            tiers.ToFrozenDictionary());
    }

    // Components with a known weenie, keyed by weenie; plus the reverse component-id → weenie map
    private static (Dictionary<uint, SpellComponentCard>, Dictionary<uint, uint>) ScanModules(ComponentBook? chart, DualIdNameMap? idents)
    {
        var cards = new Dictionary<uint, SpellComponentCard>();
        var wcidByModuleIdent = new Dictionary<uint, uint>();
        if (chart is null || idents is null)
            return (cards, wcidByModuleIdent);

        foreach ((uint moduleIdent, ComponentSpec component) in chart.Components)
        {
            if (!idents.ClientEnumToId.TryGetValue(moduleIdent, out uint wcid))
                continue;

            wcidByModuleIdent[moduleIdent] = wcid;
            cards[wcid] = new SpellComponentCard(wcid, component.Name, component.Category, component.IconId)
            {
                ArcanumModuleIdent = moduleIdent,
                BurnRate = component.Cdm,
                GestureIdent = component.Gesture,
                GesturePace = component.Time,
                Type = component.Kind.ToString(),
                Word = component.Text,
            };
        }
        return (cards, wcidByModuleIdent);
    }

    private static Dictionary<uint, uint> ScanMagicBundles(IDatAccess datFiles)
    {
        var bySchool = new Dictionary<uint, uint>();
        uint lookupDid = CanonDataIdResolver.Resolve(datFiles, MagicBundleEnumVal, MagicBundleEnumBucket);
        if (lookupDid is not 0u && datFiles.Portal.TryGet<IdNameMap>(lookupDid, out IdNameMap? lookup) && lookup is not null)
        {
            foreach ((uint school, uint wcid) in lookup.ClientEnumToId)
                bySchool[school] = wcid;
        }
        return bySchool;
    }
}

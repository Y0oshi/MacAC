using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Arcana;

public sealed record SpellFormulaShape(
    uint FormulaVersion,
    IReadOnlyList<uint> ComponentIds,
    uint School = 0u);

public sealed class ComponentNeedsService
{
    public const uint ArcanumModulesNeededProp = 68u;

    private const int UpperVesselZDepth = 16;

    private readonly ClientThingChart _objects;
    private readonly Func<uint> _avatarOid;
    private readonly Func<string> _acctLabel;
    private readonly IReadOnlyDictionary<uint, SpellFormulaShape> _equations;
    private readonly IReadOnlyDictionary<uint, uint> _wcidByScid;
    private readonly IReadOnlyDictionary<uint, uint> _bundleWcidBySchool;

    public ComponentNeedsService(
        ClientThingChart objects,
        Func<uint> playerGuid,
        Func<string> accountName,
        IReadOnlyDictionary<uint, SpellFormulaShape> formulas,
        IReadOnlyDictionary<uint, uint> wcidByScid,
        IReadOnlyDictionary<uint, uint>? magicBundleWcidBySchool = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        _acctLabel = accountName ?? throw new ArgumentNullException(nameof(accountName));
        _equations = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _wcidByScid = wcidByScid ?? throw new ArgumentNullException(nameof(wcidByScid));
        _bundleWcidBySchool = magicBundleWcidBySchool ?? new Dictionary<uint, uint>();
    }

    public bool HasNeededComponents(uint arcanumIdent)
    {
        uint me = _avatarOid();
        var avatar = _objects.Get(me);
        if (avatar is not null && !avatar.Properties.FetchBool(ArcanumModulesNeededProp, true))
            return true;
        if (!_equations.TryGetValue(arcanumIdent, out SpellFormulaShape? equation))
            return false;

        HashSet<uint> carried = new HashSet<uint>();
        foreach (ClientThing gear in _objects.Objects)
        {
            if (Carries(gear, me))
                carried.Add(gear.WeenieClassIdent);
        }

        foreach (uint scid in Form(equation, avatar, me))
        {
            if (scid is 0u)
                continue;
            if (!_wcidByScid.TryGetValue(scid, out uint wcid) || !carried.Contains(wcid))
                return false;
        }
        return true;
    }

    public IReadOnlyList<uint> FetchAppropriateEquation(uint arcanumIdent)
    {
        if (!_equations.TryGetValue(arcanumIdent, out SpellFormulaShape? equation))
            return [];
        uint me = _avatarOid();
        return Form(equation, _objects.Get(me), me);
    }

    public bool IsModulePossessed(uint arcanumModuleIdent)
    {
        if (!_wcidByScid.TryGetValue(arcanumModuleIdent, out uint wcid))
            return false;
        uint me = _avatarOid();
        foreach (ClientThing gear in _objects.Objects)
        {
            if (gear.WeenieClassIdent == wcid && Carries(gear, me))
                return true;
        }
        return false;
    }

    private IReadOnlyList<uint> Form(SpellFormulaShape equation, ClientThing? avatar, uint me)
    {
        bool needed = avatar?.Properties.FetchBool(ArcanumModulesNeededProp, true) ?? true;
        bool infused = InfusionProp(equation.School) is { } prop && avatar?.Properties.FetchInt(prop) > 0;
        bool hasBundle = _bundleWcidBySchool.TryGetValue(equation.School, out uint bundleWcid)
            && _objects.Objects.Any(gear => gear.VesselTag == me && gear.WeenieClassIdent == bundleWcid);

        return !needed || infused || hasBundle
            ? CanonSpellFormula.InqScarabSoleEquation(equation.ComponentIds)
            : CanonSpellFormula.CustomizeForAcct(equation.ComponentIds, equation.FormulaVersion, _acctLabel());
    }

    private static uint? InfusionProp(uint school)
    {
        return school switch
        {
            1u => 0x129u,   // AugmentationInfusedWarMagic
            2u => 0x128u,   // AugmentationInfusedLifeMagic
            3u => 0x127u,   // AugmentationInfusedItemMagic
            4u => 0x126u,   // AugmentationInfusedCreatureMagic
            5u => 0x148u,   // AugmentationInfusedVoidMagic
            _ => null,
        };
    }

    // Walks up the container chain (bounded) looking for the player
    private bool Carries(ClientThing gear, uint me)
    {
        if (me is 0u)
            return false;
        ClientThing cur = gear;
        for (int zDepth = 0; zDepth < UpperVesselZDepth; ++zDepth)
        {
            if (cur.WielderIdent == me || cur.VesselTag == me)
                return true;
            if (cur.VesselTag is 0u || _objects.Get(cur.VesselTag) is not { } ancestor)
                return false;
            cur = ancestor;
        }
        return false;
    }
}

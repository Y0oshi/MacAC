#nullable disable

namespace MacAC.Dat;

public class SkillFormula
{
    public int AdditiveBonus { get; set; }
    public int Attribute1Multiplier { get; set; }
    public int Attribute2Multiplier { get; set; }
    public int Divisor { get; set; }
    public AttributeId Attribute1 { get; set; }
    public AttributeId Attribute2 { get; set; }
    public static SkillFormula Read(ref DatCursor c) => new()
    {
        AdditiveBonus = c.I32(), Attribute1Multiplier = c.I32(), Attribute2Multiplier = c.I32(), Divisor = c.I32(),
        Attribute1 = (AttributeId)c.U32(), Attribute2 = (AttributeId)c.U32(),
    };
}

public class SkillSpec
{
    public string Description { get; set; } = "";
    public string Name { get; set; } = "";
    public uint IconId { get; set; }
    public int TrainedCost { get; set; }
    public int SpecializedCost { get; set; }
    public SkillCategory Category { get; set; }
    public bool UsedInCreation { get; set; }
    public uint MinLevel { get; set; }
    public SkillFormula Formula { get; set; } = null!;
    public double UpperBound { get; set; }
    public double LowerBound { get; set; }
    public double LearnMod { get; set; }
    public static SkillSpec Read(ref DatCursor c) => new()
    {
        Description = c.Str(), Name = c.Str(), IconId = c.U32(), TrainedCost = c.I32(), SpecializedCost = c.I32(), Category = (SkillCategory)c.U32(),
        UsedInCreation = c.Flag32(), MinLevel = c.U32(), Formula = SkillFormula.Read(ref c), UpperBound = c.F64(), LowerBound = c.F64(), LearnMod = c.F64(),
    };
}

public class SkillBook : DatRecord, IDatRecord<SkillBook>
{
    public static RecordKind Kind => RecordKind.SkillTable;
    public static (uint First, uint Last) IdRange => (0x0E000004, 0x0E000004);
    public Dictionary<SkillId, SkillSpec> Skills { get; set; } = [];
    public static SkillBook Read(ref DatCursor c) => new()
    {
        Id = c.U32(), Skills = Tables.ReadPacked(ref c, static (ref DatCursor c) => (SkillId)c.I32(), SkillSpec.Read),
    };
}

public class VitalBook : DatRecord, IDatRecord<VitalBook>
{
    public static RecordKind Kind => RecordKind.VitalTable;
    public static (uint First, uint Last) IdRange => (0x0E000003, 0x0E000003);
    public SkillFormula Health { get; set; } = null!;
    public SkillFormula Stamina { get; set; } = null!;
    public SkillFormula Mana { get; set; } = null!;
    public static VitalBook Read(ref DatCursor c) => new() { Id = c.U32(), Health = SkillFormula.Read(ref c), Stamina = SkillFormula.Read(ref c), Mana = SkillFormula.Read(ref c) };
}

// A spell as the client knows it. Component ids are stored encrypted against a hash of the name and description.
public class SpellSpec
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<uint> Components { get; set; } = [];
    public SpellSchool School { get; set; }
    public uint Icon { get; set; }
    public SpellFamily Category { get; set; }
    public SpellFlags Bits { get; set; }
    public uint BaseMana { get; set; }
    public float BaseRangeConstant { get; set; }
    public float BaseRangeMod { get; set; }
    public uint Power { get; set; }
    public float SpellEconomyMod { get; set; }
    public uint FormulaVersion { get; set; }
    public float ComponentLoss { get; set; }
    public SpellKind Kind { get; set; }
    public uint MetaSpellId { get; set; }
    public double Duration { get; set; }
    public float DegradeModifier { get; set; }
    public float DegradeLimit { get; set; }
    public double PortalLifetime { get; set; }
    public EffectId CasterEffect { get; set; }
    public EffectId TargetEffect { get; set; }
    public EffectId FizzleEffect { get; set; }
    public double RecoveryInterval { get; set; }
    public float RecoveryAmount { get; set; }
    public uint DisplayOrder { get; set; }
    public ItemClass NonComponentTargetType { get; set; }
    public uint ManaMod { get; set; }

    public static SpellSpec Read(ref DatCursor c)
    {
        string name = c.ObfuscatedStr(); c.Align(4);
        string desc = c.ObfuscatedStr(); c.Align(4);
        var school = (SpellSchool)c.I32();
        uint icon = c.U32();
        var category = (SpellFamily)c.U32();
        var bits = (SpellFlags)c.I32();
        uint baseMana = c.U32();
        float rangeConst = c.F32(), rangeMod = c.F32();
        uint power = c.U32();
        float economy = c.F32();
        uint formulaVersion = c.U32();
        float loss = c.F32();
        var kind = (SpellKind)c.U32();
        uint metaId = c.U32();
        double duration = 0, portalLife = 0; float degradeMod = 0, degradeLimit = 0;
        switch (kind)
        {
            case SpellKind.Enchantment or SpellKind.FellowEnchantment:
                duration = c.F64(); degradeMod = c.F32(); degradeLimit = c.F32(); break;
            case SpellKind.PortalSummon:
                portalLife = c.F64(); break;
        }
        Span<uint> raw = stackalloc uint[8];
        for (int i = 0; i < 8; i++) raw[i] = c.U32();
        var comps = Decrypt(raw, name, desc);
        return new SpellSpec
        {
            Name = name, Description = desc, Components = comps, School = school, Icon = icon, Category = category, Bits = bits, BaseMana = baseMana,
            BaseRangeConstant = rangeConst, BaseRangeMod = rangeMod, Power = power, SpellEconomyMod = economy, FormulaVersion = formulaVersion,
            ComponentLoss = loss, Kind = kind, MetaSpellId = metaId, Duration = duration, DegradeModifier = degradeMod, DegradeLimit = degradeLimit,
            PortalLifetime = portalLife, CasterEffect = (EffectId)c.U32(), TargetEffect = (EffectId)c.U32(), FizzleEffect = (EffectId)c.U32(),
            RecoveryInterval = c.F64(), RecoveryAmount = c.F32(), DisplayOrder = c.U32(), NonComponentTargetType = (ItemClass)c.U32(), ManaMod = c.U32(),
        };
    }

    static List<uint> Decrypt(ReadOnlySpan<uint> raw, string name, string desc)
    {
        uint key = TextHash(name) % 303068800u + TextHash(desc) % 3199061829u;
        var list = new List<uint>(8);
        foreach (uint v in raw)
        {
            if (v == 0) continue;
            uint x = v - key;
            if (x > 198) x &= 0xFF;
            if (x != 0) list.Add(x);
        }
        return list;
    }

    // The client's ELF-style string hash over the single-byte text.
    public static uint TextHash(string s)
    {
        long h = 0;
        foreach (byte b in Cp1252.Encode(s))
        {
            h = (sbyte)b + (h << 4);
            if ((h & 0xF0000000L) != 0) h = (h ^ ((h & 0xF0000000L) >> 24)) & 0x0FFFFFFF;
        }
        return (uint)h;
    }
}

public class SpellTiers
{
    public List<uint> SpellIds { get; set; } = [];
    public static SpellTiers Read(ref DatCursor c)
    {
        int n = c.I32();
        var t = new SpellTiers { SpellIds = new List<uint>(n) };
        for (int i = 0; i < n; i++) t.SpellIds.Add(c.U32());
        return t;
    }
}

public class SpellSet
{
    public Dictionary<uint, SpellTiers> Tiers { get; set; } = [];
    public static SpellSet Read(ref DatCursor c)
    {
        int n = c.U16();
        c.U16();
        var s = new SpellSet { Tiers = new Dictionary<uint, SpellTiers>(n) };
        for (int i = 0; i < n; i++) { uint k = c.U32(); s.Tiers[k] = SpellTiers.Read(ref c); }
        return s;
    }
}

public class SpellBook : DatRecord, IDatRecord<SpellBook>
{
    public static RecordKind Kind => RecordKind.SpellTable;
    public static (uint First, uint Last) IdRange => (0x0E00000E, 0x0E00000E);
    public Dictionary<uint, SpellSpec> Spells { get; set; } = [];
    public Dictionary<EquipmentSet, SpellSet> Sets { get; set; } = [];
    public static SpellBook Read(ref DatCursor c) => new()
    {
        Id = c.U32(),
        Spells = Tables.ReadPacked(ref c, Tables.U32, SpellSpec.Read),
        Sets = Tables.ReadPacked(ref c, static (ref DatCursor c) => (EquipmentSet)c.I32(), SpellSet.Read),
    };
}

public class ComponentSpec
{
    public string Name { get; set; } = "";
    public uint Category { get; set; }
    public uint IconId { get; set; }
    public ComponentKind Kind { get; set; }
    public uint Gesture { get; set; }
    public float Time { get; set; }
    public string Text { get; set; } = "";
    public float Cdm { get; set; }
    public static ComponentSpec Read(ref DatCursor c)
    {
        string name = c.ObfuscatedStr(); c.Align(4);
        uint cat = c.U32(), icon = c.U32();
        var kind = (ComponentKind)c.U32();
        uint gesture = c.U32();
        float time = c.F32();
        string text = c.ObfuscatedStr(); c.Align(4);
        return new ComponentSpec { Name = name, Category = cat, IconId = icon, Kind = kind, Gesture = gesture, Time = time, Text = text, Cdm = c.F32() };
    }
}

public class ComponentBook : DatRecord, IDatRecord<ComponentBook>
{
    public static RecordKind Kind => RecordKind.SpellComponentTable;
    public static (uint First, uint Last) IdRange => (0x0E00000F, 0x0E00000F);
    public Dictionary<uint, ComponentSpec> Components { get; set; } = [];
    public static ComponentBook Read(ref DatCursor c) => new() { Id = c.U32(), Components = Tables.ReadPacked(ref c, Tables.U32, ComponentSpec.Read) };
}

public class XpTable : DatRecord, IDatRecord<XpTable>
{
    public static RecordKind Kind => RecordKind.ExperienceTable;
    public static (uint First, uint Last) IdRange => (0x0E000018, 0x0E000018);
    public uint[] Attributes { get; set; } = [];
    public uint[] Vitals { get; set; } = [];
    public uint[] TrainedSkills { get; set; } = [];
    public uint[] SpecializedSkills { get; set; } = [];
    public ulong[] Levels { get; set; } = [];
    public uint[] SkillCredits { get; set; } = [];
    public static XpTable Read(ref DatCursor c)
    {
        uint id = c.U32();
        int na = c.I32() + 1, nv = c.I32() + 1, nt = c.I32() + 1, ns = c.I32() + 1, nl = (int)c.U32() + 1;
        var t = new XpTable { Id = id, Attributes = new uint[na], Vitals = new uint[nv], TrainedSkills = new uint[nt], SpecializedSkills = new uint[ns], Levels = new ulong[nl], SkillCredits = new uint[nl] };
        for (int i = 0; i < na; i++) t.Attributes[i] = c.U32();
        for (int i = 0; i < nv; i++) t.Vitals[i] = c.U32();
        for (int i = 0; i < nt; i++) t.TrainedSkills[i] = c.U32();
        for (int i = 0; i < ns; i++) t.SpecializedSkills[i] = c.U32();
        for (int i = 0; i < nl; i++) t.Levels[i] = c.U64();
        for (int i = 0; i < nl; i++) t.SkillCredits[i] = c.U32();
        return t;
    }
}

public class DatPosition
{
    public uint CellId { get; set; }
    public Pose Pose { get; set; } = null!;
    public static DatPosition Read(ref DatCursor c) => new() { CellId = c.U32(), Pose = Pose.Read(ref c) };
}

public class ContractSpec
{
    public uint Version { get; set; }
    public uint ContractId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionProgress { get; set; } = "";
    public string NpcStartName { get; set; } = "";
    public string NpcEndName { get; set; } = "";
    public string QuestflagStamped { get; set; } = "";
    public string QuestflagStarted { get; set; } = "";
    public string QuestflagFinished { get; set; } = "";
    public string QuestflagProgress { get; set; } = "";
    public string QuestflagTimer { get; set; } = "";
    public string QuestflagRepeatTime { get; set; } = "";
    public DatPosition NpcStart { get; set; } = null!;
    public DatPosition NpcEnd { get; set; } = null!;
    public DatPosition QuestArea { get; set; } = null!;
    public static ContractSpec Read(ref DatCursor c) => new()
    {
        Version = c.U32(), ContractId = c.U32(), Name = c.Str(), Description = c.Str(), DescriptionProgress = c.Str(), NpcStartName = c.Str(), NpcEndName = c.Str(),
        QuestflagStamped = c.Str(), QuestflagStarted = c.Str(), QuestflagFinished = c.Str(), QuestflagProgress = c.Str(), QuestflagTimer = c.Str(), QuestflagRepeatTime = c.Str(),
        NpcStart = DatPosition.Read(ref c), NpcEnd = DatPosition.Read(ref c), QuestArea = DatPosition.Read(ref c),
    };
}

public class ContractBook : DatRecord, IDatRecord<ContractBook>
{
    public static RecordKind Kind => RecordKind.ContractTable;
    public static (uint First, uint Last) IdRange => (0x0E00001D, 0x0E00001D);
    public Dictionary<uint, ContractSpec> Contracts { get; set; } = [];
    public static ContractBook Read(ref DatCursor c) => new() { Id = c.U32(), Contracts = Tables.ReadPacked(ref c, Tables.U32, ContractSpec.Read) };
}

public class Maneuver
{
    public Stance Stance { get; set; }
    public StrikeHeight Height { get; set; }
    public StrikeKind Attack { get; set; }
    public uint MinSkillLevel { get; set; }
    public MotionId Motion { get; set; }
    public static Maneuver Read(ref DatCursor c) => new() { Stance = (Stance)c.U32(), Height = (StrikeHeight)c.I32(), Attack = (StrikeKind)c.I32(), MinSkillLevel = c.U32(), Motion = (MotionId)c.U32() };
}

public class ManeuverBook : DatRecord, IDatRecord<ManeuverBook>
{
    public static RecordKind Kind => RecordKind.CombatTable;
    public static (uint First, uint Last) IdRange => (0x30000000, 0x3000FFFF);
    public List<Maneuver> Maneuvers { get; set; } = [];
    public static ManeuverBook Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var b = new ManeuverBook { Id = id, Maneuvers = new List<Maneuver>(n) };
        for (int i = 0; i < n; i++) b.Maneuvers.Add(Maneuver.Read(ref c));
        return b;
    }
}

// ---- character creation ----

public class SkillChoice
{
    public SkillId Skill { get; set; }
    public int NormalCost { get; set; }
    public int PrimaryCost { get; set; }
    public static SkillChoice Read(ref DatCursor c) => new() { Skill = (SkillId)c.I32(), NormalCost = c.I32(), PrimaryCost = c.I32() };
}

public class TemplateChoice
{
    public string Name { get; set; } = "";
    public uint IconId { get; set; }
    public uint Title { get; set; }
    public int Strength { get; set; }
    public int Endurance { get; set; }
    public int Coordination { get; set; }
    public int Quickness { get; set; }
    public int Focus { get; set; }
    public int Self { get; set; }
    public List<SkillId> NormalSkills { get; set; } = [];
    public List<SkillId> PrimarySkills { get; set; } = [];
    public static TemplateChoice Read(ref DatCursor c)
    {
        var t = new TemplateChoice
        {
            Name = c.PStr(), IconId = c.U32(), Title = c.U32(), Strength = c.I32(), Endurance = c.I32(), Coordination = c.I32(), Quickness = c.I32(), Focus = c.I32(), Self = c.I32(),
        };
        int n = (int)c.PackedU32(); for (int i = 0; i < n; i++) t.NormalSkills.Add((SkillId)c.I32());
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) t.PrimarySkills.Add((SkillId)c.I32());
        return t;
    }
}

public class HairChoice
{
    public uint IconId { get; set; }
    public bool Bald { get; set; }
    public uint AlternateRigId { get; set; }
    public LookDesc Look { get; set; } = null!;
    public static HairChoice Read(ref DatCursor c) => new() { IconId = c.U32(), Bald = c.U8() != 0, AlternateRigId = c.U32(), Look = LookDesc.Read(ref c) };
}

public class EyeChoice
{
    public uint IconId { get; set; }
    public uint BaldIconId { get; set; }
    public LookDesc Look { get; set; } = null!;
    public LookDesc BaldLook { get; set; } = null!;
    public static EyeChoice Read(ref DatCursor c) => new() { IconId = c.U32(), BaldIconId = c.U32(), Look = LookDesc.Read(ref c), BaldLook = LookDesc.Read(ref c) };
}

public class FaceChoice
{
    public uint IconId { get; set; }
    public LookDesc Look { get; set; } = null!;
    public static FaceChoice Read(ref DatCursor c) => new() { IconId = c.U32(), Look = LookDesc.Read(ref c) };
}

public class GearChoice
{
    public string Name { get; set; } = "";
    public uint WardrobeId { get; set; }
    public uint WeenieDefault { get; set; }
    public static GearChoice Read(ref DatCursor c) => new() { Name = c.PStr(), WardrobeId = c.U32(), WeenieDefault = c.U32() };
}

public class SexChoice
{
    public string Name { get; set; } = "";
    public uint Scale { get; set; }
    public uint RigId { get; set; }
    public uint SoundBookId { get; set; }
    public uint IconId { get; set; }
    public uint BaseColorTableId { get; set; }
    public uint SkinColorTableSetId { get; set; }
    public uint EffectBookId { get; set; }
    public uint MotionBookId { get; set; }
    public uint ManeuverBookId { get; set; }
    public LookDesc BaseLook { get; set; } = null!;
    public List<uint> HairColors { get; set; } = [];
    public List<HairChoice> HairStyles { get; set; } = [];
    public List<uint> EyeColors { get; set; } = [];
    public List<EyeChoice> EyeStrips { get; set; } = [];
    public List<FaceChoice> NoseStrips { get; set; } = [];
    public List<FaceChoice> MouthStrips { get; set; } = [];
    public List<GearChoice> Headgear { get; set; } = [];
    public List<GearChoice> Shirts { get; set; } = [];
    public List<GearChoice> Pants { get; set; } = [];
    public List<GearChoice> Footwear { get; set; } = [];
    public List<uint> ClothingColors { get; set; } = [];

    public static SexChoice Read(ref DatCursor c)
    {
        var s = new SexChoice
        {
            Name = c.PStr(), Scale = c.U32(), RigId = c.U32(), SoundBookId = c.U32(), IconId = c.U32(), BaseColorTableId = c.U32(), SkinColorTableSetId = c.U32(),
            EffectBookId = c.U32(), MotionBookId = c.U32(), ManeuverBookId = c.U32(), BaseLook = LookDesc.Read(ref c),
        };
        int n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.HairColors.Add(c.U32());
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.HairStyles.Add(HairChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.EyeColors.Add(c.U32());
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.EyeStrips.Add(EyeChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.NoseStrips.Add(FaceChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.MouthStrips.Add(FaceChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.Headgear.Add(GearChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.Shirts.Add(GearChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.Pants.Add(GearChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.Footwear.Add(GearChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) s.ClothingColors.Add(c.U32());
        return s;
    }
}

public class HeritageChoice
{
    public string Name { get; set; } = "";
    public uint IconId { get; set; }
    public uint RigId { get; set; }
    public uint EnvironmentRigId { get; set; }
    public uint AttributeCredits { get; set; }
    public uint SkillCredits { get; set; }
    public List<int> PrimaryStartAreas { get; set; } = [];
    public List<int> SecondaryStartAreas { get; set; } = [];
    public List<SkillChoice> Skills { get; set; } = [];
    public List<TemplateChoice> Templates { get; set; } = [];
    public Dictionary<int, SexChoice> Sexes { get; set; } = [];

    public static HeritageChoice Read(ref DatCursor c)
    {
        var h = new HeritageChoice { Name = c.PStr(), IconId = c.U32(), RigId = c.U32(), EnvironmentRigId = c.U32(), AttributeCredits = c.U32(), SkillCredits = c.U32() };
        int n = (int)c.PackedU32(); for (int i = 0; i < n; i++) h.PrimaryStartAreas.Add(c.I32());
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) h.SecondaryStartAreas.Add(c.I32());
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) h.Skills.Add(SkillChoice.Read(ref c));
        n = (int)c.PackedU32(); for (int i = 0; i < n; i++) h.Templates.Add(TemplateChoice.Read(ref c));
        return new HeritageChoice
        {
            Name = h.Name, IconId = h.IconId, RigId = h.RigId, EnvironmentRigId = h.EnvironmentRigId, AttributeCredits = h.AttributeCredits, SkillCredits = h.SkillCredits,
            PrimaryStartAreas = h.PrimaryStartAreas, SecondaryStartAreas = h.SecondaryStartAreas, Skills = h.Skills, Templates = h.Templates,
            Sexes = Tables.ReadHashed(ref c, Tables.I32, SexChoice.Read),
        };
    }
}

public class StartingArea
{
    public string Name { get; set; } = "";
    public List<DatPosition> Locations { get; set; } = [];
    public static StartingArea Read(ref DatCursor c)
    {
        var a = new StartingArea { Name = c.PStr() };
        int n = (int)c.PackedU32();
        for (int i = 0; i < n; i++) a.Locations.Add(DatPosition.Read(ref c));
        return a;
    }
}

public class CharacterCreation : DatRecord, IDatRecord<CharacterCreation>
{
    public static RecordKind Kind => RecordKind.CharGen;
    public static (uint First, uint Last) IdRange => (0x0E000002, 0x0E000002);
    public uint DataId { get; set; }
    public List<StartingArea> StartingAreas { get; set; } = [];
    public Dictionary<uint, HeritageChoice> Heritages { get; set; } = [];
    public static CharacterCreation Read(ref DatCursor c)
    {
        uint id = c.U32(), dataId = c.U32();
        int n = (int)c.PackedU32();
        var areas = new List<StartingArea>(n);
        for (int i = 0; i < n; i++) areas.Add(StartingArea.Read(ref c));
        return new CharacterCreation { Id = id, DataId = dataId, StartingAreas = areas, Heritages = Tables.ReadHashed(ref c, Tables.U32, HeritageChoice.Read) };
    }
}

// ---- text and naming ----

public class TextEntry
{
    public uint TableId { get; set; }
    public List<string> Strings { get; set; } = [];
    public List<uint> Variables { get; set; } = [];
    public bool VarNameTableWorthPacking { get; set; }
    public static TextEntry Read(ref DatCursor c)
    {
        var e = new TextEntry { TableId = c.U32() };
        int n = (int)c.U32(); for (int i = 0; i < n; i++) e.Strings.Add(c.WideStr());
        n = (int)c.U32(); for (int i = 0; i < n; i++) e.Variables.Add(c.U32());
        return new TextEntry { TableId = e.TableId, Strings = e.Strings, Variables = e.Variables, VarNameTableWorthPacking = c.U8() != 0 };
    }
}

public class TextTable : DatRecord, IDatRecord<TextTable>
{
    public static RecordKind Kind => RecordKind.StringTable;
    public static DatShelf Shelf => DatShelf.Local;
    public static (uint First, uint Last) IdRange => (0x23000000, 0x24FFFFFF);
    public uint Language { get; set; }
    public Dictionary<uint, TextEntry> Entries { get; set; } = [];
    public static TextTable Read(ref DatCursor c) => new() { Id = c.U32(), Language = c.U32(), Entries = Tables.ReadHashed(ref c, Tables.U32, TextEntry.Read) };
}

public class LanguageString : DatRecord, IDatRecord<LanguageString>
{
    public static RecordKind Kind => RecordKind.LanguageString;
    public static (uint First, uint Last) IdRange => (0x31000000, 0x3100FFFF);
    public string Value { get; set; } = "";
    public static LanguageString Read(ref DatCursor c) => new() { Id = c.U32(), Value = c.PStr() };
}

// Enum value -> display name.
public class NameMap : DatRecord, IDatRecord<NameMap>
{
    public static RecordKind Kind => RecordKind.EnumMapper;
    public static (uint First, uint Last) IdRange => (0x22000000, 0x22FFFFFF);
    public uint BaseMapId { get; set; }
    public Dictionary<uint, string> Names { get; set; } = [];
    public static NameMap Read(ref DatCursor c) => new() { Id = c.U32(), BaseMapId = c.U32(), Names = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr) };
}

// Client and server enum values mapped to ids and names.
public class IdNameMap : DatRecord, IDatRecord<IdNameMap>
{
    public static RecordKind Kind => RecordKind.EnumIDMap;
    public static (uint First, uint Last) IdRange => (0x25000000, 0x25FFFFFF);
    public Dictionary<uint, uint> ClientEnumToId { get; set; } = [];
    public Dictionary<uint, string> ClientEnumToName { get; set; } = [];
    public Dictionary<uint, uint> ServerEnumToId { get; set; } = [];
    public Dictionary<uint, string> ServerEnumToName { get; set; } = [];
    public static IdNameMap Read(ref DatCursor c) => new()
    {
        Id = c.U32(),
        ClientEnumToId = Tables.ReadHashed(ref c, Tables.U32, Tables.U32), ClientEnumToName = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr),
        ServerEnumToId = Tables.ReadHashed(ref c, Tables.U32, Tables.U32), ServerEnumToName = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr),
    };
}

public class DualIdNameMap : DatRecord, IDatRecord<DualIdNameMap>
{
    public static RecordKind Kind => RecordKind.DualEnumIDMap;
    public static (uint First, uint Last) IdRange => (0x27000000, 0x27FFFFFF);
    public Dictionary<uint, uint> ClientEnumToId { get; set; } = [];
    public Dictionary<uint, string> ClientEnumToName { get; set; } = [];
    public Dictionary<uint, uint> ServerEnumToId { get; set; } = [];
    public Dictionary<uint, string> ServerEnumToName { get; set; } = [];
    public static DualIdNameMap Read(ref DatCursor c) => new()
    {
        Id = c.U32(),
        ClientEnumToId = Tables.ReadHashed(ref c, Tables.U32, Tables.U32), ClientEnumToName = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr),
        ServerEnumToId = Tables.ReadHashed(ref c, Tables.U32, Tables.U32), ServerEnumToName = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr),
    };
}

// The data file's own version stamp: no id in the file, the id comes from the directory entry.
public class RevisionStamp : DatRecord, IDatRecord<RevisionStamp>
{
    public static RecordKind Kind => RecordKind.Iteration;
    public static (uint First, uint Last) IdRange => (0xFFFF0001, 0xFFFF0001);
    public const uint FileId = 0xFFFF0001;
    public int Current { get; set; }
    public Dictionary<int, int> Revisions { get; set; } = [];
    public static RevisionStamp Read(ref DatCursor c)
    {
        int current = c.I32();
        var revs = new Dictionary<int, int>();
        for (int left = current; left > 0;)
        {
            int span = c.I32();
            int key = c.I32();
            revs[key] = span;
            left += span;
        }
        return new RevisionStamp { Id = c.FileId, Current = current, Revisions = revs };
    }
}

public class BadDataTable : DatRecord, IDatRecord<BadDataTable>
{
    public static RecordKind Kind => RecordKind.BadDataTable;
    public static (uint First, uint Last) IdRange => (0x0E00001A, 0x0E00001A);
    public Dictionary<uint, uint> BadIds { get; set; } = [];
    public static BadDataTable Read(ref DatCursor c) => new() { Id = c.U32(), BadIds = Tables.ReadPacked(ref c, Tables.U32, Tables.U32) };
}

public class ChatEmote
{
    public string MyEmote { get; set; } = "";
    public string OtherEmote { get; set; } = "";
    public static ChatEmote Read(ref DatCursor c) => new() { MyEmote = c.Str(), OtherEmote = c.Str() };
}

public class ChatPoseTable : DatRecord, IDatRecord<ChatPoseTable>
{
    public static RecordKind Kind => RecordKind.ChatPoseTable;
    public static (uint First, uint Last) IdRange => (0x0E000007, 0x0E000007);
    public Dictionary<string, string> Poses { get; set; } = [];
    public Dictionary<string, ChatEmote> Emotes { get; set; } = [];
    public static ChatPoseTable Read(ref DatCursor c) => new()
    {
        Id = c.U32(), Poses = Tables.ReadPacked(ref c, Tables.Str, Tables.Str), Emotes = Tables.ReadPacked(ref c, Tables.Str, ChatEmote.Read),
    };
}

public class TabooEntry
{
    public uint Key { get; set; }
    public ushort Unknown { get; set; }
    public List<string> BannedPatterns { get; set; } = [];
    public static TabooEntry Read(ref DatCursor c)
    {
        var e = new TabooEntry { Key = c.U32(), Unknown = c.U16() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) e.BannedPatterns.Add(c.PStr());
        return e;
    }
}

public class TabooTable : DatRecord, IDatRecord<TabooTable>
{
    public static RecordKind Kind => RecordKind.TabooTable;
    public static (uint First, uint Last) IdRange => (0x0E00001E, 0x0E00001E);
    public Dictionary<uint, TabooEntry> AudienceToBannedPatterns { get; set; } = [];
    public static TabooTable Read(ref DatCursor c) => new() { Id = c.U32(), AudienceToBannedPatterns = Tables.ReadHashed(ref c, Tables.U32, TabooEntry.Read) };
}

public class NameFilterLanguage
{
    public uint MaxSameCharactersInARow { get; set; }
    public uint MaxVowelsInARow { get; set; }
    public uint FirstNCharactersMustHaveAVowel { get; set; }
    public uint VowelContainingSubstringLength { get; set; }
    public string ExtraAllowedCharacters { get; set; } = "";
    public List<string> CompoundLetterGroups { get; set; } = [];
    public static NameFilterLanguage Read(ref DatCursor c)
    {
        var l = new NameFilterLanguage
        {
            MaxSameCharactersInARow = c.U32(), MaxVowelsInARow = c.U32(), FirstNCharactersMustHaveAVowel = c.U32(), VowelContainingSubstringLength = c.U32(), ExtraAllowedCharacters = c.WideStr(),
        };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) l.CompoundLetterGroups.Add(c.WideStr());
        return l;
    }
}

public class NameFilterTable : DatRecord, IDatRecord<NameFilterTable>
{
    public static RecordKind Kind => RecordKind.NameFilterTable;
    public static (uint First, uint Last) IdRange => (0x0E000020, 0x0E000020);
    public Dictionary<uint, NameFilterLanguage> Languages { get; set; } = [];
    public static NameFilterTable Read(ref DatCursor c) => new() { Id = c.U32(), Languages = Tables.ReadHashed(ref c, Tables.U32, NameFilterLanguage.Read) };
}

public class ObjectNode
{
    public string MenuName { get; set; } = "";
    public uint WeenieClassId { get; set; }
    public List<ObjectNode> Children { get; set; } = [];
    public static ObjectNode Read(ref DatCursor c)
    {
        string name = c.ObfuscatedStr(); c.Align(4);
        uint wcid = c.U32();
        int n = c.I32();
        var kids = new List<ObjectNode>(n);
        for (int i = 0; i < n; i++) kids.Add(Read(ref c));
        c.Align(4);
        return new ObjectNode { MenuName = name, WeenieClassId = wcid, Children = kids };
    }
}

public class ObjectHierarchy : DatRecord, IDatRecord<ObjectHierarchy>
{
    public static RecordKind Kind => RecordKind.ObjectHierarchy;
    public static (uint First, uint Last) IdRange => (0x0E00000D, 0x0E00000D);
    public ObjectNode Root { get; set; } = null!;
    public static ObjectHierarchy Read(ref DatCursor c) => new() { Id = c.U32(), Root = ObjectNode.Read(ref c) };
}

public class LanguageInfo : DatRecord, IDatRecord<LanguageInfo>
{
    public static RecordKind Kind => RecordKind.LanguageInfo;
    public static DatShelf Shelf => DatShelf.Local;
    public static (uint First, uint Last) IdRange => (0x41000000, 0x41FFFFFF);
    public int Version { get; set; }
    public ushort Base { get; set; }
    public ushort DecimalDigits { get; set; }
    public bool LeadingZero { get; set; }
    public ushort GroupingSize { get; set; }
    public string Numerals { get; set; } = "";
    public string DecimalSeparator { get; set; } = "";
    public string GroupingSeparator { get; set; } = "";
    public string NegativeNumberFormat { get; set; } = "";
    public bool IsZeroSingular { get; set; }
    public bool IsOneSingular { get; set; }
    public bool IsNegativeOneSingular { get; set; }
    public bool IsTwoOrMoreSingular { get; set; }
    public bool IsNegativeTwoOrLessSingular { get; set; }
    public string TreasurePrefixLetters { get; set; } = "";
    public string TreasureMiddleLetters { get; set; } = "";
    public string TreasureSuffixLetters { get; set; } = "";
    public string MalePlayerLetters { get; set; } = "";
    public string FemalePlayerLetters { get; set; } = "";
    public uint ImeEnabledSetting { get; set; }
    public uint SymbolColor { get; set; }
    public uint SymbolColorText { get; set; }
    public uint SymbolHeight { get; set; }
    public uint SymbolTranslucence { get; set; }
    public uint SymbolPlacement { get; set; }
    public uint CandColorBase { get; set; }
    public uint CandColorBorder { get; set; }
    public uint CandColorText { get; set; }
    public uint CompColorInput { get; set; }
    public uint CompColorTargetConv { get; set; }
    public uint CompColorConverted { get; set; }
    public uint CompColorTargetNotConv { get; set; }
    public uint CompColorInputErr { get; set; }
    public uint CompTranslucence { get; set; }
    public uint CompColorText { get; set; }
    public uint OtherIme { get; set; }
    public int WordWrapOnSpace { get; set; }
    public string AdditionalSettings { get; set; } = "";
    public uint AdditionalFlags { get; set; }

    public static LanguageInfo Read(ref DatCursor c)
    {
        int version = c.I32();
        ushort b = c.U16(), digits = c.U16();
        bool leading = c.U8() != 0;
        ushort grouping = c.U16();
        string numerals = c.WideStr(), dec = c.WideStr(), grp = c.WideStr(), neg = c.WideStr();
        bool z = c.U8() != 0, one = c.U8() != 0, negOne = c.U8() != 0, two = c.U8() != 0, negTwo = c.U8() != 0;
        c.Align(4);
        string tp = c.WideStr(), tm = c.WideStr(), ts = c.WideStr(), male = c.WideStr(), female = c.WideStr();
        var ime = new uint[17];
        for (int i = 0; i < 17; i++) ime[i] = c.U32();
        int wrap = c.I32();
        string extra = c.WideStr();
        return new LanguageInfo
        {
            Id = c.FileId, Version = version, Base = b, DecimalDigits = digits, LeadingZero = leading, GroupingSize = grouping, Numerals = numerals, DecimalSeparator = dec,
            GroupingSeparator = grp, NegativeNumberFormat = neg, IsZeroSingular = z, IsOneSingular = one, IsNegativeOneSingular = negOne, IsTwoOrMoreSingular = two,
            IsNegativeTwoOrLessSingular = negTwo, TreasurePrefixLetters = tp, TreasureMiddleLetters = tm, TreasureSuffixLetters = ts, MalePlayerLetters = male,
            FemalePlayerLetters = female, ImeEnabledSetting = ime[0], SymbolColor = ime[1], SymbolColorText = ime[2], SymbolHeight = ime[3], SymbolTranslucence = ime[4], SymbolPlacement = ime[5], CandColorBase = ime[6], CandColorBorder = ime[7], CandColorText = ime[8], CompColorInput = ime[9], CompColorTargetConv = ime[10], CompColorConverted = ime[11], CompColorTargetNotConv = ime[12], CompColorInputErr = ime[13], CompTranslucence = ime[14], CompColorText = ime[15], OtherIme = ime[16], WordWrapOnSpace = wrap, AdditionalSettings = extra, AdditionalFlags = c.U32(),
        };
    }
}

public class QualityFilter : DatRecord, IDatRecord<QualityFilter>
{
    public static RecordKind Kind => RecordKind.QualityFilter;
    public static (uint First, uint Last) IdRange => (0x0E010000, 0x0E01FFFF);
    public uint[] IntStats { get; set; } = [];
    public uint[] Int64Stats { get; set; } = [];
    public uint[] BoolStats { get; set; } = [];
    public uint[] FloatStats { get; set; } = [];
    public uint[] DataIdStats { get; set; } = [];
    public uint[] InstanceIdStats { get; set; } = [];
    public uint[] StringStats { get; set; } = [];
    public uint[] PositionStats { get; set; } = [];
    public uint[] AttributeStats { get; set; } = [];
    public uint[] Attribute2ndStats { get; set; } = [];
    public uint[] SkillStats { get; set; } = [];

    public static QualityFilter Read(ref DatCursor c)
    {
        uint id = c.U32();
        var counts = new int[8];
        for (int i = 0; i < 8; i++) counts[i] = (int)c.U32();
        var lists = new uint[11][];
        for (int i = 0; i < 8; i++) lists[i] = ReadIds(ref c, counts[i]);
        var more = new int[3];
        for (int i = 0; i < 3; i++) more[i] = (int)c.U32();
        for (int i = 0; i < 3; i++) lists[8 + i] = ReadIds(ref c, more[i]);
        return new QualityFilter
        {
            Id = id, IntStats = lists[0], Int64Stats = lists[1], BoolStats = lists[2], FloatStats = lists[3], DataIdStats = lists[4], InstanceIdStats = lists[5],
            StringStats = lists[6], PositionStats = lists[7], AttributeStats = lists[8], Attribute2ndStats = lists[9], SkillStats = lists[10],
        };
    }

    static uint[] ReadIds(ref DatCursor c, int n) { var a = new uint[n]; for (int i = 0; i < n; i++) a[i] = c.U32(); return a; }
}

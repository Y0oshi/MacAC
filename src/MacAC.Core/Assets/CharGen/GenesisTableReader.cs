using System.Collections.Frozen;
using MacAC.Mechanics.Genesis;
using MacAC.Dat;
using DatCharGen =  MacAC.Dat.CharacterCreation;
using DatObjDesc =  MacAC.Dat.LookDesc;
using DatSkillTable =  MacAC.Dat.SkillBook;

namespace MacAC.Assets.CharGen;

public static class GenesisTableReader
{
    public const uint ChargenChartDid = 0x0E000002u;

    public const uint AptitudeChartDid = 0x0E000004u;

    public static GenesisOptions Load(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        DatCharGen? chart = datFiles.Get<DatCharGen>(ChargenChartDid);
        if (chart is null)
            return GenesisOptions.Empty;
        return Project(chart, datFiles.Get<DatSkillTable>(AptitudeChartDid));
    }

    public static GenesisOptions Project(DatCharGen chart, DatSkillTable? aptitudeChart = null)
    {
        ArgumentNullException.ThrowIfNull(chart);

        GenesisStartArea[] starterAreas = new GenesisStartArea[chart.StartingAreas.Count];
        for (int idx = 0; idx < starterAreas.Length; ++idx)
            starterAreas[idx] = BeginArea(idx, chart.StartingAreas[idx]);

        var heritages = new Dictionary<uint, GenesisHeritageOptions>(chart.Heritages.Count);
        foreach ((uint ident, HeritageChoice cluster) in chart.Heritages)
            heritages[ident] = Heritage(ident, cluster);

        int aptitudeTally = aptitudeChart?.Skills.Count ?? 0;
        var prices = new Dictionary<uint, GenesisSkillPrice>(aptitudeTally);
        var particulars = new Dictionary<uint, GenesisSkillDetail>(aptitudeTally);
        if (aptitudeChart is not null)
        {
            foreach ((SkillId ident, SkillSpec aptitude) in aptitudeChart.Skills)
            {
                uint aptitudeIdent = (uint)ident;
                prices[aptitudeIdent] = new GenesisSkillPrice(aptitudeIdent, aptitude.TrainedCost, aptitude.SpecializedCost);
                particulars[aptitudeIdent] = new GenesisSkillDetail(
                    aptitudeIdent,
                    aptitude.MinLevel,
                    aptitude.Description,
                    new GenesisSkillFormula(
                        aptitude.Formula.AdditiveBonus,
                        aptitude.Formula.Attribute1Multiplier,
                        aptitude.Formula.Attribute2Multiplier,
                        aptitude.Formula.Divisor,
                        (uint)aptitude.Formula.Attribute1,
                        (uint)aptitude.Formula.Attribute2));
            }
        }

        return new GenesisOptions(
            Array.AsReadOnly(starterAreas),
            heritages.ToFrozenDictionary(),
            prices.ToFrozenDictionary(),
            particulars.ToFrozenDictionary());
    }

    // Projects every element of a dat list into a read-only array, in order
    private static IReadOnlyList<TOut> Each<TIn, TOut>(List<TIn> ranks, Func<TIn, TOut> project)
    {
        TOut[] projected = new TOut[ranks.Count];
        for (int idx = 0; idx < projected.Length; ++idx)
            projected[idx] = project(ranks[idx]);
        return Array.AsReadOnly(projected);
    }

    private static IReadOnlyList<uint> Idents(List<SkillId> aptitudes) => Each(aptitudes, static s => (uint)s);

    private static GenesisStartArea BeginArea(int ordinal, StartingArea area)
    {
        return new(ordinal, area.Name, Each(area.Locations, static position => new GenesisSpawnPoint(position.CellId, position.Pose.Origin, position.Pose.Orientation)));
    }

    private static GenesisHeritageOptions Heritage(uint lineageIdent, HeritageChoice cg)
    {
        var prices = new Dictionary<uint, GenesisSkillPrice>(cg.Skills.Count);
        foreach (SkillChoice aptitude in cg.Skills)
            prices[(uint)aptitude.Skill] = new GenesisSkillPrice((uint)aptitude.Skill, aptitude.NormalCost, aptitude.PrimaryCost);

        var sexes = new Dictionary<int, GenesisSexOptions>(cg.Sexes.Count);
        foreach ((int tag, SexChoice sex) in cg.Sexes)
            sexes[tag] = Sex(tag, sex);

        return new GenesisHeritageOptions(
            lineageIdent,
            cg.Name,
            cg.IconId,
            cg.RigId,
            cg.EnvironmentRigId,
            cg.AttributeCredits,
            cg.SkillCredits,
            Array.AsReadOnly(cg.PrimaryStartAreas.ToArray()),
            Array.AsReadOnly(cg.SecondaryStartAreas.ToArray()),
            prices.ToFrozenDictionary(),
            Each(cg.Templates, Template),
            sexes.ToFrozenDictionary());
    }

    private static GenesisTemplate Template(TemplateChoice t)
    {
        return new(
        t.Name,
        t.IconId,
        t.Title,
        new GenesisAttributeSpread(t.Strength, t.Endurance, t.Coordination, t.Quickness, t.Focus, t.Self),
        Idents(t.NormalSkills),
        Idents(t.PrimarySkills));
    }

    private static GenesisSexOptions Sex(int tag, SexChoice sex)
    {
        return new(
        tag,
        sex.Name,
        sex.Scale,
        sex.RigId,
        sex.SoundBookId,
        sex.IconId,
        sex.BaseColorTableId,
        sex.SkinColorTableSetId,
        sex.EffectBookId,
        sex.MotionBookId,
        sex.ManeuverBookId,
        ObjDesc(sex.BaseLook),
        Array.AsReadOnly(sex.HairColors.ToArray()),
        Each(sex.HairStyles, static h => new GenesisHairStyle(h.IconId, h.Bald, h.AlternateRigId, ObjDesc(h.Look))),
        Array.AsReadOnly(sex.EyeColors.ToArray()),
        Each(sex.EyeStrips, static e => new GenesisEyeStrip(e.IconId, e.BaldIconId, ObjDesc(e.Look), ObjDesc(e.BaldLook))),
        Each(sex.NoseStrips, FrontStrip),
        Each(sex.MouthStrips, FrontStrip),
        Gear(sex.Headgear),
        Gear(sex.Shirts),
        Gear(sex.Pants),
        Gear(sex.Footwear),
        Array.AsReadOnly(sex.ClothingColors.ToArray()));
    }

    private static GenesisFaceStrip FrontStrip(FaceChoice strip) => new(strip.IconId, ObjDesc(strip.Look));

    private static IReadOnlyList<GenesisGearOption> Gear(List<GearChoice> gear)
    {
        return Each(gear, static g => new GenesisGearOption(g.Name, g.WardrobeId, g.WeenieDefault));
    }

    private static GenesisObjDesc ObjDesc(DatObjDesc objRefDsc)
    {
        return new(
        objRefDsc.ColorTableId,
        Each(objRefDsc.ColorSwaps, static palette => new GenesisSubPalette(palette.ColorTableId, palette.Offset, palette.Count)),
        Each(objRefDsc.TextureSwaps, static change => new GenesisTextureSwap(change.PartIndex, change.OldTextureId, change.NewTextureId)),
        Each(objRefDsc.PartSwaps, static change => new GenesisAnimPartSwap(change.PartIndex, change.PartMeshId)));
    }
}

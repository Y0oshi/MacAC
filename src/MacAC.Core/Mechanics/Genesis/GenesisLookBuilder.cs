namespace MacAC.Mechanics.Genesis;

public sealed record GenesisLook(
    uint SetupId,
    uint BasePaletteId,
    GenesisObjDesc ObjDesc,
    IReadOnlyList<uint> MissingPalSetIds,
    IReadOnlyList<uint> MissingClothingTableIds,
    IReadOnlyList<uint> ClothingTablesMissingBaseEffectForSetup);

public static class GenesisLookBuilder
{
    public const uint HumanRigIdent = 0x02000001u;

    private const uint NoDid = 0xFFFFFFFFu;

    // Palette ranges are stored /8: skin covers 0..191, hair 192..255, eyes 256..319.
    private static readonly (byte Offset, byte Count) SkinSpan = (0, 24);
    private static readonly (byte Offset, byte Count) HairSpan = (24, 8);
    private static readonly (byte Offset, byte Count) EyePtSpan = (32, 8);

    private const uint DenseUnit = 8u;
    private const uint DenseCeiling = 2040u;
    private const uint WholeSwatchSentinel = 2048u;

    public static bool TryConstruct(
        GenesisOptions knobs,
        uint lineageIdent,
        int genderTag,
        GenesisLookChoice pick,
        IGenesisPalSetSource palSets,
        IGenesisGarbTableSource clothingCharts,
        out GenesisLook outcome,
        uint alternateRigIdentOverride = NoDid)
    {
        ArgumentNullException.ThrowIfNull(knobs);
        ArgumentNullException.ThrowIfNull(palSets);
        ArgumentNullException.ThrowIfNull(clothingCharts);

        outcome = default!;
        if (!knobs.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage)
            || !lineage.GendersByKey.TryGetValue(genderTag, out GenesisSexOptions? sex))

            return false;

        Assembly gaze = new Assembly(palSets, clothingCharts);

        var hair = Pick(sex.HairStyles, pick.HairStyle);
        uint rigIdent = SelectRig(sex, hair, alternateRigIdentOverride);

        gaze.Layer(sex.BaseObjDesc);
        if (hair is not null)
            gaze.Layer(hair.ObjDesc);

        gaze.Garment(sex.Headgears, pick.HeadgearStyle, sex.ClothingColors, pick.HeadgearColor, pick.HeadgearShade, rigIdent);
        gaze.Garment(sex.Pants, pick.TrousersStyle, sex.ClothingColors, pick.TrousersColor, pick.TrousersShade, rigIdent);
        gaze.Garment(sex.Shirts, pick.ShirtStyle, sex.ClothingColors, pick.ShirtColor, pick.ShirtShade, rigIdent);
        gaze.Garment(sex.Footwear, pick.FootwearStyle, sex.ClothingColors, pick.FootwearColor, pick.FootwearShade, rigIdent);

        if (Pick(sex.EyeStrips, pick.EyesStrip) is { } eyes)
            gaze.Layer(hair?.Bald == true ? eyes.BaldObjDesc : eyes.ObjDesc);
        if (Pick(sex.NoseStrips, pick.NoseStrip) is { } nose)
            gaze.Layer(nose.ObjDesc);
        if (Pick(sex.MouthStrips, pick.MouthStrip) is { } mouth)
            gaze.Layer(mouth.ObjDesc);

        gaze.Tint(sex.SkinPalSetId, pick.SkinShade, SkinSpan);
        if (Pick(sex.HairColors, pick.HairColor) is { } hairPalSetIdent)
            gaze.Tint(hairPalSetIdent, pick.HairShade, HairSpan);
        if (Pick(sex.EyeColors, pick.EyeColor) is { } eyePtSwatchIdent)
            gaze.SubSwatch(eyePtSwatchIdent, EyePtSpan);

        outcome = gaze.Finish(rigIdent, sex.BasePaletteId);
        return true;
    }

    private static T? Pick<T>(IReadOnlyList<T> knobs, uint ordinal) where T : class
    {
        return ordinal != GenesisLookChoice.Unset && ordinal < (uint)knobs.Count ? knobs[(int)ordinal] : null;
    }

    private static uint? Pick(IReadOnlyList<uint> knobs, uint ordinal)
    {
        return ordinal != GenesisLookChoice.Unset && ordinal < (uint)knobs.Count ? knobs[(int)ordinal] : null;
    }

    private static uint SelectRig(GenesisSexOptions sex, GenesisHairStyle? hair, uint forced)
    {
        uint rig = sex.SetupId;
        if (hair is { AlternateSetup: not 0 and not NoDid })
            rig = hair.AlternateSetup;
        if (forced != NoDid)
            rig = forced;
        return rig is 0 or NoDid ? HumanRigIdent : rig;
    }

    private sealed class Assembly(IGenesisPalSetSource palSets, IGenesisGarbTableSource clothingCharts)
    {
        private readonly List<GenesisSubPalette> _subSwatches = [];
        private readonly List<GenesisTextureSwap> _textures = [];
        private readonly List<GenesisAnimPartSwap> _pieces = [];
        private readonly List<uint> _absentPalSets = [];
        private readonly List<uint> _absentCharts = [];
        private readonly List<uint> _chartsWithoutBaseFx = [];

        public void Layer(GenesisObjDesc piece)
        {
            _subSwatches.AddRange(piece.SubPalettes);
            _textures.AddRange(piece.TextureChanges);
            _pieces.AddRange(piece.AnimPartChanges);
        }

        public void SubSwatch(uint swatchIdent, (byte Offset, byte Count) span)
        {
            _subSwatches.Add(new GenesisSubPalette(swatchIdent, span.Offset, span.Count));
        }

        // Adds the shade-selected palette of a pal set, or records the set as missing
        public void Tint(uint palSetIdent, double shade, (byte Offset, byte Count) span)
        {
            if (palSets.TryFetchPalSet(palSetIdent) is not { } set)
            {
                _absentPalSets.Add(palSetIdent);
                return;
            }
            int at = GenesisPalSetRules.FetchSwatchOrdinal(set.PaletteIds.Count, shade);
            if (at >= 0)
                SubSwatch(set.PaletteIds[at], span);
        }

        public void Garment(
            IReadOnlyList<GenesisGearOption> knobs,
            uint stylingOrdinal,
            IReadOnlyList<uint> colours,
            uint colourOrdinal,
            double shade,
            uint corpusRigIdent)
        {
            if (Pick(knobs, stylingOrdinal) is not { } gear)
                return;
            if (clothingCharts.TryFetchClothingChart(gear.ClothingTableId) is not { } chart)
            {
                _absentCharts.Add(gear.ClothingTableId);
                return;
            }

            if (chart.BaseEffectsBySetupId.TryGetValue(corpusRigIdent, out GenesisGarbBaseEffect? fx))
            {
                _pieces.AddRange(fx.PartChanges);
                _textures.AddRange(fx.TextureChanges);
            }
            else
            {
                _chartsWithoutBaseFx.Add(gear.ClothingTableId);
            }

            if (Pick(colours, colourOrdinal) is not { } blueprintIdent)
                return;
            if (!chart.PaletteTemplatesById.TryGetValue(blueprintIdent, out GenesisGarbPaletteTemplate? blueprint))
                return;

            foreach (GenesisGarbSubPaletteChoice choice in blueprint.Choices)
            {
                if (palSets.TryFetchPalSet(choice.PalSetId) is not { } set)
                {
                    _absentPalSets.Add(choice.PalSetId);
                    break;
                }

                int at = GenesisPalSetRules.FetchSwatchOrdinal(set.PaletteIds.Count, shade);
                if (at < 0)
                    continue;

                uint swatchIdent = set.PaletteIds[at];
                foreach (GenesisGarbSubPaletteRange span in choice.Ranges)
                    _subSwatches.Add(new GenesisSubPalette(swatchIdent, BundleShift(span.Offset), BundleTally(span.NumColors)));
            }
        }

        public GenesisLook Finish(uint rigIdent, uint baseSwatchIdent)
        {
            return new(
            rigIdent,
            baseSwatchIdent,
            new GenesisObjDesc(baseSwatchIdent, _subSwatches.AsReadOnly(), _textures.AsReadOnly(), _pieces.AsReadOnly()),
            _absentPalSets.AsReadOnly(),
            _absentCharts.AsReadOnly(),
            _chartsWithoutBaseFx.AsReadOnly());
        }
    }

    private static byte BundleShift(uint offset)
    {
        if (offset % DenseUnit is not 0 || offset > DenseCeiling)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                "Clothing subpalette range offset doesn't fit the packed *8 byte "
                + "convention (wanted a multiple of 8 in [0, 2040])");
        }
        return (byte)(offset / DenseUnit);
    }

    private static byte BundleTally(uint count)
    {
        if (count == WholeSwatchSentinel)
            return 0;
        if (count % DenseUnit is not 0 || count > DenseCeiling)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "Clothing subpalette range color count doesn't fit the packed *8 byte "
                + "convention (wanted a multiple of 8 in [0, 2040], or precisely 2048 "
                + "for the whole-palette sentinel)");
        }
        return (byte)(count / DenseUnit);
    }
}

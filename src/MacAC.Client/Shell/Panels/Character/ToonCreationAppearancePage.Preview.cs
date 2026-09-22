using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationAppearancePage
{
    internal IClientChargenPreviewControl? PreviewControl { get; set; }

    internal IGenesisPalSetSource? PalSetSrc { get; set; }

    internal IGenesisGarbTableSource? ClothingChartSrc { get; set; }

    internal IGenesisPaletteColorSource? SwatchTintSrc { get; set; }

    internal IChargenSwatchBitmapOrigin? SwatchTextureSrc { get; set; }

    internal WidgetViewport? Viewport { get; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _femaleBtn?.OnClick = null;
        _maleBtn?.OnClick = null;
        _faceBtn?.OnClick = null;
        _clothesBtn?.OnClick = null;
        foreach (WidgetBtn spin in _spins.Values)
            spin.OnClickAt = null;
        _spins.Clear();
        foreach (WidgetBtn? swatch in _swatches)
        {
            swatch?.OnClick = null;
        }
        _shadeRoll?.ScalarAltered = null;
        _spinClockwise?.OnClick = null;
        _spinCounterClockwise?.OnClick = null;
        _zoomIn?.OnClick = null;
        _zoomOut?.OnClick = null;
        PreviewControl = null;
        PalSetSrc = null;
        ClothingChartSrc = null;
        SwatchTintSrc = null;
    }

    internal void Randomize()
    {
        if (_destroyed)
            return;
        if (_latestChoice == Pick.Clothes)
            _bindings.RandomizeClothing?.Invoke();
        else
            _bindings.RandomizeAppearance?.Invoke();
    }

    internal static uint CycleOrdinal(uint latest, int diff, int tally, bool allowUnset)
    {
        if (tally <= 0)
            return Unset;

        if (allowUnset)
        {
            int cur = latest == Unset ? tally : (int)latest;
            int dims = tally + 1;
            int upcoming = Mod(cur + diff, dims);
            return upcoming == tally ? Unset : (uint)upcoming;
        }

        if (latest == Unset)
        {
            int fromUnset = -1 + diff;
            return fromUnset < 0 ? (uint)(tally - 1) : fromUnset >= tally ? 0u : (uint)fromUnset;
        }
        return (uint)Mod((int)latest + diff, tally);
    }

    private void SelectChoice(Pick choice)
    {
        if (_destroyed)
            return;
        _latestChoice = choice;
        _latestPiece = choice == Pick.Face ? ClientPart.Hair : ClientPart.Headgear;
        ImposeChoiceVis();
        RenewTintAndShadeControlsFromCurrentCapture();
    }

    private void PickPiece(ClientPart piece)
    {
        if (_destroyed)
            return;
        StandardizeChoiceOnPick(piece);
        _latestPiece = piece;
        RenewTintAndShadeControlsFromCurrentCapture();
    }

    private void PickTint(int ordinal)
    {
        if (_destroyed)
            return;
        var lens = _bindings.View();
        if (lens is null)
            return;
        var capture = lens.Snapshot;
        if (!TryFetchGender(lens, capture, out GenesisSexOptions? gender))
            return;
        var socket = TintSocketFor(_latestPiece);
        if (socket is null)
            return;

        int tally = TintTally(_latestPiece, gender);
        if (ordinal >= tally)
            return;

        _bindings.SetAppearanceIndex?.Invoke(socket.Value, (uint)ordinal);
    }

    private void ImposeChoiceVis()
    {
        _faceChoices?.Visible = _latestChoice == Pick.Face;
        _clothesChoices?.Visible = _latestChoice == Pick.Clothes;
        _faceBtn?.Selected = _latestChoice == Pick.Face;
        _clothesBtn?.Selected = _latestChoice == Pick.Clothes;
    }

    private void AttachSpin(WidgetElem sheetTrunk, uint ident, ClientPart piece)
    {
        WidgetBtn? spin = Find<WidgetBtn>(sheetTrunk, ident);
        if (spin is null)
            return;
        _spins[piece] = spin;

        if (piece == ClientPart.Skin)
        {
            spin.OnClickAt = (_, _) => PickPiece(ClientPart.Skin);
            return;
        }

        spin.OnClickAt = (x, _) =>
        {
            if (x is >= (int)DecrementZoneBegin and < (int)IncrementZoneBegin)
                CycleStyling(piece, -1);
            else if (x is >= (int)IncrementZoneBegin and < (int)IncrementZoneFinish)
                CycleStyling(piece, +1);
            else
                PickPiece(piece);
        };
    }

    private void StandardizeChoiceOnPick(ClientPart piece)
    {
        var socket = StylingSocketFor(piece);
        if (socket is null)
            return;

        var lens = _bindings.View();
        if (lens is null)
            return;
        var capture = lens.Snapshot;
        if (!TryFetchGender(lens, capture, out GenesisSexOptions? gender))
            return;

        int tally = StylingTally(piece, gender);
        if (tally <= 0)
            return;
        uint latest = StylingLatest(piece, capture.Appearance);

        uint normalized;
        if (piece == ClientPart.Headgear)
        {
            if (latest != Unset && latest >= (uint)tally)
                normalized = Unset;
            else
                return;
        }
        else
        {
            if (latest != Unset && latest >= (uint)tally)
                normalized = 0u;
            else if (latest == Unset)
                normalized = (uint)(tally - 1);
            else
                return;
        }

        _bindings.SetAppearanceIndex?.Invoke(socket.Value, normalized);
    }

    private void CycleStyling(ClientPart piece, int diff)
    {
        if (_destroyed)
            return;
        if (piece == ClientPart.Eyes && _eyesArrowsDisabled)
        {
            PickPiece(piece);
            return;
        }

        var lens = _bindings.View();
        if (lens is null)
            return;
        var capture = lens.Snapshot;
        if (!TryFetchGender(lens, capture, out GenesisSexOptions? gender))
            return;

        var socket = StylingSocketFor(piece);
        if (socket is null)
        {
            PickPiece(piece);
            return;
        }

        int tally = StylingTally(piece, gender);
        uint latest = StylingLatest(piece, capture.Appearance);
        uint upcoming = CycleOrdinal(latest, diff, tally, allowUnset: piece == ClientPart.Headgear);
        _bindings.SetAppearanceIndex?.Invoke(socket.Value, upcoming);
        PickPiece(piece);
    }

    private static int Mod(int val, int modulus) =>
        ((val % modulus) + modulus) % modulus;

    private void AssignShadeFromScalar(float scalar)
    {
        if (_destroyed)
            return;
        var socket = ShadeSocketFor(_latestPiece);
        if (socket is null)
            return;
        _bindings.SetShade?.Invoke(socket.Value, scalar);
    }

    private void AssignSpinLegend(ClientPart piece, string tag)
    {
        if (!_spins.TryGetValue(piece, out WidgetBtn? spin))
            return;
        if (_bindings.ResolveText?.Invoke(tag) is { } phrase)
            spin.Label = phrase;
    }

    private Func<uint>? AssembleSwatchTextureLocator(bool shown, GenesisSwatchRgb? rgb)
    {
        if (!shown)
            return () => SwatchTextureSrc?.BlankSpotTexture ?? 0u;
        return rgb is { } c ? (() => SwatchTextureSrc?.FetchEngagedSpotTexture(c) ?? 0u) : null;
    }

    private GenesisSwatchRgb?[] CalculateSwatchTints(
        GenesisSexOptions gender, SimToonGenesisAppearance looks)
    {
        if (PalSetSrc is not { } palSets || SwatchTintSrc is not { } tints)
            return VacantSwatchTints;

        GenesisSwatchRgb?[] outcome = new GenesisSwatchRgb?[SwatchIdents.Length];
        switch (_latestPiece)
        {
            case ClientPart.Hair:
                PopulatePalSetClan(outcome, gender.HairColors, palSets, tints, GenesisSwatchResolver.HairSpecimenOrdinal);
                break;
            case ClientPart.Eyes:
                PopulateStraightClan(outcome, gender.EyeColors, tints, GenesisSwatchResolver.EyePtSpecimenOrdinal);
                break;
            case ClientPart.Nose:
            case ClientPart.Mouth:
            case ClientPart.Skin:
                if (GenesisSwatchResolver.TryFetchPalSetAverageTint(
                        palSets, tints, gender.SkinPalSetId,
                        GenesisSwatchResolver.SkinClanSpecimenOrdinal, out GenesisSwatchRgb skin))
                {
                    outcome[0] = skin;
                }
                break;
            case ClientPart.Headgear:
                PopulateClothingClan(outcome, gender, gender.Headgears, looks.HeadgearStyle, palSets, tints);
                break;
            case ClientPart.Shirt:
                PopulateClothingClan(outcome, gender, gender.Shirts, looks.ShirtStyle, palSets, tints);
                break;
            case ClientPart.Trousers:
                PopulateClothingClan(outcome, gender, gender.Pants, looks.TrousersStyle, palSets, tints);
                break;
            case ClientPart.Footwear:
                PopulateClothingClan(outcome, gender, gender.Footwear, looks.FootwearStyle, palSets, tints);
                break;
        }
        return outcome;
    }

    private static void PopulatePalSetClan(
        GenesisSwatchRgb?[] outcome,
        IReadOnlyList<uint> palSetIdents,
        IGenesisPalSetSource palSets,
        IGenesisPaletteColorSource tints,
        int specimenOrdinal)
    {
        int tally = Math.Min(outcome.Length, palSetIdents.Count);
        for (int idx = 0; idx < tally; ++idx)
        {
            if (GenesisSwatchResolver.TryFetchPalSetAverageTint(
                    palSets, tints, palSetIdents[idx], specimenOrdinal, out GenesisSwatchRgb c))

                outcome[idx] = c;
        }
    }

    private static void PopulateStraightClan(
        GenesisSwatchRgb?[] outcome,
        IReadOnlyList<uint> swatchIdents,
        IGenesisPaletteColorSource tints,
        int specimenOrdinal)
    {
        int tally = Math.Min(outcome.Length, swatchIdents.Count);
        for (int idx = 0; idx < tally; ++idx)
        {
            if (GenesisSwatchResolver.TryFetchStraightTint(tints, swatchIdents[idx], specimenOrdinal, out GenesisSwatchRgb c))
                outcome[idx] = c;
        }
    }

    private void PopulateClothingClan(
        GenesisSwatchRgb?[] outcome,
        GenesisSexOptions gender,
        IReadOnlyList<GenesisGearOption> gearKnobs,
        uint stylingOrdinal,
        IGenesisPalSetSource palSets,
        IGenesisPaletteColorSource tints)
    {
        if (ClothingChartSrc is not { } clothingCharts)
            return;
        if (stylingOrdinal == Unset || stylingOrdinal >= (uint)gearKnobs.Count)
            return;

        uint clothingChartIdent = gearKnobs[(int)stylingOrdinal].ClothingTableId;
        PopulateClothingClanRest(clothingCharts, clothingChartIdent, gender, outcome, palSets, tints);
    }

    private void PopulateClothingClanRest(IGenesisGarbTableSource clothingCharts, uint clothingChartIdent, GenesisSexOptions gender, GenesisSwatchRgb?[] outcome, IGenesisPalSetSource palSets, IGenesisPaletteColorSource tints)
    {
        var clothingTints = gender.ClothingColors;
        int tally = Math.Min(outcome.Length, clothingTints.Count);
        for (int idx = 0; idx < tally; ++idx)
        {
            if (!GenesisSwatchResolver.TryFetchClothingSwatchPalSetIdent(
                    clothingCharts, clothingChartIdent, clothingTints[idx], out uint palSetIdent))

                continue;
            if (GenesisSwatchResolver.TryFetchPalSetAverageTint(
                    palSets, tints, palSetIdent, GenesisSwatchResolver.ClothingSpecimenOrdinal, out GenesisSwatchRgb c))

                outcome[idx] = c;
        }
    }

    private static Vector4 ToTintTint(GenesisSwatchRgb rgb) =>
        new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, 1f);

    private static T? Find<T>(WidgetElem trunk, uint ident) where T : WidgetElem =>
        WidgetElem.SeekDescendant(trunk, ident) as T;

    private void ReassemblePreview(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        if (PreviewControl is null
            || capture.HeritageId is 0u
            || capture.GenderKey is 0u)

            return;

        var appearance = capture.Appearance;
        GenesisLookChoice pick = new GenesisLookChoice(
            appearance.EyesStrip, appearance.NoseStrip, appearance.MouthStrip,
            appearance.HairStyle, appearance.HairColor, appearance.EyeColor,
            appearance.HeadgearStyle, appearance.HeadgearColor,
            appearance.ShirtStyle, appearance.ShirtColor,
            appearance.TrousersStyle, appearance.TrousersColor,
            appearance.FootwearStyle, appearance.FootwearColor,
            appearance.SkinShade, appearance.HairShade, appearance.HeadgearShade,
            appearance.ShirtShade, appearance.TrousersShade, appearance.FootwearShade);

        PreviewControl.Rebuild(lens.Options, capture.HeritageId, (int)capture.GenderKey, pick);
    }

    private static GenesisAppearanceSlot? StylingSocketFor(ClientPart piece)
    {
        return piece switch
        {
            ClientPart.Hair => GenesisAppearanceSlot.HairStyle,
            ClientPart.Eyes => GenesisAppearanceSlot.EyesStrip,
            ClientPart.Nose => GenesisAppearanceSlot.NoseStrip,
            ClientPart.Mouth => GenesisAppearanceSlot.MouthStrip,
            ClientPart.Headgear => GenesisAppearanceSlot.HeadgearStyle,
            ClientPart.Shirt => GenesisAppearanceSlot.ShirtStyle,
            ClientPart.Trousers => GenesisAppearanceSlot.TrousersStyle,
            ClientPart.Footwear => GenesisAppearanceSlot.FootwearStyle,
            _ => null, // Skin
        };
    }

    private static int StylingTally(ClientPart piece, GenesisSexOptions gender)
    {
        return piece switch
        {
            ClientPart.Hair => gender.HairStyles.Count,
            ClientPart.Eyes => gender.EyeStrips.Count,
            ClientPart.Nose => gender.NoseStrips.Count,
            ClientPart.Mouth => gender.MouthStrips.Count,
            ClientPart.Headgear => gender.Headgears.Count,
            ClientPart.Shirt => gender.Shirts.Count,
            ClientPart.Trousers => gender.Pants.Count,
            ClientPart.Footwear => gender.Footwear.Count,
            _ => 0,
        };
    }

    private static uint StylingLatest(ClientPart piece, SimToonGenesisAppearance appearance)
    {
        return piece switch
        {
            ClientPart.Hair => appearance.HairStyle,
            ClientPart.Eyes => appearance.EyesStrip,
            ClientPart.Nose => appearance.NoseStrip,
            ClientPart.Mouth => appearance.MouthStrip,
            ClientPart.Headgear => appearance.HeadgearStyle,
            ClientPart.Shirt => appearance.ShirtStyle,
            ClientPart.Trousers => appearance.TrousersStyle,
            ClientPart.Footwear => appearance.FootwearStyle,
            _ => Unset,
        };
    }

    private static GenesisAppearanceSlot? TintSocketFor(ClientPart piece)
    {
        return piece switch
        {
            ClientPart.Hair => GenesisAppearanceSlot.HairColor,
            ClientPart.Eyes => GenesisAppearanceSlot.EyeColor,
            ClientPart.Headgear => GenesisAppearanceSlot.HeadgearColor,
            ClientPart.Shirt => GenesisAppearanceSlot.ShirtColor,
            ClientPart.Trousers => GenesisAppearanceSlot.TrousersColor,
            ClientPart.Footwear => GenesisAppearanceSlot.FootwearColor,
            _ => null,
        };
    }

    private static int TintTally(ClientPart piece, GenesisSexOptions gender)
    {
        return piece switch
        {
            ClientPart.Hair => gender.HairColors.Count,
            ClientPart.Eyes => gender.EyeColors.Count,
            ClientPart.Headgear or ClientPart.Shirt or ClientPart.Trousers or ClientPart.Footwear =>
                gender.ClothingColors.Count,
            _ => 0,
        };
    }

    private static uint TintLatest(ClientPart piece, SimToonGenesisAppearance appearance)
    {
        return piece switch
        {
            ClientPart.Hair => appearance.HairColor,
            ClientPart.Eyes => appearance.EyeColor,
            ClientPart.Headgear => appearance.HeadgearColor,
            ClientPart.Shirt => appearance.ShirtColor,
            ClientPart.Trousers => appearance.TrousersColor,
            ClientPart.Footwear => appearance.FootwearColor,
            _ => Unset,
        };
    }

    private static GenesisShadeSlot? ShadeSocketFor(ClientPart piece)
    {
        return piece switch
        {
            ClientPart.Hair => GenesisShadeSlot.Hair,
            ClientPart.Nose => GenesisShadeSlot.Skin,
            ClientPart.Mouth => GenesisShadeSlot.Skin,
            ClientPart.Skin => GenesisShadeSlot.Skin,
            ClientPart.Headgear => GenesisShadeSlot.Headgear,
            ClientPart.Shirt => GenesisShadeSlot.Shirt,
            ClientPart.Trousers => GenesisShadeSlot.Trousers,
            ClientPart.Footwear => GenesisShadeSlot.Footwear,
            _ => null, // Eyes.
        };
    }

    private static double ShadeLatest(GenesisShadeSlot socket, SimToonGenesisAppearance appearance)
    {
        return socket switch
        {
            GenesisShadeSlot.Skin => appearance.SkinShade,
            GenesisShadeSlot.Hair => appearance.HairShade,
            GenesisShadeSlot.Headgear => appearance.HeadgearShade,
            GenesisShadeSlot.Shirt => appearance.ShirtShade,
            GenesisShadeSlot.Trousers => appearance.TrousersShade,
            GenesisShadeSlot.Footwear => appearance.FootwearShade,
            _ => 0.0,
        };
    }

    private static bool IsClothesConcealedLineage(uint lineageIdent)
    {
        return lineageIdent is ((uint)GenesisHeritage.Gearknight)
        or ((uint)GenesisHeritage.Olthoi)
        or ((uint)GenesisHeritage.OlthoiAcid);
    }

    private static bool TryFetchGender(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GenesisSexOptions? gender)
    {
        gender = null;
        if (capture.HeritageId is 0u || capture.GenderKey is 0u)
            return false;
        return !lens.Options.TryFetchLineage(capture.HeritageId, out GenesisHeritageOptions? lineage)
            ? false
            : lineage.GendersByKey.TryGetValue((int)capture.GenderKey, out gender);
    }
}

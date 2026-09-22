using System.Numerics;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationAppearancePage
{
    internal void Refresh(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        if (_destroyed)
            return;

        _femaleBtn?.Selected = capture.GenderKey == 2u;
        _maleBtn?.Selected = capture.GenderKey == 1u;

        bool clothesConcealed = IsClothesConcealedLineage(capture.HeritageId);
        _clothesBtn?.Visible = !clothesConcealed;
        if (_spins.TryGetValue(ClientPart.Nose, out WidgetBtn? noseSpin))
            noseSpin.Visible = !clothesConcealed;
        if (_spins.TryGetValue(ClientPart.Mouth, out WidgetBtn? mouthSpin))
            mouthSpin.Visible = !clothesConcealed;
        _eyesArrowsDisabled = clothesConcealed;
        if (clothesConcealed)
        {
            _latestChoice = Pick.Face;
            _latestPiece = ClientPart.Hair;
        }
        ImposeChoiceVis();

        // GF-6: heritage-flavored, index-independent - no gender needed.
        RenewSpinLegends(capture.HeritageId);

        RenewTintAndShadeControls(lens, capture);
        ReassemblePreview(lens, capture);
    }

    private void RenewTintAndShadeControlsFromCurrentCapture()
    {
        var lens = _bindings.View();
        if (lens is not null)
            RenewTintAndShadeControls(lens, lens.Snapshot);
    }

    private void RenewTintAndShadeControls(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        foreach ((ClientPart spinPiece, WidgetBtn spin) in _spins)
        {
            spin.TrySetCanonPhase(
                spinPiece == _latestPiece
                    ? WidgetButtonStateMachine.Highlight
                    : WidgetButtonStateMachine.Normal);
        }

        var tintSocket = TintSocketFor(_latestPiece);
        uint latestTint = tintSocket is null ? Unset : TintLatest(_latestPiece, capture.Appearance);
        for (int idx = 0; idx < _swatchTopLayers.Length; ++idx)
        {
            if (_swatchTopLayers[idx] is { } topLayer)
                topLayer.Visible = tintSocket is not null && latestTint == (uint)idx;
        }

        bool swatchGenderSettled = TryFetchGender(lens, capture, out GenesisSexOptions? swatchGender);
        int tintTally = tintSocket is not null && swatchGenderSettled
            ? TintTally(_latestPiece, swatchGender!)
            : 0;
        int readoutTally = tintSocket is not null ? tintTally : 1;

        GenesisSwatchRgb?[] swatchTints = swatchGenderSettled
            ? CalculateSwatchTints(swatchGender!, capture.Appearance)
            : new GenesisSwatchRgb?[SwatchIdents.Length];

        for (int idx = 0; idx < _swatches.Length; ++idx)
        {
            if (_swatches[idx] is not { } swatch)
                continue;
            bool shown = idx < readoutTally;
            swatch.Visible = true;
            GenesisSwatchRgb? rgb = shown ? swatchTints[idx] : null;
            swatch.Tint = rgb is { } c ? ToTintTint(c) : Vector4.One;
            swatch.TintTagFaceLocator = AssembleSwatchTextureLocator(shown, rgb);
        }

        if (_gradCircle is not null)
        {
            bool isEyes = _latestPiece == ClientPart.Eyes;
            _gradCircle.Visible = true;
            int gradOrdinal = isEyes
                ? -1
                : tintSocket is null
                    ? 0
                    : (int)TintLatest(_latestPiece, capture.Appearance);
            GenesisSwatchRgb? gradTint =
                gradOrdinal >= 0 && gradOrdinal < swatchTints.Length ? swatchTints[gradOrdinal] : null;
            _gradCircle.Tint = gradTint is { } gc ? ToTintTint(gc) : Vector4.One;
            uint gradTexture = SwatchTextureSrc is { } textures
                ? (isEyes ? textures.GradPlugTexture : textures.GradDiskTexture)
                : 0u;
            _gradCircle.CoreImageTexture = gradTexture;
        }

        var shadeSocket = ShadeSocketFor(_latestPiece);
        if (_shadeRoll is null)
            return;
        _shadeRoll.Visible = shadeSocket is not null;
        if (shadeSocket is { } socket)
        {
            double shade = ShadeLatest(socket, capture.Appearance);
            float scalar = shade < 0.0 ? 0f : (float)Math.Clamp(shade, 0.0, 1.0);
            _shadeRoll.AssignScalarLocus(scalar);
        }
    }

    private void RenewSpinLegends(uint lineageIdent)
    {
        (string hairTag, string eyesTag, string skinTag) = lineageIdent switch
        {
            (uint)GenesisHeritage.Gearknight => (
                "ID_CharGen_GearText_HairButton",
                "ID_CharGen_GearText_EyesButton",
                "ID_CharGen_GearText_SkinButton"),
            (uint)GenesisHeritage.Olthoi or (uint)GenesisHeritage.OlthoiAcid => (
                "ID_CharGen_OlthoiText_HairButton",
                "ID_CharGen_OlthoiText_EyesButton",
                "ID_CharGen_OlthoiText_SkinButton"),
            _ => ("ID_CharGen_HairStyle", "ID_CharGen_Eyes", "ID_CharGen_Skin"),
        };

        AssignSpinLegend(ClientPart.Hair, hairTag);
        AssignSpinLegend(ClientPart.Eyes, eyesTag);
        AssignSpinLegend(ClientPart.Skin, skinTag);
    }
}

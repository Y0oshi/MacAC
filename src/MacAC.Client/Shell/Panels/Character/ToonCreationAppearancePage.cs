using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationAppearancePage : IDisposable
{
    private const uint Unset = SimToonGenesisAppearance.Unset;

    internal enum ClientPart
    {
        Hair = 1,
        Eyes = 2,
        Nose = 3,
        Mouth = 4,
        Skin = 5,
        Headgear = 6,
        Shirt = 7,
        Trousers = 8,
        Footwear = 9,
    }

    private enum Pick
    {
        Face,
        Clothes,
    }

    internal const uint FemaleBtnIdent = 0x100003A7u;

    internal const uint MaleBtnIdent = 0x100003A8u;

    internal const uint FaceBtnIdent = 0x100003A9u;

    internal const uint ClothesBtnIdent = 0x100003AAu;

    internal const uint FaceChoicesIdent = 0x100003AEu;

    internal const uint ClothesChoicesIdent = 0x100003B4u;

    internal const uint HairSpinIdent = 0x100003AFu;

    internal const uint EyesSpinIdent = 0x100003B0u;

    internal const uint NoseSpinIdent = 0x100003B1u;

    internal const uint MouthSpinIdent = 0x100003B2u;

    internal const uint SkinSpinIdent = 0x100003B3u;

    internal const uint HeadgearSpinIdent = 0x100003B5u;

    internal const uint ShirtSpinIdent = 0x100003B6u;

    internal const uint TrousersSpinIdent = 0x100003B7u;

    internal const uint FootwearSpinIdent = 0x100003B8u;

    internal const uint SpinClockwiseIdent = 0x10000323u;

    internal const uint SpinCounterClockwiseIdent = 0x10000324u;

    internal const uint ZoomInIdent = 0x10000325u;

    internal const uint ZoomOutIdent = 0x10000326u;

    internal const uint GradCircleIdent = 0x1000030Eu;

    internal const uint ShadeRollIdent = 0x10000321u;

    internal const uint ViewRectIdent = 0x100003BBu;

    internal const uint HelpPhraseIdent = 0x100003ABu;

    private const uint HelpRollRelativeIdent = 0x100002E7u;

    internal static readonly uint[] SwatchIdents =
    [
        0x1000030Fu, 0x10000310u, 0x10000311u, 0x10000312u, 0x10000313u,
        0x10000314u, 0x10000315u, 0x10000316u, 0x10000317u,
    ];

    internal static readonly uint[] SwatchTopLayerIdents =
    [
        0x10000318u, 0x10000319u, 0x1000031Au, 0x1000031Bu, 0x1000031Cu,
        0x1000031Du, 0x1000031Eu, 0x1000031Fu, 0x10000320u,
    ];

    private const float DecrementZoneBegin = 80f;

    private const float IncrementZoneBegin = 127f;

    private const float IncrementZoneFinish = 174f;

    private readonly ToonCreationEngineWiring _bindings;

    private readonly WidgetBtn? _femaleBtn;

    private readonly WidgetBtn? _maleBtn;

    private readonly WidgetBtn? _faceBtn;

    private readonly WidgetBtn? _clothesBtn;

    private readonly WidgetElem? _faceChoices;

    private readonly WidgetElem? _clothesChoices;

    private readonly Dictionary<ClientPart, WidgetBtn> _spins = [];

    private readonly WidgetBtn?[] _swatches = new WidgetBtn?[SwatchIdents.Length];

    private readonly WidgetElem?[] _swatchTopLayers = new WidgetElem?[SwatchTopLayerIdents.Length];

    private readonly WidgetScroller? _shadeRoll;

    private readonly WidgetBtn? _spinClockwise;

    private readonly WidgetBtn? _spinCounterClockwise;

    private readonly WidgetBtn? _zoomIn;

    private readonly WidgetBtn? _zoomOut;

    private readonly WidgetPhrase? _helpPhrase;

    private readonly WidgetDatElement? _gradCircle;

    private Pick _latestChoice = Pick.Face;

    private ClientPart _latestPiece = ClientPart.Hair;

    private bool _eyesArrowsDisabled;

    private bool _destroyed;

    internal ToonCreationAppearancePage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings)
    {
        _bindings = mappings;

        _femaleBtn = Find<WidgetBtn>(sheetTrunk, FemaleBtnIdent);
        _femaleBtn?.OnClick = () => _bindings.SelectGender(2u);
        _maleBtn = Find<WidgetBtn>(sheetTrunk, MaleBtnIdent);
        _maleBtn?.OnClick = () => _bindings.SelectGender(1u);

        _faceBtn = Find<WidgetBtn>(sheetTrunk, FaceBtnIdent);
        _faceBtn?.OnClick = () => SelectChoice(Pick.Face);
        _clothesBtn = Find<WidgetBtn>(sheetTrunk, ClothesBtnIdent);
        _clothesBtn?.OnClick = () => SelectChoice(Pick.Clothes);

        _faceChoices = Find<WidgetElem>(sheetTrunk, FaceChoicesIdent);
        _clothesChoices = Find<WidgetElem>(sheetTrunk, ClothesChoicesIdent);

        AttachSpin(sheetTrunk, HairSpinIdent, ClientPart.Hair);
        AttachSpin(sheetTrunk, EyesSpinIdent, ClientPart.Eyes);
        AttachSpin(sheetTrunk, NoseSpinIdent, ClientPart.Nose);
        AttachSpin(sheetTrunk, MouthSpinIdent, ClientPart.Mouth);
        AttachSpin(sheetTrunk, SkinSpinIdent, ClientPart.Skin);
        AttachSpin(sheetTrunk, HeadgearSpinIdent, ClientPart.Headgear);
        AttachSpin(sheetTrunk, ShirtSpinIdent, ClientPart.Shirt);
        AttachSpin(sheetTrunk, TrousersSpinIdent, ClientPart.Trousers);
        AttachSpin(sheetTrunk, FootwearSpinIdent, ClientPart.Footwear);

        for (int idx = 0; idx < SwatchIdents.Length; ++idx)
        {
            WidgetBtn? swatch = Find<WidgetBtn>(sheetTrunk, SwatchIdents[idx]);
            if (swatch is null)
                continue;
            int ordinal = idx;
            swatch.OnClick = () => PickTint(ordinal);
            _swatches[idx] = swatch;
        }

        for (int idx = 0; idx < SwatchTopLayerIdents.Length; ++idx)
            _swatchTopLayers[idx] = Find<WidgetElem>(sheetTrunk, SwatchTopLayerIdents[idx]);

        _shadeRoll = Find<WidgetScroller>(sheetTrunk, ShadeRollIdent);
        _shadeRoll?.ScalarAltered = AssignShadeFromScalar;

        _gradCircle = Find<WidgetDatElement>(sheetTrunk, GradCircleIdent);

        Viewport = Find<WidgetViewport>(sheetTrunk, ViewRectIdent);

        _spinClockwise = Find<WidgetBtn>(sheetTrunk, SpinClockwiseIdent);
        _spinClockwise?.OnClick = () => PreviewControl?.SpinClockwise();
        _spinCounterClockwise = Find<WidgetBtn>(sheetTrunk, SpinCounterClockwiseIdent);
        _spinCounterClockwise?.OnClick = () => PreviewControl?.SpinCounterClockwise();
        _zoomIn = Find<WidgetBtn>(sheetTrunk, ZoomInIdent);
        _zoomIn?.OnClick = () =>
            {
                PreviewControl?.ZoomIn();
                _zoomIn.TrySetCanonPhase(WidgetButtonStateMachine.Highlight);
                _zoomOut?.TrySetCanonPhase(WidgetButtonStateMachine.Normal);
            };
        _zoomOut = Find<WidgetBtn>(sheetTrunk, ZoomOutIdent);
        _zoomOut?.OnClick = () =>
            {
                PreviewControl?.ZoomOut();
                _zoomOut.TrySetCanonPhase(WidgetButtonStateMachine.Highlight);
                _zoomIn?.TrySetCanonPhase(WidgetButtonStateMachine.Normal);
            };

        _helpPhrase = Find<WidgetPhrase>(sheetTrunk, HelpPhraseIdent);
        if (_helpPhrase is not null)
        {
            _helpPhrase.PreserveFinishOnArrangement = false;
            if (Find<WidgetScroller>(_helpPhrase, HelpRollRelativeIdent) is { } helpRoll)
                helpRoll.Model = _helpPhrase.Scroll;
        }

        ImposeChoiceVis();
    }

    private static readonly GenesisSwatchRgb?[] VacantSwatchTints = new GenesisSwatchRgb?[SwatchIdents.Length];
}

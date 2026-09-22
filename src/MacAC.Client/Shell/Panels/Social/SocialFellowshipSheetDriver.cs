using System.Numerics;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed partial class SocialFellowshipSheetDriver
{
    private const uint RosterBboxIdent = 0x10000279u;

    private const uint LabelListingBboxIdent = 0x1000026Fu;

    private const uint BuildBtnIdent = 0x10000274u;

    private const uint FellowshipLabelPhraseIdent = 0x10000276u;

    private const uint LeaderBtnIdent = 0x1000027Bu;

    private const uint QuitBtnIdent = 0x1000027Cu;

    private const uint OpenBtnIdent = 0x1000027Du;

    private const uint RecruitBtnIdent = 0x1000027Eu;

    private const uint DismissBtnIdent = 0x1000027Fu;

    private const uint DisbandBtnIdent = 0x10000280u;

    private const uint IgnoreReqsTickboxIdent = 0x10000270u;

    private const uint AutoAdmitTickboxIdent = 0x10000271u;

    private const uint PortionXpTickboxIdent = 0x10000272u;

    private const uint PortionLootTickboxIdent = 0x10000273u;

    private const uint RankLabelBandIdent = 0x10000282u;

    private const uint RankLabelPhraseIdent = 0x10000283u;

    private const uint RankStatsPhraseIdent = 0x10000284u;

    private const uint RankHealthGaugeIdent = 0x10000285u;

    private const uint RankStaminaGaugeIdent = 0x10000287u;

    private const uint RankManaGaugeIdent = 0x10000289u;

    private const uint KnobStringChartIdent = 0x23000003u;

    private const uint FellowshipStringChartIdent = 0x23000001u;

    private static readonly float[] EvenDividePctChart =
        [1.0f, 0.75f, 0.6f, 0.55f, 0.5f, 0.45f, 0.4f, 0.35f, 0.3111111f, 0.28f];

    private const int UpperFellowshipDims = 9;

    private static readonly Vector4 ParticipantLabelTint = Vector4.One;

    private string? _previousFellowshipLabel;

    private Func<IReadOnlyList<WidgetPhrase.Line>>? _fellowshipLabelStrokesSupplier;

    public sealed record Bindings(
        Func<SimFellowsCapture> Snapshot,
        Func<IEnumerable<SimFellowMemberCapture>> Members,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        Func<string, bool, SimDirectiveResult> Create,
        Func<uint, SimDirectiveResult> Recruit,
        Func<uint, SimDirectiveResult> Dismiss,
        Func<bool, SimDirectiveResult> Quit,
        Func<uint, SimDirectiveResult> AssignLeader,
        Func<bool, SimDirectiveResult> SetOpen,
        Func<bool, SimDirectiveResult> SetPanelOpen,
        PickPhase Selection,
        Func<uint> LocalPlayerGuid,
        Func<CharacterOptionId, bool> CurrentCharacterOption,
        Action<CharacterOptionId, bool> SetCharacterOption,
        Func<uint, uint, string?> ResolveString);

    private readonly record struct FellowRankWidgets(
        WidgetDatElement? NameBand,
        WidgetPhrase? Name,
        WidgetPhrase? Stats,
        WidgetGauge? Health,
        WidgetGauge? Stamina,
        WidgetGauge? Mana);

    private readonly WidgetElem _notInFellowshipCycle;

    private readonly WidgetElem _inFellowshipCycle;

    private readonly Bindings _bindings;

    private readonly WidgetBlueprintRosterBbox? _rosterBbox;

    private readonly WidgetField? _labelField;

    private readonly WidgetBtn? _buildBtn;

    private readonly WidgetPhrase? _fellowshipLabelPhrase;

    private readonly WidgetBtn? _leaderBtn;

    private readonly WidgetBtn? _quitBtn;

    private readonly WidgetBtn? _openBtn;

    private readonly WidgetBtn? _recruitBtn;

    private readonly WidgetBtn? _dismissBtn;

    private readonly WidgetBtn? _disbandBtn;

    private readonly WidgetBtn? _ignoreReqsTickbox;

    private readonly WidgetBtn? _autoAdmitTickbox;

    private readonly WidgetBtn? _portionXpTickbox;

    private readonly WidgetBtn? _portionLootTickbox;

    private readonly Dictionary<uint, FellowRankWidgets> _ranks = [];

    private readonly HashSet<uint> _participantOids = [];

    private uint _chosenFellowOid;

    private long _previousLineupRev = long.MinValue;

    private bool? _previousOpenPhase;

    private bool _sheetShown;

    private readonly string? _openLegend;

    private readonly string? _shutLegend;

    private SocialFellowshipSheetDriver(
        WidgetElem notInFellowshipCycle,
        WidgetElem inFellowshipCycle,
        Bindings mappings,
        WidgetBlueprintRosterBbox? rosterBbox,
        WidgetField? labelField,
        WidgetBtn? buildBtn,
        WidgetPhrase? fellowshipLabelPhrase,
        WidgetBtn? leaderBtn,
        WidgetBtn? quitBtn,
        WidgetBtn? openBtn,
        WidgetBtn? recruitBtn,
        WidgetBtn? dismissBtn,
        WidgetBtn? disbandBtn,
        WidgetBtn? ignoreReqsTickbox,
        WidgetBtn? autoAdmitTickbox,
        WidgetBtn? portionXpTickbox,
        WidgetBtn? portionLootTickbox,
        string? openLegend,
        string? shutLegend)
    {
        _notInFellowshipCycle = notInFellowshipCycle;
        _inFellowshipCycle = inFellowshipCycle;
        _bindings = mappings;
        _rosterBbox = rosterBbox;
        _labelField = labelField;
        _buildBtn = buildBtn;
        _fellowshipLabelPhrase = fellowshipLabelPhrase;
        _leaderBtn = leaderBtn;
        _quitBtn = quitBtn;
        _openBtn = openBtn;
        _recruitBtn = recruitBtn;
        _dismissBtn = dismissBtn;
        _disbandBtn = disbandBtn;
        _ignoreReqsTickbox = ignoreReqsTickbox;
        _autoAdmitTickbox = autoAdmitTickbox;
        _portionXpTickbox = portionXpTickbox;
        _portionLootTickbox = portionLootTickbox;
        _openLegend = openLegend;
        _shutLegend = shutLegend;
    }
}

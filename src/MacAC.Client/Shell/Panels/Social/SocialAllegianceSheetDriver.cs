using System.Numerics;
using MacAC.Sim;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed partial class SocialAllegianceSheetDriver
{
    private const uint SelfLabelPhraseIdent = 0x10000251u;

    private const uint SelfFollowersPhraseIdent = 0x10000252u;

    private const uint SelfGradePhraseIdent = 0x10000253u;

    private const uint MonarchFieldIdent = 0x10000255u;

    private const uint MonarchCaptionPhraseIdent = 0x10000256u;

    private const uint MonarchLabelPhraseIdent = 0x10000257u;

    private const uint MonarchFollowersPhraseIdent = 0x10000258u;

    private const uint PatronFieldIdent = 0x1000025Au;

    private const uint PatronLabelPhraseIdent = 0x1000025Cu;

    private const uint VassalRosterBboxIdent = 0x10000260u;

    private const uint IgnoreReqsTickboxIdent = 0x10000262u;

    private const uint SwearBtnIdent = 0x10000263u;

    private const uint BreakBtnIdent = 0x10000264u;

    private const uint KickBtnIdent = 0x10000265u;

    private const uint MonarchIsPatronSubChunkIdent = 0x10000490u;

    private const uint ExperiencePassedUpPhraseIdent = 0x10000492u;

    private const uint RankLabelPhraseIdent = 0x10000268u;

    private const uint RankExperiencePassedUpPhraseIdent = 0x10000269u;

    private const uint RankOfflineMarkerIdent = 0x100004AAu;

    private const uint StringChartIdent = 0x23000001u;

    private const uint KnobStringChartIdent = 0x23000003u;

    private static readonly Vector4 PhraseTint = Vector4.One;

    private static readonly IReadOnlyList<WidgetPhrase.Line> BlankStroke =
        [new WidgetPhrase.Line(" ", PhraseTint)];

    private static readonly Func<IReadOnlyList<WidgetPhrase.Line>> BlankStrokeSupplier = () => BlankStroke;

    private const string BlankSentinel = " blank ";

    public sealed record Bindings(
        Func<SimAllegianceCapture> Snapshot,
        Func<SimAllegianceMemberCapture?> Monarch,
        Func<uint, SimAllegianceMemberCapture?> Patron,
        Func<uint, SimAllegianceMemberCapture?> Member,
        Func<uint, IEnumerable<SimAllegianceMemberCapture>> Vassals,
        Func<uint, SimDirectiveResult> Swear,
        Func<uint, SimDirectiveResult> Break,
        Func<uint, SimDirectiveResult> Kick,
        Func<bool, SimDirectiveResult> SetUpdateSubscription,
        MacAC.Mechanics.Targeting.PickPhase Selection,
        Func<uint> LocalPlayerGuid,
        Func<CharacterOptionId, bool> CurrentCharacterOption,
        Action<CharacterOptionId, bool> SetCharacterOption,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        Func<uint, uint, string?> ResolveString,
        Func<uint, string?> ResolveWorldObjectName,
        Func<string, Action<bool>, uint> ShowConfirmation,
        Func<string, string, string?>? ResolvePlayerTemplate = null);

    private readonly record struct VassalRankWidgets(
        WidgetPhrase? Name,
        WidgetPhrase? ExperiencePassedUp,
        WidgetElem? OfflineMarker);

    private readonly Bindings _bindings;

    private readonly WidgetPhrase? _selfLabel;

    private readonly WidgetPhrase? _selfFollowers;

    private readonly WidgetPhrase? _selfGrade;

    private readonly WidgetElem _monarchField;

    private readonly WidgetPhrase? _monarchCaption;

    private readonly WidgetPhrase? _monarchMoniker;

    private readonly WidgetPhrase? _monarchFollowers;

    private readonly WidgetElem? _monarchIsPatronSubChunk;

    private readonly WidgetPhrase? _monarchExperiencePassedUp;

    private readonly WidgetElem _patronField;

    private readonly WidgetPhrase? _patronLabel;

    private readonly WidgetPhrase? _patronExperiencePassedUp;

    private readonly WidgetBlueprintRosterBbox? _vassalRosterBbox;

    private readonly WidgetBtn? _ignoreReqsTickbox;

    private readonly WidgetBtn? _swearBtn;

    private readonly WidgetBtn? _breakBtn;

    private readonly WidgetBtn? _kickBtn;

    private readonly string? _monarchCaptionLegend;

    private readonly string? _patronSlashMonarchCaptionLegend;

    private readonly Dictionary<uint, VassalRankWidgets> _ranks = [];

    private readonly HashSet<uint> _vassalOids = [];

    private uint _chosenVassalOid;

    private long _previousLineupRev = long.MinValue;

    private bool _subscribed;

    private string? _previousSelfLabel;

    private string? _previousSelfFollowers;

    private string? _previousSelfGrade;

    private string? _previousMonarchLabel;

    private string? _previousMonarchFollowers;

    private string? _previousMonarchExperiencePassedUp;

    private string? _previousPatronLabel;

    private string? _previousPatronExperiencePassedUp;

    private SocialAllegianceSheetDriver(
        Bindings mappings,
        WidgetPhrase? selfLabel,
        WidgetPhrase? selfFollowers,
        WidgetPhrase? selfGrade,
        WidgetElem monarchField,
        WidgetPhrase? monarchCaption,
        WidgetPhrase? monarchMoniker,
        WidgetPhrase? monarchFollowers,
        WidgetElem? monarchIsPatronSubChunk,
        WidgetPhrase? monarchExperiencePassedUp,
        WidgetElem patronField,
        WidgetPhrase? patronLabel,
        WidgetPhrase? patronExperiencePassedUp,
        WidgetBlueprintRosterBbox? vassalRosterBbox,
        WidgetBtn? ignoreReqsTickbox,
        WidgetBtn? swearBtn,
        WidgetBtn? breakBtn,
        WidgetBtn? kickBtn,
        string? monarchCaptionLegend,
        string? patronSlashMonarchCaptionLegend)
    {
        _bindings = mappings;
        _selfLabel = selfLabel;
        _selfFollowers = selfFollowers;
        _selfGrade = selfGrade;
        _monarchField = monarchField;
        _monarchCaption = monarchCaption;
        _monarchMoniker = monarchMoniker;
        _monarchFollowers = monarchFollowers;
        _monarchIsPatronSubChunk = monarchIsPatronSubChunk;
        _monarchExperiencePassedUp = monarchExperiencePassedUp;
        _patronField = patronField;
        _patronLabel = patronLabel;
        _patronExperiencePassedUp = patronExperiencePassedUp;
        _vassalRosterBbox = vassalRosterBbox;
        _ignoreReqsTickbox = ignoreReqsTickbox;
        _swearBtn = swearBtn;
        _breakBtn = breakBtn;
        _kickBtn = kickBtn;
        _monarchCaptionLegend = monarchCaptionLegend;
        _patronSlashMonarchCaptionLegend = patronSlashMonarchCaptionLegend;
    }
}

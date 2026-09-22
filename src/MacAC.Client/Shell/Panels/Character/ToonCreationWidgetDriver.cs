using System.Numerics;
using MacAC.Mechanics.Genesis;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

public sealed record ToonCreationEngineWiring(
    Func<ISimToonGenesisLens?> View,
    Func<uint, SimDirectiveResult> SelectHeritage,
    Func<uint, SimDirectiveResult> SelectGender,
    Func<uint, SimDirectiveResult> SelectTemplate,
    Func<GenesisTraitId, int, SimDirectiveResult> SetAttribute,
    Func<GenesisTraitId, bool, SimDirectiveResult> SetAttributeLock,
    Func<uint, SimDirectiveResult> TrainSkill,
    Func<uint, SimDirectiveResult> SpecializeSkill,
    Func<uint, SimDirectiveResult> UntrainSkill,
    Func<int, SimDirectiveResult> SelectStartArea,
    Func<bool, SimDirectiveResult> Finish,
    Action RequestExit,
    Func<GenesisAppearanceSlot, uint, SimDirectiveResult>? SetAppearanceIndex = null,
    Func<GenesisShadeSlot, double, SimDirectiveResult>? SetShade = null,
    Func<string, string?>? ResolveText = null,
    Func<string, SimDirectiveResult>? SetName = null,
    Func<SimDirectiveResult>? AcknowledgeRejection = null,
    Func<SimDirectiveResult>? RandomizeCharacter = null,
    /// <summary>CC5: the Appearance page's Random button on its Face sub-tab.</summary>
    Func<SimDirectiveResult>? RandomizeAppearance = null,
    Func<SimDirectiveResult>? RandomizeClothing = null,
    Func<uint, GenesisAttributeSpread, GenesisSkillTrack, uint>? GetSkillScore = null,
    bool OpenOnStart = false);

internal sealed partial class ToonCreationWidgetDriver : IDisposable
{
    internal const uint TrunkEnum = 0x10000039u;

    internal const uint TrunkElemIdent = 0x100003CCu;

    internal const uint HeadwayBarElemIdent = 0x100003CEu;

    internal const uint BackElemIdent = 0x100003C6u;

    internal const uint UpcomingElemIdent = 0x100003C7u;

    internal const uint CompleteElemIdent = 0x100003C8u;

    internal const uint HelpElemIdent = 0x100003C9u;

    internal const uint QuitElemIdent = 0x100003CAu;

    internal const uint RandomElemIdent = 0x100003CBu;

    internal const uint MasterSheetElemIdent = 0x100003D0u;

    internal const uint LineageSheetElemIdent = 0x100003D1u;

    internal const uint ProfessionSheetElemIdent = 0x100003D2u;

    internal const uint AptitudesSheetElemIdent = 0x100003D3u;

    internal const uint LooksSheetElemIdent = 0x100003D4u;

    internal const uint TownSheetElemIdent = 0x100003D5u;

    internal const uint SummarySheetElemIdent = 0x100003D6u;

    internal const uint LineageTabElemIdent = 0x100003EFu;

    internal const uint ProfessionTabElemIdent = 0x100003F0u;

    internal const uint AptitudesTabElemIdent = 0x100003F1u;

    internal const uint LooksTabElemIdent = 0x100003F2u;

    internal const uint TownTabElemIdent = 0x100003F3u;

    internal const uint SummaryTabElemIdent = 0x100003F4u;

    internal enum Page
    {
        Heritage = 1,
        Profession = 2,
        Skills = 3,
        Appearance = 4,
        Town = 5,
        Summary = 6,
    }

    internal sealed record PromptStrings(
        string ExitWarning,
        string NoNameWarning,
        string CreditWarning,
        string RandomizeWarning,
        string NameTooLong);

    private readonly WidgetTrunk _hub;

    private readonly ImportedArrangement _arrangement;

    private readonly WidgetElem _headwayBar;

    private readonly WidgetBtn _back;

    private readonly WidgetBtn _upcoming;

    private readonly WidgetBtn _complete;

    private readonly WidgetBtn _help;

    private readonly WidgetBtn _quit;

    private readonly WidgetBtn _random;

    private readonly WidgetElem _masterSheet;

    private readonly WidgetElem _lineageSheetTrunk;

    private readonly WidgetElem _professionSheetTrunk;

    private readonly WidgetElem _aptitudesSheetTrunk;

    private readonly WidgetElem _looksSheetTrunk;

    private readonly WidgetElem _townSheetTrunk;

    private readonly WidgetElem _summarySheetTrunk;

    private readonly WidgetBtn _lineageTab;

    private readonly WidgetBtn _professionTab;

    private readonly WidgetBtn _aptitudesTab;

    private readonly WidgetBtn _looksTab;

    private readonly WidgetBtn _townTab;

    private readonly WidgetBtn _summaryTab;

    private readonly CanonPromptMint _popups;

    private readonly ToonCreationEngineWiring _bindings;

    private readonly PromptStrings _texts;

    private readonly ToonCreationHeritagePage _lineageSheet;

    private readonly ToonCreationProfessionPage _professionSheet;

    private readonly ToonCreationSkillsPage _aptitudesSheet;

    private readonly ToonCreationTownPage _townSheet;

    private readonly ToonCreationAppearancePage _looksSheet;

    private readonly ToonCreationSummaryPage _summarySheet;

    private Vector2 _authoredCanvas;

    private SimEpochTicket _previousGen;

    private long _previousRev = long.MinValue;

    private Page _latestSheet = Page.Heritage;

    private bool _engaged;

    private bool _isOpen;

    private bool _openOnBeginConsumed;

    private uint _quitPopupCtx;

    private uint _creditWarningPopupCtx;

    private uint _randomizeWarningPopupCtx;

    private uint _noLabelWarningPopupCtx;

    private uint _problemMsgPopupCtx;

    private SimToonGenesisRejection? _previousShownRejection;

    private bool _suppressPopupHooks;

    private bool _destroyed;

    private ToonCreationWidgetDriver(
        WidgetTrunk hub,
        ImportedArrangement arrangement,
        WidgetElem headwayBar,
        WidgetBtn back,
        WidgetBtn upcoming,
        WidgetBtn complete,
        WidgetBtn help,
        WidgetBtn quit,
        WidgetBtn random,
        WidgetElem masterSheet,
        WidgetElem lineageSheetTrunk,
        WidgetElem professionSheetTrunk,
        WidgetElem aptitudesSheetTrunk,
        WidgetElem looksSheetTrunk,
        WidgetElem townSheetTrunk,
        WidgetElem summarySheetTrunk,
        WidgetBtn lineageTab,
        WidgetBtn professionTab,
        WidgetBtn aptitudesTab,
        WidgetBtn looksTab,
        WidgetBtn townTab,
        WidgetBtn summaryTab,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        CanonPromptMint popups,
        ToonCreationEngineWiring mappings,
        PromptStrings texts)
    {
        _hub = hub;
        _arrangement = arrangement;
        _headwayBar = headwayBar;
        _back = back;
        _upcoming = upcoming;
        _complete = complete;
        _help = help;
        _quit = quit;
        _random = random;
        _masterSheet = masterSheet;
        _lineageSheetTrunk = lineageSheetTrunk;
        _professionSheetTrunk = professionSheetTrunk;
        _aptitudesSheetTrunk = aptitudesSheetTrunk;
        _looksSheetTrunk = looksSheetTrunk;
        _townSheetTrunk = townSheetTrunk;
        _summarySheetTrunk = summarySheetTrunk;
        _lineageTab = lineageTab;
        _professionTab = professionTab;
        _aptitudesTab = aptitudesTab;
        _looksTab = looksTab;
        _townTab = townTab;
        _summaryTab = summaryTab;
        _popups = popups;
        _bindings = mappings;
        _texts = texts;

        Root.Left = 0f;
        Root.Top = 0f;
        Root.ClickThrough = false;
        Root.Visible = false;
        _authoredCanvas = new Vector2(
            Root.Width > 0f ? Root.Width : 800f,
            Root.Height > 0f ? Root.Height : 600f);

        _lineageSheet = new ToonCreationHeritagePage(lineageSheetTrunk, mappings, ImposeLineageTabRevert);
        _professionSheet = new ToonCreationProfessionPage(professionSheetTrunk, mappings);
        _aptitudesSheet = new ToonCreationSkillsPage(aptitudesSheetTrunk, mappings, blueprintLocator);
        _townSheet = new ToonCreationTownPage(townSheetTrunk, mappings);
        _looksSheet = new ToonCreationAppearancePage(looksSheetTrunk, mappings);
        _summarySheet = new ToonCreationSummaryPage(
            summarySheetTrunk, mappings, popups, texts.NameTooLong, blueprintLocator);

        _back.OnClick = OnBack;
        _upcoming.OnClick = OnUpcoming;
        _complete.OnClick = OnComplete;
        _help.OnClick = null;
        _quit.OnClick = OnQuit;
        _random.OnClick = OnRandom;
        _lineageTab.OnClick = () => ImposeHeadwayPhase(Page.Heritage);
        _professionTab.OnClick = () => ImposeHeadwayPhase(Page.Profession);
        _aptitudesTab.OnClick = () => ImposeHeadwayPhase(Page.Skills);
        _looksTab.OnClick = () => ImposeHeadwayPhase(Page.Appearance);
        _townTab.OnClick = () => ImposeHeadwayPhase(Page.Town);
        _summaryTab.OnClick = () => ImposeHeadwayPhase(Page.Summary);

    }

    private static readonly IReadOnlySet<uint> LineageTabUnhideBtnIdents = new HashSet<uint>
    {
        0x100003BFu, 0x100003C1u, 0x100003C2u, 0x100003C3u,
        0x10000590u, 0x10000591u, 0x100005A9u, 0x100005BFu,
        0x100005C4u, 0x100005E8u,
    };

    private static readonly IReadOnlySet<uint> LineageTabConcealBtnIdents = new HashSet<uint>
    {
        0x100005C7u, 0x100005C8u,
    };
}

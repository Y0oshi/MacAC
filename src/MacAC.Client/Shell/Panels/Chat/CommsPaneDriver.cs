using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public sealed partial class CommsPaneDriver : IRetainedWindowStateDriver, IRetainedPaneDriver
{
    public const uint LayoutIdent = 0x2100006Fu;

    private bool _destroyed;

    private const uint TrunkIdent = 0x10000600u;

    private const uint TranscriptBoardIdent = 0x10000010u;

    private const uint TranscriptIdent = 0x10000011u;   // Type-12 prototype - skipped by factory

    private const uint UnreadIndicatorIdent = 0x1000048Cu;

    private const uint FollowIdent = 0x10000012u;

    private const uint FeedBarIdent = 0x10000013u;

    private const uint MenuIdent = 0x10000014u;

    private const uint MenuCaptionIdent = 0x10000015u;

    private const uint FeedIdent = 0x10000016u;   // Type-12 Text + Editable 0x16 → UiField

    private const uint TransmitIdent = 0x10000019u;

    private const uint UpperLowerIdent = 0x1000046Fu;

    private const uint Indicator1Ident = 0x10000522u;

    private const uint Indicator2Ident = 0x10000523u;

    private const uint Indicator3Ident = 0x10000524u;

    private const uint Indicator4Ident = 0x10000525u;

    private static readonly uint[] BoltedTwinIdents =
    [
        0x10000693u, 0x10000694u, 0x10000695u, 0x10000696u,
        0x10000697u, 0x10000698u, 0x10000699u, 0x1000069Au,
    ];

    private const uint MenuNorm = 0x06004D65u;   // button face

    private const uint MenuPressed = 0x06004D66u;   // button pressed

    private const uint MenuPopupBg = 0x0600124Cu;

    private const uint MenuGearRank = 0x0600124Eu;

    private const uint MenuGearChosen = 0x0600124Du;

    public WidgetElem Root { get; private set; } = null!;

    public WidgetPhrase Transcript { get; private set; } = null!;

    public WidgetField Input { get; private set; } = null!;

    public WidgetScroller Scrollbar { get; private set; } = null!;

    public WidgetMenu Menu { get; private set; } = null!;

    public ElemDetails DatPaneDetails { get; private set; } = null!;

    private CommsChannelKind _engagedLane = CommsChannelKind.Say;

    private ChatPaneState _paneFilters = null!;

    private IReadOnlyList<WidgetPhrase.Line> _stashedTranscriptStrokes = Array.Empty<WidgetPhrase.Line>();

    private readonly List<IReadOnlyList<WidgetPhrase.PhraseExec>?> _stashedTranscriptExecutions = [];

    private readonly List<IReadOnlyList<(int Start, int Length, TextTag Tag)>?>
        _stashedTranscriptTags = [];
    private bool _hasUnseenPhrase;

    private long _stashedTranscriptRev = -1;

    private ulong _stashedSift;

    private float _stashedTranscriptEncloseWidth = float.NaN;

    private WidgetDatFont? _stashedTranscriptDatTypeface;

    private BitmapFont? _stashedTranscriptDiagTypeface;

    private enum ClientTalkFocusSpecial
    {
        Squelch,
        TellToSelected,
    }

    private string? _tellMark;

    private Func<string, string?>? _commsTexts;

    private static readonly (string Key, string Fallback, CommsChannelKind Channel)[] LaneGearList =
    [
        ("ID_Chat_TellToAll",      "Chat to All",           CommsChannelKind.Say),
        ("ID_Chat_TellToFellows",  "Tell to Fellows",       CommsChannelKind.Fellowship),
        ("ID_Chat_TellToGeneral",  "Tell to General Chat",  CommsChannelKind.General),
        ("ID_Chat_TellToLFG",      "Tell to LFG Chat",      CommsChannelKind.Lfg),
        ("ID_Chat_TellToSociety",  "Tell to Society Chat",  CommsChannelKind.Society),
        ("ID_Chat_TellToMonarch",  "Tell to Monarch",       CommsChannelKind.Monarch),
        ("ID_Chat_TellToPatron",   "Tell to Patron",        CommsChannelKind.Patron),
        ("ID_Chat_TellToVassals",  "Tell to Vassals",       CommsChannelKind.Vassals),
        ("ID_Chat_TellToAllegiance", "Tell to Allegiance",  CommsChannelKind.Allegiance),
        ("ID_Chat_TellToTrade",    "Tell to Trade Chat",    CommsChannelKind.Trade),
        ("ID_Chat_TellToRoleplay", "Tell to Roleplay Chat", CommsChannelKind.Roleplay),
        ("ID_Chat_TellToOlthoi",   "Tell to Olthoi Chat",   CommsChannelKind.Olthoi),
    ];

    // Window height before maximize (stored to restore on un-maximize)
    private float _normHeight;

    // Window top before maximize
    private float _normTop;
    private WidgetBtn? _upperLowerBtn;

    private readonly WidgetBtn?[] _indicatorBtns = new WidgetBtn?[4];

    public static CommsPaneDriver? Bind(
        ElemDetails trunkDetails,
        ImportedArrangement arrangement,
        ChatModel model,
        Func<IDirectiveBus> busSupplier,
        ChatPaneState paneFilters,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        Func<uint, (uint tex, int w, int h)> locate,
        Func<string?>? chosenMarkLabel = null,
        Func<string, string?>? commsTexts = null,
        Func<uint, WidgetDatFont?>? locateTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(paneFilters);

        WidgetElem? transcriptBoard = arrangement.SeekElem(TranscriptBoardIdent);
        WidgetElem? feedBar = arrangement.SeekElem(FeedBarIdent);
        WidgetField? feed = arrangement.SeekElem(FeedIdent) as WidgetField;

        if (feed is null || transcriptBoard is null || feedBar is null)
        {
            Console.WriteLine(
                $"[UI] ChatWindowController.Bind: absent needed elements " +
                $"(input={feed is not null}, " +
                $"panel={transcriptBoard is not null}, bar={feedBar is not null}) — " +
                $"chat window will not be interactive");
            return null;
        }

        WidgetElem pane = arrangement.SeekElem(TrunkIdent) ?? arrangement.Root;
        CommsPaneDriver controller = new CommsPaneDriver
        {
            Root = pane,
            DatPaneDetails = SeekDetails(trunkDetails, TrunkIdent) ?? trunkDetails,
            _paneFilters = paneFilters,
            _commsTexts = commsTexts,
        };

        foreach (uint ident in BoltedTwinIdents)
            if (arrangement.SeekElem(ident) is { } twin)
                twin.Visible = false;

        uint[] indicatorIdents = [Indicator1Ident, Indicator2Ident, Indicator3Ident, Indicator4Ident];
        for (int idx = 0; idx < indicatorIdents.Length; ++idx)
        {
            WidgetBtn? indicator = arrangement.SeekElem(indicatorIdents[idx]) as WidgetBtn;
            indicator?.SuppressSelfFlip = true;
            controller._indicatorBtns[idx] = indicator;
        }

        controller.Transcript = arrangement.SeekElem(TranscriptIdent) as WidgetPhrase
            ?? throw new InvalidOperationException("chat transcript 0x10000011 not built as WidgetPhrase");
        controller.Transcript.DatFont = datTypeface;
        controller.Transcript.Font = diagTypeface;
        controller.Transcript.Centered = false;
        controller.Transcript.RightAligned = false;
        controller.Transcript.OneLine = false;
        controller.Transcript.Selectable = true;
        controller.Transcript.StrokesSupplier = () => controller.FetchTranscriptStrokes(model);
        controller.Transcript.StrokeExecutionsSupplier = ordinal =>
            ordinal >= 0 && ordinal < controller._stashedTranscriptExecutions.Count
                ? controller._stashedTranscriptExecutions[ordinal]
                : null;
        controller.Transcript.OnCharPress = spot => controller.TryBeginTellFromTag(spot);

        controller.UnreadIndicatorForTest = arrangement.SeekElem(UnreadIndicatorIdent);
        if (controller.UnreadIndicatorForTest is not null)
        {
            controller.AssignUnreadIndicatorPhase(unread: false);
            if (controller.UnreadIndicatorForTest is WidgetBtn unread)
                unread.OnClick = controller.RollToNewestAndWipeUnread;
        }

        controller.Input = feed;
        controller.Input.DatTypeface = datTypeface;
        controller.Input.Font = diagTypeface;
        controller.Input.SpriteLocate = locate;
        controller.Input.PhraseReplacer = phrase =>
            CommsTextSwaps.Widen(phrase, model.LastIncomingTellSender);
        controller.Input.OnSubmit = phrase => CommsDirectiveRouter.Submit(
            phrase, model, busSupplier(), controller._engagedLane, controller._tellMark);

        if (controller.Input.ArrangementRule is { } feedRule)
        {
            controller.Input.ArrangementRule = new WidgetArrangementRule(
                feedRule.LeftManner,
                feedRule.TopManner,
                rightMode: 1u,
                feedRule.BottomManner,
                feedRule.OriginalDescendant,
                feedRule.OriginalAncestor);
        }
        else
        {
            controller.Input.Moorings |= MooringRims.Right;
        }

        WidgetElem? follow = arrangement.SeekElem(FollowIdent);
        if (follow is WidgetScroller bar)
        {
            bar.Model = controller.Transcript.Scroll;
            bar.SpriteResolve ??= locate;
            controller.Scrollbar = bar;
        }

        if (arrangement.SeekElem(MenuIdent) is WidgetMenu menu)
        {
            menu.DatFont = datTypeface; menu.Font = diagTypeface; menu.SpriteResolve = locate;
            if (SeekDetails(trunkDetails, MenuCaptionIdent) is { } captionDetails)
            {
                if (captionDetails.FontColor is { } authoredTint)
                    menu.WordingTint = authoredTint;
                if (captionDetails.FontDid is not 0u
                    && locateTypeface?.Invoke(captionDetails.FontDid) is { } btnTypeface)
                    menu.BtnDatTypeface = btnTypeface;
                menu.BtnPhraseCentered =
                    captionDetails.HJustify == ClientHJustify.Center;
            }
            menu.NormSprite = MenuNorm; menu.PressedSprite = MenuPressed;
            menu.PopupBgSprite = MenuPopupBg;
            menu.GearNormSprite = MenuGearRank; menu.GearHighlightSprite = MenuGearChosen;
            string? ChosenLabel() => chosenMarkLabel?.Invoke();

            void ReassembleGearList()
            {
                string? mark = ChosenLabel();
                var gearList = new List<WidgetMenu.MenuGear>(LaneGearList.Length)
                {
                    new(mark is null
                            ? controller.S("ID_Chat_SquelchSelectedNoSelection",
                                  "Squelch (ignore) Selected")
                            : controller.S("ID_Chat_SquelchSelected", "Squelch (ignore) ") + mark,
                        ClientTalkFocusSpecial.Squelch),
                    new(mark is null
                            ? controller.S("ID_Chat_TellToSelectedNoSelection", "Tell to Selected")
                            : controller.S("ID_Chat_TellToSelected", "Tell to ") + mark,
                        ClientTalkFocusSpecial.TellToSelected),
                };
                foreach ((string tag, string backup, CommsChannelKind kind) in LaneGearList)
                    gearList.Add(new WidgetMenu.MenuGear(controller.S(tag, backup), kind));
                menu.Items = gearList.ToArray();
            }

            ReassembleGearList();
            menu.Selected = (object?)controller._engagedLane;
            menu.TurnedOnSupplier = p => p switch
            {
                CommsChannelKind kind => LaneOnHand(kind),
                ClientTalkFocusSpecial => ChosenLabel() is not null,
                _ => true,
            };
            menu.BtnCaptionSupplier = () => controller.LaneBtnCaption(controller._engagedLane);
            menu.OnOpen = ReassembleGearList;
            menu.OnSelect = p =>
            {
                switch (p)
                {
                    case CommsChannelKind kind:
                        controller._engagedLane = kind;
                        controller._tellMark = null;
                        menu.Selected = p;
                        break;

                    case ClientTalkFocusSpecial.TellToSelected when ChosenLabel() is { } label:
                        controller._engagedLane = CommsChannelKind.Tell;
                        controller._tellMark = label;
                        menu.Selected = p;
                        break;

                    case ClientTalkFocusSpecial.Squelch when ChosenLabel() is { } squelched:
                        busSupplier().Publish(
                            new ExecuteClientDirectiveCmd(
                                ClientDirectiveId.Squelch, squelched));
                        break;
                }
            };
            controller.Menu = menu;
        }

        if (arrangement.SeekElem(TransmitIdent) is WidgetBtn transmitElem)
        {
            transmitElem.OnClick = () => controller.Input.Submit();
            transmitElem.Label = "Send";
            var transmitDetails = SeekDetails(trunkDetails, TransmitIdent);
            transmitElem.LabelFont =
                (transmitDetails?.FontDid is { } transmitTypefaceDid and not 0u
                    ? locateTypeface?.Invoke(transmitTypefaceDid)
                    : null) ?? datTypeface;
            transmitElem.CaptionColor = transmitDetails?.FontColor ?? new Vector4(1f, 1f, 1f, 1f);
        }

        if (arrangement.SeekElem(UpperLowerIdent) is WidgetBtn upperLowerElem)
        {
            controller._upperLowerBtn = upperLowerElem;
            upperLowerElem.OnClick = controller.FlipMaximize;
        }

        return controller;
    }

    private bool _flashBegun;
}

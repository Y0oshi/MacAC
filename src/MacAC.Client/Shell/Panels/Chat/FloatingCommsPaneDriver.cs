using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public sealed class FloatingCommsPaneDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x2100005Bu;

    private const uint TrunkIdent = 0x100004F7u;   // window root, 250x108
    private const uint TranscriptBoardIdent = 0x10000010u;
    private const uint TranscriptIdent = 0x10000011u;   // Type-12 prototype - skipped by factory
    private const uint FollowIdent = 0x10000012u;
    private const uint FeedRankIdent = 0x10000509u;
    private const uint FeedIdent = 0x10000016u;   // Type-12 Text + Editable 0x16 -> UiField
    private const uint TransmitIdent = 0x10000019u;
    private const uint BannerBarIdent = 0x100004D9u;
    private const uint ShutBtnIdent = 0x1000052Au;

    private bool _destroyed;

    public int PaneIdent { get; }

    public WidgetElem Root { get; private set; } = null!;
    public WidgetPhrase Transcript { get; private set; } = null!;
    public WidgetField Input { get; private set; } = null!;
    public WidgetScroller? Scrollbar { get; private set; }

    public ElemDetails DatPaneInfo { get; private set; } = null!;

    public CanonWindowHandle? PaneHandle { get; private set; }

    private IReadOnlyList<WidgetPhrase.Line> _stashedTranscriptStrokes = Array.Empty<WidgetPhrase.Line>();
    private long _stashedTranscriptRev = -1;
    private ulong _stashedSift;
    private float _stashedTranscriptEncloseWidth = float.NaN;
    private WidgetDatFont? _stashedTranscriptDatTypeface;
    private BitmapFont? _stashedTranscriptDiagTypeface;
    internal int TranscriptArrangementAssembleTally { get; private set; }

    private FloatingCommsPaneDriver(int paneIdent)
    {
        PaneIdent = paneIdent;
    }

    public static FloatingCommsPaneDriver? Bind(
        int windowId,
        ElemDetails trunkDetails,
        ImportedArrangement arrangement,
        ChatModel model,
        Func<IDirectiveBus> busSupplier,
        ChatPaneState paneFilters,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        Func<uint, (uint tex, int w, int h)> locate)
    {
        if (windowId is < ChatPaneState.LowerFloatingPaneIdent or > ChatPaneState.UpperFloatingPaneIdent)
            throw new ArgumentOutOfRangeException(nameof(windowId));
        ArgumentNullException.ThrowIfNull(paneFilters);

        WidgetElem? transcriptBoard = arrangement.SeekElem(TranscriptBoardIdent);
        WidgetElem? feedRank = arrangement.SeekElem(FeedRankIdent);
        WidgetField? feed = arrangement.SeekElem(FeedIdent) as WidgetField;

        if (feed is null || transcriptBoard is null || feedRank is null)
        {
            Console.WriteLine(
                $"[UI] FloatingChatWindowController.Bind(window {windowId}): absent needed elements " +
                $"(input={feed is not null}, panel={transcriptBoard is not null}, row={feedRank is not null}) — " +
                $"floating chat window will not be interactive");
            return null;
        }

        WidgetElem pane = arrangement.SeekElem(TrunkIdent) ?? arrangement.Root;
        var controller = new FloatingCommsPaneDriver(windowId)
        {
            Root = pane,
            DatPaneInfo = SeekDetails(trunkDetails, TrunkIdent) ?? trunkDetails,
            Transcript = arrangement.SeekElem(TranscriptIdent) as WidgetPhrase
                ?? throw new InvalidOperationException("floating chat transcript 0x10000011 not built as WidgetPhrase")
        };
        controller.Transcript.DatFont = datTypeface;
        controller.Transcript.Font = diagTypeface;
        controller.Transcript.Centered = false;
        controller.Transcript.RightAligned = false;
        controller.Transcript.OneLine = false;
        controller.Transcript.Selectable = true;
        controller.Transcript.StrokesSupplier = () => controller.FetchTranscriptStrokes(model, paneFilters);

        controller.Input = feed;
        controller.Input.DatTypeface = datTypeface;
        controller.Input.Font = diagTypeface;
        controller.Input.SpriteLocate = locate;
        controller.Input.OnSubmit = phrase => CommsDirectiveRouter.Submit(phrase, model, busSupplier(), CommsChannelKind.Say);

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

        if (arrangement.SeekElem(FollowIdent) is WidgetScroller bar)
        {
            bar.Model = controller.Transcript.Scroll;
            bar.SpriteResolve ??= locate;
            controller.Scrollbar = bar;
        }

        if (arrangement.SeekElem(TransmitIdent) is WidgetBtn transmitElem)
        {
            transmitElem.OnClick = () => controller.Input.Submit();
            transmitElem.Label = "Send";
            transmitElem.LabelFont = datTypeface;
            transmitElem.CaptionColor = new Vector4(1f, 0.92f, 0.72f, 1f);
        }

        if (arrangement.SeekElem(BannerBarIdent) is WidgetPhrase bannerPhrase)
        {
            bannerPhrase.DatFont = datTypeface;
            bannerPhrase.Font = diagTypeface;
            bannerPhrase.OneLine = true;
            string banner = $"Chat {windowId}";
            Vector4 bannerTint = new Vector4(1f, 0.92f, 0.72f, 1f);
            bannerPhrase.StrokesSupplier = () => new[] { new WidgetPhrase.Line(banner, bannerTint) };
        }

        if (arrangement.SeekElem(ShutBtnIdent) is WidgetBtn shutElem)

            shutElem.OnClick = () => controller.PaneHandle?.Hide();

        return controller;
    }

    public void AffixPane(CanonWindowHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!ReferenceEquals(handle.SubstanceTrunk, Root))
            throw new ArgumentException(
                "Floating chat handle content root doesn't match the bound layout", nameof(handle));
        if (PaneHandle is not null && !ReferenceEquals(PaneHandle, handle))
            throw new InvalidOperationException("Floating chat controller is by now attached to another window");
        PaneHandle = handle;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
    }

    private static ElemDetails? SeekDetails(ElemDetails joint, uint ident)
    {
        if (joint.Id == ident) return joint;
        foreach (ElemDetails descendant in joint.Children)
        {
            var located = SeekDetails(descendant, ident);
            if (located is not null) return located;
        }
        return null;
    }

    private IReadOnlyList<WidgetPhrase.Line> FetchTranscriptStrokes(ChatModel model, ChatPaneState paneFilters)
    {
        float upperW = Transcript.Width - 2f * Transcript.Padding;
        var datTypeface = Transcript.DatFont;
        BitmapFont? diagTypeface = Transcript.Font;
        long rev = model.Rev;
        ulong sift = paneFilters.FetchSift(PaneIdent);

        if (_stashedTranscriptRev == rev
            && _stashedSift == sift
            && _stashedTranscriptEncloseWidth.Equals(upperW)
            && ReferenceEquals(_stashedTranscriptDatTypeface, datTypeface)
            && ReferenceEquals(_stashedTranscriptDiagTypeface, diagTypeface))

            return _stashedTranscriptStrokes;

        var detailed = model.RecentStrokesDetailed();
        Func<string, float> gauge =
              datTypeface is { } font ? font.MeasureWidth
            : diagTypeface is { } bf ? bf.MeasureWidth
            : static s => s.Length * 7f;

        bool Admit(uint tracePhraseKind) => paneFilters.ShouldReadout(
            PaneIdent, ChatPaneState.AirMarkPane, tracePhraseKind);
        List<WidgetPhrase.Line> outcome = CommsTranscriptPainter.AssembleStrokes(
            detailed, upperW, gauge, Admit, Transcript.DefaultTint);

        _stashedTranscriptRev = rev;
        _stashedSift = sift;
        _stashedTranscriptEncloseWidth = upperW;
        _stashedTranscriptDatTypeface = datTypeface;
        _stashedTranscriptDiagTypeface = diagTypeface;
        _stashedTranscriptStrokes = outcome;
        ++TranscriptArrangementAssembleTally;
        return outcome;
    }
}

using System.Globalization;
using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;

namespace MacAC.Cockpit.Panels.Chat;

public sealed class ChatModel : IDisposable, ICommsDirectiveFeedback
{
    public const int DefaultReadoutThreshold = 20;

    private const uint LeadAvatarObjectIdent = 0x50000001u;
    private const uint PreviousAvatarObjectIdent = 0x6FFFFFFFu;

    private readonly ChatTranscript _trace;
    private readonly CommandTargetState _targets;
    private readonly bool _ownsMarks;
    private readonly int _readoutThreshold;
    private bool _destroyed;

    public ChatModel(ChatTranscript log, int displayLimit = DefaultReadoutThreshold, CommandTargetState? directiveMarks = null)
    {
        _trace = log ?? throw new ArgumentNullException(nameof(log));
        if (displayLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(displayLimit), displayLimit, "has to be >= 1");
        _readoutThreshold = displayLimit;
        _ownsMarks = directiveMarks is null;
        _targets = directiveMarks ?? new CommandTargetState(_trace);
    }

    public string? LastIncomingTellSender => _targets.LastIncomingTellSender;

    public string? LastOutgoingTellTarget => _targets.LastOutgoingTellTarget;

    public string? PreviousMonarchSender => _targets.LastMonarchSender;

    public string? PreviousPatronSender => _targets.LastPatronSender;

    public Func<float>? FpsSupplier { get; init; }

    public Func<Vector3>? LocusSupplier { get; init; }

    public Action<string>? OnInterfacePhrase { get; init; }

    public long Rev => _trace.Revision;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        if (_ownsMarks)
            _targets.Dispose();
    }

    public void ShowSystemMessage(string phrase) => _trace.OnSysMsg(phrase, commsKind: 0x00u);

    public void ShowInterfaceText(string phrase)
    {
        if (OnInterfacePhrase is { } drain)
            drain(phrase);
        else
            _trace.OnSysMsg(phrase, commsKind: (uint)CanonLogTextType.ClientLocal);
    }

    public void Clear() => _trace.Clear();

    public void RestartSessMarks() => _targets.ResetSession();

    public void RevealFps()
    {
        ShowSystemMessage(FpsSupplier?.Invoke() is { } fps
        ? string.Create(CultureInfo.InvariantCulture, $"Framerate: {fps:F1} FPS")
        : "Framerate: (provider unavailable)");
    }

    public void RevealLocale()
    {
        ShowSystemMessage(LocusSupplier?.Invoke() is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"Location: ({p.X:F1}, {p.Y:F1}, {p.Z:F1})")
        : "Location: (provider unavailable)");
    }

    public IReadOnlyList<string> RecentStrokes()
    {
        var rear = Rear();
        if (rear.Count is 0)
            return [];

        bool stamp = Timestamped;
        string[] strokes = new string[rear.Count];
        for (int idx = 0; idx < strokes.Length; ++idx)
        {
            ChatRow rank = rear[idx];
            strokes[idx] = stamp ? ChatTranscript.ComposeStampStem(rank.Received) + ComposeListing(rank) : ComposeListing(rank);
        }
        return strokes;
    }

    private bool Timestamped => _trace.ReadoutTimestampsSrc?.Invoke() == true;

    public IReadOnlyList<StyledLine> RecentStrokesDetailed()
    {
        var rear = Rear();
        if (rear.Count is 0)
            return [];

        bool stamp = Timestamped;
        StyledLine[] strokes = new StyledLine[rear.Count];
        for (int idx = 0; idx < strokes.Length; ++idx)
        {
            ChatRow rank = rear[idx];
            string markup = ComposeListingTagged(rank);
            IReadOnlyList<TextSpan>? spans = ShouldTagSender(rank) ? TagMarkup.Parse(markup) : null;
            string phrase = spans is null ? markup : string.Concat(spans.Select(span => span.Text));

            if (stamp)
            {
                string stem = ChatTranscript.ComposeStampStem(rank.Received);
                spans = [new TextSpan(stem, null, SpanRole.Timestamp), .. spans ?? [new TextSpan(phrase, null)]];
                phrase = stem + phrase;
            }

            strokes[idx] = new StyledLine(phrase, rank.Kind, rank.FightingSort, rank.TracePhraseKind, spans);
        }
        return strokes;
    }

    public static string ComposeListing(ChatRow listing) => Compose(listing, static sender => sender);

    /// <summary>Wraps a player sender in retail's Tell markup so the renderer can make it clickable.</summary>
    public static string ComposeListingTagged(ChatRow listing)
    {
        return ShouldTagSender(listing)
            ? Compose(listing, sender => $"<Tell:IIDString:{listing.SenderGuid}:{sender}>{sender}<\\Tell>")
            : ComposeListing(listing);
    }

    // Only speech-like rows from another player's guid, with a plain name, get the tag
    internal static bool ShouldTagSender(ChatRow listing)
    {
        return listing.SenderGuid is >= LeadAvatarObjectIdent and <= PreviousAvatarObjectIdent
        && !string.IsNullOrEmpty(listing.Sender)
        && listing.Sender.IndexOfAny(['<', '>']) < 0
        && !IsSelf(listing.Sender)
        && listing.Kind is ChatFlavor.LocalSpeech or ChatFlavor.RangedSpeech or ChatFlavor.Channel or ChatFlavor.Tell;
    }

    // The tail of the transcript, at most the display limit, oldest first
    private ArraySegment<ChatRow> Rear()
    {
        ChatRow[] ranks = _trace.Snapshot();
        int begin = Math.Max(0, ranks.Length - _readoutThreshold);
        return new ArraySegment<ChatRow>(ranks, begin, ranks.Length - begin);
    }

    private static string Compose(ChatRow e, Func<string, string> sender)
    {
        return e.Kind switch
        {
            ChatFlavor.LocalSpeech => IsSelf(e.Sender) ? $"You say, \"{e.Text}\"" : $"{sender(e.Sender)} says, \"{e.Text}\"",
            ChatFlavor.RangedSpeech => IsSelf(e.Sender) ? $"You shout, \"{e.Text}\"" : $"{sender(e.Sender)} shouts, \"{e.Text}\"",
            ChatFlavor.Channel => IsSelf(e.Sender) ? $"[{Channel(e)}] You say, \"{e.Text}\"" : $"[{Channel(e)}] {sender(e.Sender)} says, \"{e.Text}\"",
            ChatFlavor.Tell => e.SenderGuid is not 0 ? $"{sender(e.Sender)} tells you, \"{e.Text}\"" : $"You tell {e.Sender}, \"{e.Text}\"",
            ChatFlavor.Popup => $"[Popup] {e.Text}",
            ChatFlavor.Emote or ChatFlavor.SoulEmote => $"* {e.Sender} {e.Text}",
            _ => e.Text,
        };
    }

    private static bool IsSelf(string sender) => string.IsNullOrEmpty(sender) || sender == "You";

    private static string Channel(ChatRow e)
    {
        return string.IsNullOrEmpty(e.LaneLabel) ? $"ch {e.ChannelId}" : e.LaneLabel;
    }
}

public readonly record struct StyledLine(string Text, ChatFlavor Kind, FightLineKind? CombatKind, uint LogTextType, IReadOnlyList<TextSpan>? Spans = null);

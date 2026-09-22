using System.Collections.Concurrent;
using System.Globalization;
using MacAC.Mechanics.Fighting;

namespace MacAC.Mechanics.Comms;

public enum ChatFlavor
{
    LocalSpeech,
    RangedSpeech,
    Channel,
    Tell,
    System,
    Popup,
    Emote,
    SoulEmote,
    Combat,
}

/// <summary>One line in the chat window.</summary>
public readonly record struct ChatRow(
    ChatFlavor Kind,
    string Sender,
    string Text,
    uint SenderGuid,
    uint ChannelId)
{
    public DateTime Received { get; init; } = DateTime.UtcNow;

    public FightLineKind? FightingSort { get; init; }

    public string LaneLabel { get; init; } = "";

    public uint TracePhraseKind { get; init; } = 0x00u;
}

public sealed class ChatTranscript
{
    private static readonly TimeSpan SysRepeatPane = TimeSpan.FromSeconds(1);

    private readonly ConcurrentQueue<ChatRow> _strokes = new();
    private readonly int _cap;
    private uint _self;
    private long _rev;
    private string _previousSysStroke = "";
    private DateTime _previousSysAt = DateTime.MinValue;

    public ChatTranscript(int upperListings = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(upperListings, 1);
        _cap = upperListings;
    }

    public Func<bool>? ReadoutTimestampsSrc { get; set; }

    public event Action<ChatRow>? EntryAppended;

    public int Count => _strokes.Count;

    public long Revision => Interlocked.Read(ref _rev);

    public ChatRow[] Snapshot() => _strokes.ToArray();

    public void ApplyOwnAvatarOid(uint oid) => _self = oid;

    public void RestartSessPersona()
    {
        _self = 0u;
        _previousSysStroke = string.Empty;
        _previousSysAt = DateTime.MinValue;
    }

    public static string ComposeStampStem(DateTime receivedUtc)
    {
        return receivedUtc.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture);
    }

    public void Clear()
    {
        while (_strokes.TryDequeue(out _))
        {
        }
        Interlocked.Increment(ref _rev);
    }

    public void OnOwnSpeech(string sender, string phrase, uint senderOid, bool isRanged, uint tracePhraseKind)
    {
        bool ownEcho = _self is not 0 && senderOid == _self;
        Push(new ChatRow(
            isRanged ? ChatFlavor.RangedSpeech : ChatFlavor.LocalSpeech,
            ownEcho || string.IsNullOrEmpty(sender) ? "You" : sender,
            phrase,
            senderOid,
            ChannelId: 0)
        {
            TracePhraseKind = tracePhraseKind,
        });
    }

    /// <summary>EmoteLine (0x01E0): a server-driven third-person emote.</summary>
    public void OnEmote(string senderLabel, string phrase, uint senderOid)
    {
        Push(new ChatRow(ChatFlavor.Emote, senderLabel, phrase, senderOid, ChannelId: 0)
        {
            TracePhraseKind = (uint)CanonLogTextType.Emote,
        });
    }

    public void OnSoulEmote(string senderLabel, string phrase, uint senderOid)
    {
        Push(new ChatRow(ChatFlavor.SoulEmote, senderLabel, phrase, senderOid, ChannelId: 0)
        {
            TracePhraseKind = (uint)CanonLogTextType.Emote,
        });
    }

    /// <summary>Death notices about other people; the local player's own get their own path.</summary>
    public void OnAvatarKilled(string deathMsg, uint victimOid, uint killerOid, uint ownAvatarOid = 0u)
    {
        if (ownAvatarOid is not 0u && (ownAvatarOid == victimOid || ownAvatarOid == killerOid))
            return;
        Push(new ChatRow(ChatFlavor.System, "", deathMsg, victimOid, killerOid) { TracePhraseKind = 0x00u });
    }

    public void OnLaneAir(uint laneIdent, string sender, string phrase, uint? tracePhraseKind = null, string laneLabel = "")
    {
        Push(new ChatRow(ChatFlavor.Channel, sender, phrase, SenderGuid: 0, laneIdent)
        {
            LaneLabel = laneLabel,
            TracePhraseKind = tracePhraseKind ?? LegacyChannelType.Resolve(laneIdent, ownTransmit: false),
        });
    }

    public void OnTellReceived(string sender, string phrase, uint senderOid, uint tracePhraseKind)
    {
        Push(new ChatRow(ChatFlavor.Tell, sender, phrase, senderOid, ChannelId: 0) { TracePhraseKind = tracePhraseKind });
    }

    /// <summary>System lines repeated within a second are collapsed; the window slides with each repeat.</summary>
    public void OnSysMsg(string phrase, uint commsKind)
    {
        DateTime instant = DateTime.UtcNow;
        bool repeat = phrase == _previousSysStroke && instant - _previousSysAt < SysRepeatPane;
        _previousSysStroke = phrase;
        _previousSysAt = instant;
        if (repeat)
            return;

        Push(new ChatRow(ChatFlavor.System, "", phrase, SenderGuid: 0, commsKind) { TracePhraseKind = commsKind });
    }

    public void OnPopup(string phrase)
    {
        Push(new ChatRow(ChatFlavor.Popup, "", phrase, SenderGuid: 0, ChannelId: 0) { TracePhraseKind = 0x00u });
    }

    public void OnFightingStroke(string phrase, uint tracePhraseKind, FightLineKind sort = FightLineKind.Info)
    {
        Push(new ChatRow(ChatFlavor.Combat, "", phrase, SenderGuid: 0, ChannelId: 0)
        {
            FightingSort = sort,
            TracePhraseKind = tracePhraseKind,
        });
    }

    public void OnSelfSent(ChatFlavor sort, string phrase, uint tracePhraseKind, string markOrLane = "")
    {
        Push(new ChatRow(sort, sort == ChatFlavor.Tell ? markOrLane : "", phrase, SenderGuid: 0, ChannelId: 0)
        {
            LaneLabel = sort == ChatFlavor.Channel ? markOrLane : "",
            TracePhraseKind = tracePhraseKind,
        });
    }

    private void Push(ChatRow stroke)
    {
        _strokes.Enqueue(stroke);
        while (_strokes.Count > _cap)
            _strokes.TryDequeue(out _);
        Interlocked.Increment(ref _rev);
        EntryAppended?.Invoke(stroke);
    }
}

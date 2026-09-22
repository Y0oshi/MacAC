using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fellows;

namespace MacAC.Sim.Play;

public readonly record struct SimCommsEvent(ulong Sequence, SimCommsEntry Entry);

public interface ISimCommsWatcher
{
    void OnComms(in SimCommsEvent diff);
}

public interface ISimCommsEventFeed
{
    IDisposable Subscribe(ISimCommsWatcher watcher);
}

public readonly record struct SimCommsHoldingCapture(
    bool IsDisposed,
    bool CommandTargetsDisposed,
    int StreamSubscriberCount,
    int PendingDispatchCount,
    bool IsDispatching,
    int FriendCount,
    int SquelchAccountCount,
    int SquelchCharacterCount,
    int SquelchGlobalTypeCount,
    int NegotiatedRoomCount,
    bool HasReplyTarget,
    bool HasRetellTarget,
    long DispatchFailureCount)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed && CommandTargetsDisposed
        && StreamSubscriberCount is 0 && PendingDispatchCount is 0 && !IsDispatching
        && FriendCount is 0 && SquelchAccountCount is 0 && SquelchCharacterCount is 0 && SquelchGlobalTypeCount is 0
        && NegotiatedRoomCount is 0 && !HasReplyTarget && !HasRetellTarget;
        }
    }
}

public sealed class SimCommsLedger : IDisposable
{
    private readonly SimCommsEventFlow _flow;
    private bool _destroyed;

    public SimCommsLedger(int ceilingCommsListings = 500)
    {
        Chat = new ChatTranscript(ceilingCommsListings);
        SpewBox = new SpewPaneState();
        DirectiveMarks = new CommandTargetState(Chat);
        _flow = new SimCommsEventFlow(Chat);
        TurbineChat = new TurbineChatPhase();
        Friends = new FriendsLedger();
        Squelch = new SquelchLedger();
        CommsPanes = new ChatPaneState();
        View = new Lens(Chat);
        SocialView = new SocialLens(TurbineChat, Friends, Squelch);
    }

    public ChatTranscript Chat { get; }
    public ChatPaneState CommsPanes { get; }
    public SpewPaneState SpewBox { get; }
    public CommandTargetState DirectiveMarks { get; }
    public TurbineChatPhase TurbineChat { get; }
    public FriendsLedger Friends { get; }
    public SquelchLedger Squelch { get; }
    public ISimCommsLens View { get; }
    public ISimSocialLens SocialView { get; }
    public ISimCommsEventFeed Events => _flow;

    public Func<bool>? ReadoutTimestampsSource
    {
        get => Chat.ReadoutTimestampsSrc;
        set => Chat.ReadoutTimestampsSrc = value;
    }

    public bool IsDisposed => _destroyed;
    public ulong LastSeries => _flow.LastSequence;
    public int SubscriberCount => _flow.SubscriberCount;
    public int QueuedDispatchCount => _flow.PendingRelayCount;
    public bool IsDispatching => _flow.IsDispatching;
    public long DispatchFailureCount => _flow.DispatchFailureCount;
    public Exception? LastDispatchFailure => _flow.LastDispatchFailure;

    public SimCommsHoldingCapture CaptureOwnership()
    {
        SquelchBook squelch = Squelch.Snapshot();
        return new SimCommsHoldingCapture(
            _destroyed,
            DirectiveMarks.IsDisposed,
            SubscriberCount,
            QueuedDispatchCount,
            IsDispatching,
            Friends.Count,
            squelch.Accounts.Count,
            squelch.Characters.Count,
            squelch.Global.MessageTypes.Count,
            HallsJoined(TurbineChat),
            DirectiveMarks.LastIncomingTellSender is not null,
            DirectiveMarks.LastOutgoingTellTarget is not null,
            DispatchFailureCount);
    }

    public void RestartDirectiveMarks() => DirectiveMarks.ResetSession();
    public void RestartCommsPersona() => Chat.RestartSessPersona();
    public void RestartNegotiatedLanes() => TurbineChat.Reset();
    public void RestartFriends() => Friends.Clear();
    public void RestartSquelch() => Squelch.Clear();
    public void RestartSpewBbox() => SpewBox.Reset();

    public void AddText(string phrase, CanonLogTextType kind, uint paneIdent = 0)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        phrase = phrase.Trim();
        if (kind == CanonLogTextType.ClientLocal)
            SpewBox.Enqueue(phrase);
        else
            Chat.OnSysMsg(phrase, (uint)kind);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _flow.Dispose();
        DirectiveMarks.ResetSession();
        DirectiveMarks.Dispose();
        TurbineChat.Reset();
        Friends.Clear();
        Squelch.Clear();
        Chat.RestartSessPersona();
        SpewBox.Reset();
        CommsPanes.RewindToDefaults();
    }

    private static int HallsJoined(TurbineChatPhase comms)
    {
        ReadOnlySpan<uint> halls = [comms.AllegianceHall, comms.GeneralHall, comms.BarterHall, comms.LfgHall, comms.RoleplayHall, comms.SocietyHall, comms.OlthoiHall];
        int joined = 0;
        foreach (uint hall in halls)
        {
            if (hall is not 0u)
                ++joined;
        }
        return joined;
    }

    private sealed class Lens(ChatTranscript comms) : ISimCommsLens
    {
        public long Revision => comms.Revision;
        public int Count => comms.Count;
    }

    private sealed class SocialLens(TurbineChatPhase turbineComms, FriendsLedger friends, SquelchLedger squelch) : ISimSocialLens
    {
        public SimSocialCapture Snapshot
        {
            get
            {
                SquelchBook book = squelch.Snapshot();
                return new SimSocialCapture(
                    friends.Revision, friends.Count, squelch.Revision,
                    book.Accounts.Count, book.Characters.Count, book.Global.MessageTypes.Count, HallsJoined(turbineComms));
            }
        }

        public bool TryFetchFriend(uint toonIdent, out SimBuddyCapture friend)
        {
            if (friends.TryGet(toonIdent, out FriendRow? rank) && rank is not null)
            {
                friend = new SimBuddyCapture(rank.Id, rank.Name, rank.Online, rank.AppearOffline);
                return true;
            }
            friend = default;
            return false;
        }
    }
}

// Turns transcript appends into watcher events
internal sealed class SimCommsEventFlow : ISimCommsEventFeed, IDisposable
{
    private readonly ChatTranscript _comms;
    private readonly object _latch = new();
    private readonly List<SimCommsEvent> _fifo = [];
    private ISimCommsWatcher[] _watchers = [];
    private long _series;
    private long _misses;
    private bool _draining;
    private bool _destroyed;

    public SimCommsEventFlow(ChatTranscript chat)
    {
        _comms = chat ?? throw new ArgumentNullException(nameof(chat));
        _comms.EntryAppended += OnAppended;
    }

    public int SubscriberCount => Volatile.Read(ref _watchers).Length;

    public ulong LastSequence
    {
        get { lock (_latch) return unchecked((ulong)_series); }
    }

    public int PendingRelayCount
    {
        get { lock (_latch) return _fifo.Count; }
    }

    public bool IsDispatching
    {
        get { lock (_latch) return _draining; }
    }

    public long DispatchFailureCount => Interlocked.Read(ref _misses);

    public Exception? LastDispatchFailure { get; private set; }

    public IDisposable Subscribe(ISimCommsWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            var latest = _watchers;
            if (Array.IndexOf(latest, watcher) >= 0)
                throw new InvalidOperationException("The communication observer is by now subscribed");
            Volatile.Write(ref _watchers, [.. latest, watcher]);
        }
        return new SubscriptionUnit(this, watcher);
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _comms.EntryAppended -= OnAppended;
            _fifo.Clear();
            _draining = false;
            Volatile.Write(ref _watchers, []);
        }
    }

    private void Delist(ISimCommsWatcher watcher)
    {
        lock (_latch)
        {
            var latest = _watchers;
            int ordinal = Array.IndexOf(latest, watcher);
            if (ordinal < 0)
                return;
            ISimCommsWatcher[] leftover = new ISimCommsWatcher[latest.Length - 1];
            Array.Copy(latest, 0, leftover, 0, ordinal);
            Array.Copy(latest, ordinal + 1, leftover, ordinal, latest.Length - ordinal - 1);
            Volatile.Write(ref _watchers, leftover);
        }
    }

    private void OnAppended(ChatRow rank)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            ulong series = unchecked((ulong)++_series);
            _fifo.Add(new SimCommsEvent(
                series,
                new SimCommsEntry(_comms.Revision, rank.SenderGuid, (int)rank.Kind, rank.Sender, rank.Text, rank.LaneLabel)));
            if (_draining)
                return;
            _draining = true;
        }

        for (int ordinal = 0; ; ++ordinal)
        {
            SimCommsEvent upcoming;
            lock (_latch)
            {
                if (ordinal >= _fifo.Count)
                {
                    _fifo.Clear();
                    _draining = false;
                    return;
                }
                upcoming = _fifo[ordinal];
            }
            Deliver(in upcoming);
        }
    }

    private void Deliver(in SimCommsEvent diff)
    {
        foreach (ISimCommsWatcher watcher in Volatile.Read(ref _watchers))
        {
            try
            {
                watcher.OnComms(in diff);
            }
            catch (Exception problem)
            {
                Interlocked.Increment(ref _misses);
                LastDispatchFailure = problem;
            }
        }
    }

    private sealed class SubscriptionUnit(SimCommsEventFlow holder, ISimCommsWatcher watcher) : IDisposable
    {
        private SimCommsEventFlow? _holder = holder;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.Delist(watcher);
    }
}

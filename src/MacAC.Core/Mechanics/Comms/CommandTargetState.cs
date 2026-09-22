namespace MacAC.Mechanics.Comms;

public sealed class CommandTargetState : IDisposable
{
    private const uint MonarchLane = 0x00004000u;
    private const uint PatronLane = 0x00002000u;

    private readonly ChatTranscript _comms;
    private readonly Lock _synchronize = new();
    private string? _tellFrom;
    private string? _tellTo;
    private string? _monarch;
    private string? _patron;
    private bool _destroyed;

    public CommandTargetState(ChatTranscript chat)
    {
        _comms = chat ?? throw new ArgumentNullException(nameof(chat));
        _comms.EntryAppended += Observe;
    }

    public string? LastIncomingTellSender => Read(ref _tellFrom);

    public string? LastOutgoingTellTarget => Read(ref _tellTo);

    public string? LastMonarchSender => Read(ref _monarch);

    public string? LastPatronSender => Read(ref _patron);

    public bool IsDisposed
    {
        get
        {
            lock (_synchronize)
                return _destroyed;
        }
    }

    public void ResetSession()
    {
        lock (_synchronize)
            _tellFrom = _tellTo = _monarch = _patron = null;
    }

    public void Dispose()
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _comms.EntryAppended -= Observe;
        }
    }

    private string? Read(ref string? socket)
    {
        lock (_synchronize)
            return socket;
    }

    private void Observe(ChatRow stroke)
    {
        if (string.IsNullOrEmpty(stroke.Sender))
            return;

        lock (_synchronize)
        {
            if (_destroyed)
                return;
            switch (stroke.Kind)
            {
                case ChatFlavor.Tell when stroke.SenderGuid is not 0u:
                    _tellFrom = stroke.Sender;
                    break;
                case ChatFlavor.Tell:
                    _tellTo = stroke.Sender;
                    break;
                case ChatFlavor.Channel when stroke.ChannelId == MonarchLane:
                    _monarch = stroke.Sender;
                    break;
                case ChatFlavor.Channel when stroke.ChannelId == PatronLane:
                    _patron = stroke.Sender;
                    break;
            }
        }
    }
}

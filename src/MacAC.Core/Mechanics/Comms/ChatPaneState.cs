namespace MacAC.Mechanics.Comms;

public sealed class ChatPaneState
{
    public const int PrimaryPaneIdent = 0;
    public const int LowerFloatingPaneIdent = 1;
    public const int UpperFloatingPaneIdent = 4;

    public const ulong PrimaryPaneDefaultSift = 0xFBFFFFFFul;
    public const ulong Floaty1DefaultSift = 0x0000101Cul;   // Speech, Tell, Speech_Direct_Send, Emote
    public const ulong Floaty2DefaultSift = 0x00040C00ul;   // Social, Social_Send, Allegiance
    public const ulong Floaty3DefaultSift = 0x00080000ul;   // Fellowship
    public const ulong Floaty4DefaultSift = 0x78000000ul;

    public const uint AirMarkPane = uint.MaxValue;

    private const int Windows = UpperFloatingPaneIdent + 1;
    private const uint SiftBitset = 64u;

    private static readonly ulong[] Defaults =
    [
        PrimaryPaneDefaultSift,
        Floaty1DefaultSift,
        Floaty2DefaultSift,
        Floaty3DefaultSift,
        Floaty4DefaultSift,
    ];

    private readonly Lock _synchronize = new();
    private readonly ulong[] _filters = new ulong[Windows];
    private readonly bool[] _open = new bool[Windows];
    private long _rev;

    public ChatPaneState() => RewindToDefaults();

    public long Revision => Interlocked.Read(ref _rev);

    public void RewindToDefaults()
    {
        lock (_synchronize)
        {
            Defaults.CopyTo(_filters, 0);
            Array.Clear(_open);
            _open[PrimaryPaneIdent] = true;
            Interlocked.Increment(ref _rev);
        }
    }

    public ulong FetchSift(int paneIdent)
    {
        Check(paneIdent);
        lock (_synchronize)
            return _filters[paneIdent];
    }

    public void ApplySift(int paneIdent, ulong sift)
    {
        Check(paneIdent);
        lock (_synchronize)
        {
            if (_filters[paneIdent] == sift)
                return;
            _filters[paneIdent] = sift;
            Interlocked.Increment(ref _rev);
        }
    }

    public bool IsOpen(int paneIdent)
    {
        Check(paneIdent);
        if (paneIdent == PrimaryPaneIdent)
            return true;
        lock (_synchronize)
            return _open[paneIdent];
    }

    public void SetOpen(int paneIdent, bool open)
    {
        Check(paneIdent);
        if (paneIdent == PrimaryPaneIdent)
            return;
        lock (_synchronize)
        {
            if (_open[paneIdent] == open)
                return;
            _open[paneIdent] = open;
            Interlocked.Increment(ref _rev);
        }
    }

    public bool Toggle(int paneIdent)
    {
        Check(paneIdent);
        if (paneIdent == PrimaryPaneIdent)
            return true;
        lock (_synchronize)
        {
            _open[paneIdent] = !_open[paneIdent];
            Interlocked.Increment(ref _rev);
            return _open[paneIdent];
        }
    }

    public bool KindIsEngaged(int paneIdent, uint tracePhraseKind)
    {
        Check(paneIdent);
        if (tracePhraseKind >= SiftBitset)
            return false;
        ulong sift;
        lock (_synchronize)
            sift = _filters[paneIdent];
        return (sift & (1UL << (int)tracePhraseKind)) is not 0UL;
    }

    public bool ShouldReadout(int paneIdent, uint markPaneIdent, uint tracePhraseKind)
    {
        Check(paneIdent);
        if (markPaneIdent == (uint)paneIdent)
            return true;
        return markPaneIdent == AirMarkPane && KindIsEngaged(paneIdent, tracePhraseKind);
    }

    private static void Check(int windowId)
    {
        if (windowId is < PrimaryPaneIdent or > UpperFloatingPaneIdent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowId), windowId, "chat window id has to be 0 (main) through 4 (floating)");
        }
    }
}

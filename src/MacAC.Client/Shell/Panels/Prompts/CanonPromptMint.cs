namespace MacAC.Client.Shell.Panels;

public sealed partial class CanonPromptMint(
    WidgetTrunk host,
    Func<CanonPromptType, ImportedArrangement?> createLayout) : IDisposable
{
    public const uint DefaultQueueKey = 2u;

    public const uint NonQueuedTag = 1u;

    private sealed class PromptInfo
    {
        public required CanonPromptData Data { get; init; }
        public required uint Context { get; init; }
        public required uint FifoLookupKey { get; init; }
        public required ulong Series { get; init; }
        public Action<CanonPromptData>? Hook { get; init; }
        public ICanonPromptView? View { get; set; }
    }

    private readonly WidgetTrunk _hub = host ?? throw new ArgumentNullException(nameof(host));

    private readonly Func<CanonPromptType, ImportedArrangement?> _buildArrangement = createLayout ?? throw new ArgumentNullException(nameof(createLayout));

    private readonly Dictionary<uint, PromptInfo> _engagedQueued = [];

    private readonly Dictionary<uint, PromptInfo> _engagedNonQueued = [];

    private readonly Dictionary<uint, LinkedList<PromptInfo>> _queued = [];

    private readonly LinkedList<PromptInfo> _retryable = new();

    private readonly List<PromptInfo> _openOrdering = [];

    private uint _globalCtx;

    private ulong _globalSeries;

    private bool _resetting;

    private bool _destroyed;

    public event Action<uint, CanonPromptData>? DialogClosed;

    public event Action<uint>? DialogOpened;
}

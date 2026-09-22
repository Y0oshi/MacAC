namespace MacAC.Client.Graphics.Stage;

internal enum DirectionalShadeCasterKind : byte
{
    OutdoorStatic,
    Building,
    AnimatedStatic,
    LiveDynamic,
    EquippedChild,
}

internal readonly record struct DirectionalShadeCaster(
    RenderMirrorRecord Projection,
    DirectionalShadeCasterKind Kind)
{
    public bool UsesLatestMovingXforms
    {
        get
        {
            return Projection.ProjectionClass
            is RenderMirrorClass.ActiveAnimatedStatic
            or RenderMirrorClass.LiveDynamicRoot
            or RenderMirrorClass.EquippedChild;
        }
    }
}

internal readonly struct DirectionalShadeChangedPose
{
    internal DirectionalShadeChangedPose(
        int invokerOrdinal,
        in DirectionalShadeTransformCapture capture)
    {
        InvokerOrdinal = invokerOrdinal;
        Capture = capture;
    }

    internal readonly int InvokerOrdinal;
    internal readonly DirectionalShadeTransformCapture Capture;
}

internal readonly record struct DirectionalShadeCasterClassTelemetry(
    int TerrainCommands,
    int OutdoorStatics,
    int Buildings,
    int AnimatedStatics,
    int LocalPlayers,
    int RemotePlayers,
    int NonPlayerCreatures,
    int OtherLiveDynamics,
    int EquippedChildren);

internal readonly record struct DirectionalShadeCasterBuildStats(
    int SourceOutdoorStatics,
    int SourceOutdoorDynamics,
    int Accepted,
    int RejectedNotDrawable,
    int RejectedNotResident,
    int RejectedTransparent,
    int RejectedIndoor,
    int RejectedMissingMesh,
    int IndexCopies,
    int Classifications,
    int DynamicTransformRefreshes,
    bool TopologyRebuilt,
    int CopiedTransformChanges = 0,
    int DedupedChangedCasterSlots = 0,
    bool TransformJournalFullRefresh = false,
    int UpdateTransformChanges = 0,
    int UpdateAppearanceChanges = 0,
    int DynamicSynchronizationChanges = 0,
    int ActiveAnimatedStaticChanges = 0,
    int LiveDynamicRootChanges = 0,
    int EquippedChildChanges = 0,
    bool DensityBulkRefresh = false,
    int BatchedProjectionCopyCalls = 0,
    int ActiveSelected = 0)
{
    public DirectionalShadeCasterClassTelemetry InvokerClasses { get; init; }
}

internal sealed partial class DirectionalShadeCasterFrame
{
    private RenderMirrorRecord[] _exteriorStaticTemp = [];

    private RenderMirrorRecord[] _exteriorDynamicTemp = [];

    private DirectionalShadeCaster[] _casters = [];

    private bool[] _chosenCasters = [];

    private int[] _renewInvokerSockets = [];

    private DirectionalShadeChangedPose[] _alteredInvokerPostures = [];

    private bool[] _alteredInvokerFlagSet = [];

    private RenderMirrorId[] _invokerIdents = [];

    private RenderMirrorClass[] _invokerClasses = [];

    private RenderMirrorId[] _denseIdentTemp = [];

    private RenderMirrorRecord[] _denseCaptureTemp = [];

    private int[] _orderOrdinals = [];

    private ulong[] _orderTags = [];

    private DirectionalShadeCaster[] _orderTemp = [];

    private readonly DirectionalShadeTransformCapture[] _xformEditTemp =
        new DirectionalShadeTransformCapture[
            DirectionalShadeTransformChangeDiary.Capacity];

    private readonly Dictionary<RenderMirrorId, int> _renewInvokerSocketByIdent = [];

    private int _invokerTally;

    private int _renewInvokerSocketTally;

    private int _alteredInvokerPostureTally;

    private ulong _wiringRev;
    private DirectionalShadeTransformChanges _previousXformEdits;

    private bool _previousDensityBulkRenew;

    private int _previousBatchedProjDuplicateCalls;

    private static void SecureCap<T>(ref T[] vals, int required)
    {
        if (required < 0)
            throw new ArgumentOutOfRangeException(nameof(required));
        if (vals.Length >= required)
            return;
        int cap = vals.Length is 0 ? 4 : vals.Length;
        while (cap < required)
            cap = checked(cap * 2);
        Array.Resize(ref vals, cap);
    }

    private readonly struct InvokerOrdinalOrdering(
        ulong[] tags,
        DirectionalShadeCaster[] casters) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            ulong left = tags[x];
            ulong right = tags[y];
            if (left != right)
                return left < right ? -1 : 1;
            int ordering = DirectionalShadeCasterComparer.Instance.Compare(
                casters[x],
                casters[y]);
            return ordering is not 0 ? ordering : x.CompareTo(y);
        }
    }

    private sealed class DirectionalShadeCasterComparer
        : IComparer<DirectionalShadeCaster>
    {
        public static DirectionalShadeCasterComparer Instance { get; } = new();

        public int Compare(DirectionalShadeCaster left, DirectionalShadeCaster right)
        {
            int ordering = left.Projection.SortKey.Value.CompareTo(
                right.Projection.SortKey.Value);
            return ordering is not 0
                ? ordering
                : left.Projection.Id.CompareTo(right.Projection.Id);
        }
    }
}

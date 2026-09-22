using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

[Flags]
public enum LandblockSunsetJuncture : ushort
{
    None = 0,
    MeshReferences = 1 << 0,
    StaticScripts = 1 << 1,
    Classification = 1 << 2,
    EntityLighting = 1 << 3,
    EntityTranslucency = 1 << 4,
    PluginProjection = 1 << 5,
    Physics = 1 << 6,
    Terrain = 1 << 7,
    CellVisibility = 1 << 8,
    BuildingRegistry = 1 << 9,
    EnvironmentCells = 1 << 10,
    LegacyPresentation = 1 << 11,
}

internal enum LandblockSunsetOpOutcome : byte
{
    NoWork,
    Progressed,
    Pending,
    Failed,
}

public sealed partial class LandblockSunsetTicket
{
    private readonly Dictionary<LandblockSunsetJuncture, int> _actorCursors = [];

    private readonly Dictionary<LandblockSunsetJuncture, Exception> _misses = [];

    private Exception? _hookMiss;

    private Exception? _previousReportedMiss;

    internal LandblockSunsetTicket(
        GpuLandblockSunset phase,
        LandblockSunsetJuncture neededJunctures)
    {
        State = phase;
        NeededJunctures = neededJunctures;
    }

    public GpuLandblockSunset State { get; }

    public uint LandblockId => State.LandblockId;

    public LandblockSunsetFlavor Kind => State.Kind;

    public IReadOnlyList<RealmActor> Entities => State.Entities;

    public LandblockSunsetJuncture NeededJunctures { get; }

    public LandblockSunsetJuncture FinishedJunctures { get; private set; }

    public bool IsComplete
    {
        get
        {
            return (FinishedJunctures & NeededJunctures) == NeededJunctures
        && _hookMiss is null;
        }
    }

    internal LandblockSunsetJuncture UpcomingIncompleteJuncture
    {
        get
        {
            ushort leftover = (ushort)(NeededJunctures & ~FinishedJunctures);
            if (leftover is 0)
                return LandblockSunsetJuncture.None;

            ushort lowest = (ushort)(leftover & (ushort)-(short)leftover);
            return (LandblockSunsetJuncture)lowest;
        }
    }

    internal void CommenceAttempt() => _hookMiss = null;

    internal void CaptureHookMiss(Exception problem) =>
        _hookMiss = problem;

    internal bool TryGrabNewMiss(out Exception? problem)
    {
        problem = _hookMiss ?? _misses.Values.FirstOrDefault();
        if (problem is null || ReferenceEquals(problem, _previousReportedMiss))
            return false;

        _previousReportedMiss = problem;
        return true;
    }

    private static void VetSingleJuncture(LandblockSunsetJuncture stage)
    {
        ushort val = (ushort)stage;
        if (val is 0 || (val & (val - 1)) is not 0)
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A retirement operation must own precisely one stage bit");
    }
}

public sealed partial class LandblockRetirementMarshal
{
    private enum AllottedAdvanceResult : byte
    {
        Progressed,
        Yielded,
        Failed,
    }

    private enum AskKind : byte
    {
        BeginFull,
        BeginNearLayer,
        Advance,
    }

    private readonly record struct ClientRequest(AskKind Kind, uint LandblockId);

    private const LandblockSunsetJuncture CoreJunctures =
        LandblockSunsetJuncture.MeshReferences
        | LandblockSunsetJuncture.StaticScripts
        | LandblockSunsetJuncture.Classification;

    public const LandblockSunsetJuncture ProductionExhibitJunctures =
        LandblockSunsetJuncture.EntityLighting
        | LandblockSunsetJuncture.EntityTranslucency
        | LandblockSunsetJuncture.PluginProjection
        | LandblockSunsetJuncture.Terrain
        | LandblockSunsetJuncture.Physics
        | LandblockSunsetJuncture.CellVisibility
        | LandblockSunsetJuncture.BuildingRegistry
        | LandblockSunsetJuncture.EnvironmentCells;

    private readonly GpuRealmPhase _phase;

    private readonly Action<LandblockSunsetTicket> _proceedExhibit;

    private readonly Func<
        LandblockSunsetTicket,
        LandblockSunsetOpOutcome>? _proceedExhibitHop;

    private readonly Func<LandblockSunsetFlavor, LandblockSunsetJuncture>
        _neededExhibitJunctures;

    private readonly Action<GpuLandblockSunset>? _onDetached;

    private readonly Dictionary<uint, List<LandblockSunsetTicket>> _queued = [];

    private readonly LinkedList<LandblockSunsetTicket> _queuedOrdering = new();

    private readonly Dictionary<
        LandblockSunsetTicket,
        LinkedListNode<LandblockSunsetTicket>> _queuedJoints =
            new(ReferenceEqualityComparer.Instance);

    private readonly List<uint> _finishedIdents = [];

    private readonly Queue<ClientRequest> _reqs = new();

    private readonly HashSet<ClientRequest> _observedDuringEmpty = [];

    private bool _drainingReqs;

    public LandblockRetirementMarshal(
        GpuRealmPhase phase,
        Action<LandblockSunsetTicket> proceedExhibit,
        Func<LandblockSunsetFlavor, LandblockSunsetJuncture>? neededExhibitJunctures = null)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(proceedExhibit);
        _phase = phase;
        _proceedExhibit = proceedExhibit;
        _proceedExhibitHop = null;
        _onDetached = null;
        _neededExhibitJunctures = neededExhibitJunctures
            ?? (sort => sort == LandblockSunsetFlavor.Full
                ? ProductionExhibitJunctures
                : ProductionExhibitJunctures & ~LandblockSunsetJuncture.Terrain);
    }

    private LandblockRetirementMarshal(
        GpuRealmPhase state,
        Func<LandblockSunsetTicket, LandblockSunsetOpOutcome>
            advancePresentationStep,
        Action<LandblockSunsetTicket> advancePresentation,
        Func<LandblockSunsetFlavor, LandblockSunsetJuncture>
            requiredPresentationStages,
        Action<GpuLandblockSunset>? onDetached)
    {
        _phase = state ?? throw new ArgumentNullException(nameof(state));
        _proceedExhibitHop = advancePresentationStep
            ?? throw new ArgumentNullException(nameof(advancePresentationStep));
        _proceedExhibit = advancePresentation
            ?? throw new ArgumentNullException(nameof(advancePresentation));
        _neededExhibitJunctures = requiredPresentationStages
            ?? throw new ArgumentNullException(nameof(requiredPresentationStages));
        _onDetached = onDetached;
    }
}

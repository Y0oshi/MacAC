namespace MacAC.Mechanics.Gear;

public enum OpenContainerShiftKind
{
    Opened,
    ReplacementRequested,
    Closed,
    Reset,
}

public readonly record struct OpenContainerShift(
    OpenContainerShiftKind Kind,
    uint PreviousContainerId,
    uint ContainerId);

public sealed class OpenContainerState
{
    private readonly HashSet<uint> _corpsesLooted = [];

    public uint AskedVesselIdent { get; private set; }
    public uint LatestVesselIdent { get; private set; }
    public int OpenedCorpseTally => _corpsesLooted.Count;

    public event Action<OpenContainerShift>? Changed;

    public bool ReqOpen(uint vesselIdent, bool isCorpse = false)
    {
        if (vesselIdent is 0u)
            return false;

        if (isCorpse)
            _corpsesLooted.Add(vesselIdent);

        if (AskedVesselIdent == vesselIdent)
            return false;

        uint shown = LatestVesselIdent;
        AskedVesselIdent = vesselIdent;

        // Asking for a different container while one is open drops the open one now.
        if (shown is not 0u && shown != vesselIdent)
        {
            LatestVesselIdent = 0u;
            Raise(OpenContainerShiftKind.ReplacementRequested, shown, vesselIdent);
        }
        return true;
    }

    public bool HasCorpseBeenOpened(uint objectIdent) => objectIdent is not 0u && _corpsesLooted.Contains(objectIdent);

    public bool AssignCorpseDeleted(uint objectIdent) => objectIdent is not 0u && _corpsesLooted.Remove(objectIdent);

    public bool ImposeLensInsides(uint vesselIdent)
    {
        if (vesselIdent is 0u || vesselIdent != AskedVesselIdent)
            return false;

        uint shown = LatestVesselIdent;
        LatestVesselIdent = vesselIdent;
        AskedVesselIdent = vesselIdent;
        if (shown == vesselIdent)
            return false;

        Raise(OpenContainerShiftKind.Opened, shown, vesselIdent);
        return true;
    }

    public bool ImposeShut(uint vesselIdent)
    {
        if (vesselIdent is 0u || vesselIdent != LatestVesselIdent)
            return false;

        uint shown = LatestVesselIdent;
        LatestVesselIdent = 0u;
        AskedVesselIdent = 0u;
        Raise(OpenContainerShiftKind.Closed, shown, 0u);
        return true;
    }

    /// <summary>A failed use rolls the request back to whatever is actually open.</summary>
    public bool ImposeUseDone(uint weenieProblem)
    {
        if (weenieProblem is 0u || AskedVesselIdent == LatestVesselIdent)
            return false;
        AskedVesselIdent = LatestVesselIdent;
        return true;
    }

    public bool Reset()
    {
        uint shown = LatestVesselIdent;
        bool hadPhase = shown is not 0u || AskedVesselIdent is not 0u || _corpsesLooted.Count is not 0;
        LatestVesselIdent = 0u;
        AskedVesselIdent = 0u;
        _corpsesLooted.Clear();

        OpenContainerShift shift = new OpenContainerShift(OpenContainerShiftKind.Reset, shown, 0u);
        List<Exception>? misses = null;
        foreach (Action<OpenContainerShift> listener in Changed?.GetInvocationList() ?? [])
        {
            try { listener(shift); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }
        if (misses is not null)
            throw new AggregateException("One or more external-container reset observers failed", misses);
        return hadPhase;
    }

    private void Raise(OpenContainerShiftKind sort, uint earlier, uint latest) =>
        Changed?.Invoke(new OpenContainerShift(sort, earlier, latest));
}

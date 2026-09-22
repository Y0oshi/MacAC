namespace MacAC.Mechanics.Kinetics;

public enum SpawnStampVerdict
{
    StaleGeneration,
    InitialGeneration,
    ExistingGeneration,
    NewGeneration,
}

public enum PoseStampVerdict
{
    Rejected,
    Apply,
    ForcePosition,
}

public sealed class KineticStampGate
{
    private enum MechChannel
    {
        Position,
        Movement,
        State,
        Vector,
        Teleport,
        ServerControlledMove,
        ForcePosition,
        ObjDesc,
        Instance,
    }

    private const int LaneTally = 9;
    private const int HalfSpan = 0x7FFF;

    private readonly ushort[] _stamps = new ushort[LaneTally];
    private bool _seeded;

    private ushort this[MechChannel c]
    {
        get => _stamps[(int)c];
        set => _stamps[(int)c] = value;
    }

    public ushort LocusStamp => this[MechChannel.Position];
    public ushort TravelStamp => this[MechChannel.Movement];
    public ushort PhaseStamp => this[MechChannel.State];
    public ushort VectorStamp => this[MechChannel.Vector];
    public ushort WarpStamp => this[MechChannel.Teleport];
    public ushort SrvControlledRelocateStamp => this[MechChannel.ServerControlledMove];
    public ushort ForceLocusStamp => this[MechChannel.ForcePosition];
    public ushort ObjRefDscStamp => this[MechChannel.ObjDesc];
    public ushort InstStamp => this[MechChannel.Instance];

    /// <summary>Wrap-aware "newStamp comes after oldStamp".</summary>
    public static bool IsNewer(ushort formerStamp, ushort newStamp)
    {
        int gap = Math.Abs((int)newStamp - formerStamp);
        return gap > HalfSpan ? newStamp < formerStamp : formerStamp < newStamp;
    }

    public SpawnStampVerdict SeedForBuildObject(
        ushort locus,
        ushort travel,
        ushort phase,
        ushort vector,
        ushort warp,
        ushort srvControlledRelocate,
        ushort forceLocus,
        ushort objRefDsc,
        ushort inst)
    {
        ReadOnlySpan<ushort> incoming =
            [locus, travel, phase, vector, warp, srvControlledRelocate, forceLocus, objRefDsc, inst];

        if (!_seeded)
        {
            incoming.CopyTo(_stamps);
            _seeded = true;
            return SpawnStampVerdict.InitialGeneration;
        }

        var verdict = JudgeInst(inst);
        if (verdict == SpawnStampVerdict.NewGeneration)
            incoming.CopyTo(_stamps);
        return verdict;
    }

    public SpawnStampVerdict PreviewBuildObject(ushort inst) =>
        _seeded ? JudgeInst(inst) : SpawnStampVerdict.InitialGeneration;

    public bool TryAdmitTravelSignal(ushort inst, ushort travel, ushort srvControlledRelocate)
    {
        if (!IsLatestInst(inst) || !Advance(MechChannel.Movement, travel))
            return false;
        if (IsNewer(srvControlledRelocate, this[MechChannel.ServerControlledMove]))
            return false;
        this[MechChannel.ServerControlledMove] = srvControlledRelocate;
        return true;
    }

    public bool TryAdmitPhaseSignal(ushort inst, ushort phase) =>
        IsLatestInst(inst) && Advance(MechChannel.State, phase);

    public bool TryAdmitVectorSignal(ushort inst, ushort vector) =>
        IsLatestInst(inst) && Advance(MechChannel.Vector, vector);

    public bool TryAdmitObjRefDscSignal(ushort inst, ushort objRefDsc) =>
        IsLatestInst(inst) && Advance(MechChannel.ObjDesc, objRefDsc);

    public bool TryAdmitLocusLaneSignal(ushort inst, ushort locus) =>
        IsLatestInst(inst) && Advance(MechChannel.Position, locus);

    public bool IsLatestInst(ushort inst) => _seeded && inst == this[MechChannel.Instance];

    public bool IsFreshWarpStart(ushort warp) => _seeded && !IsNewer(warp, this[MechChannel.Teleport]);

    public bool TryAdmitEraseSignal(ushort inst, bool isOwnAvatar = false) =>
        !isOwnAvatar && _seeded && inst == this[MechChannel.Instance];

    public PoseStampVerdict TryAdmitLocusSignal(
        ushort inst,
        ushort locus,
        ushort warp,
        ushort forceLocus,
        bool isOwnAvatar)
    {
        if (!IsLatestInst(inst))
            return PoseStampVerdict.Rejected;

        // A newer force-position stamp on the local player overrides the
        // ordinary ordering, unless a teleport is still ahead of it.
        if (isOwnAvatar && IsNewer(this[MechChannel.ForcePosition], forceLocus))
        {
            this[MechChannel.ForcePosition] = forceLocus;
            if (!IsNewer(warp, this[MechChannel.Teleport]))
            {
                this[MechChannel.Position] = locus;
                return PoseStampVerdict.ForcePosition;
            }
        }

        ushort precedingLocus = this[MechChannel.Position];
        if (!Advance(MechChannel.Position, locus))
            return PoseStampVerdict.Rejected;

        if (IsNewer(warp, this[MechChannel.Teleport]))
        {
            this[MechChannel.Position] = precedingLocus;
            return PoseStampVerdict.Rejected;
        }

        if (IsNewer(this[MechChannel.Teleport], warp))
            this[MechChannel.Teleport] = warp;
        return PoseStampVerdict.Apply;
    }

    private SpawnStampVerdict JudgeInst(ushort inst)
    {
        ushort latest = this[MechChannel.Instance];
        if (IsNewer(latest, inst))
            return SpawnStampVerdict.NewGeneration;
        return IsNewer(inst, latest) ? SpawnStampVerdict.StaleGeneration : SpawnStampVerdict.ExistingGeneration;
    }

    // Moves a channel forward only when the incoming stamp is strictly newer
    private bool Advance(MechChannel lane, ushort incoming)
    {
        if (!IsNewer(this[lane], incoming))
            return false;
        this[lane] = incoming;
        return true;
    }
}

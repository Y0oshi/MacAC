using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

internal sealed class CellOrderScratch
{
    private readonly List<List<uint>> _records = new();
    private int _depth;

    internal int EngagedZDepth => _depth;
    internal int KeptCaptureTally => _records.Count;
    internal IReadOnlyList<List<uint>> KeptRecords => _records;

    internal List<uint> Rent()
    {
        if (_depth == _records.Count)
            _records.Add(new List<uint>());

        List<uint> capture = _records[_depth++];
        capture.Clear();
        return capture;
    }

    internal void Yield(List<uint> capture)
    {
        int ordinal = _depth - 1;
        if (ordinal < 0 || !ReferenceEquals(_records[ordinal], capture))
        {
            throw new InvalidOperationException(
                "Cell-order scratch has to be returned in LIFO order");
        }

        capture.Clear();
        _depth = ordinal;
    }

    internal void RestartForReuse()
    {
        foreach (List<uint> capture in _records)
            capture.Clear();
        _depth = 0;
    }
}

public sealed class SweepPath
{
    public int NumSphere = 1;

    // Sphere arrays - index 0 = foot/body, index 1 = head (when NumSphere==2)
    public readonly Orb[] LocalSphere = new Orb[2] { new(), new() };
    public readonly Orb[] GlobalSphere = new Orb[2] { new(), new() };
    public readonly Orb[] GlobalCurrCenter = new Orb[2] { new(), new() };

    // Positions
    public Vector3 BeginPos;
    public Vector3 EndPos;
    public Vector3 CurPos;
    public Vector3 CheckPos;
    public Quaternion BeginOrientation = Quaternion.Identity;
    public Quaternion EndOrientation = Quaternion.Identity;
    public Quaternion CurOrientation = Quaternion.Identity;
    public Quaternion CheckOrientation = Quaternion.Identity;

    public uint CurCellId;
    public uint CheckCellId;

    public Vector3? CarriedBlockOrigin;

    public Vector3 GlobalOffset;

    public bool StepUp;
    public Vector3 StepUpNormal;
    public bool Collide;

    public bool StepDown;
    public float StepDownAmt;
    public float WalkInterp = 1.0f;

    public bool WalkableValid;
    public Plane WalkablePlane;
    public Vector3[]? WalkableVertices;
    public Vector3 WalkableUp = Vector3.UnitZ;
    public float WalkableAllowance = KineticConstants.FloorZ;
    public bool HasPassablePolyg => WalkableValid && WalkableVertices is { Length: >= 3 };

    public bool LastWalkableValid;
    public Plane LastWalkablePlane;
    public Vector3[]? LastWalkableVertices;
    public Vector3 LastWalkableUp = Vector3.UnitZ;
    public bool HasPreviousPassablePolyg => LastWalkableValid && LastWalkableVertices is { Length: >= 3 };

    public Vector3 BackupCheckPos;
    public uint BackupCheckCellId;

    public bool NegPolyHit;
    public bool NegStepUp;
    public Vector3 NegCollisionNormal;
    public bool CheckWalkable;
    public SlotKind InsertType = SlotKind.Transition;
    public bool PlacementAllowsSliding = true;

    public bool ObstructionEthereal;

    public bool BldgCheck;

    public bool HitsInteriorCell;

    private Vector3[]? _walkableVertexStorage;
    private Vector3[]? _lastWalkableVertexStorage;
    internal readonly ChamberArray CellCandidates = new();
    internal readonly ChamberArray SetLocusAskFootprint = new();
    internal readonly CellOrderScratch SequencedChamberTemp = new();

    internal Vector3[]? KeptPassableVertDepot => _walkableVertexStorage;
    internal Vector3[]? KeptPreviousPassableVertDepot => _lastWalkableVertexStorage;

    public void AssignVerifySpot(Vector3 spot, uint chamberIdent)
    {
        CheckPos = spot;
        CheckCellId = chamberIdent;
        for (int idx = 0; idx < NumSphere; ++idx)
        {
            GlobalSphere[idx].Center =
                Vector3.Transform(LocalSphere[idx].Center, CheckOrientation) + spot;
            GlobalSphere[idx].Radius = LocalSphere[idx].Radius;
        }
    }

    public void AppendShiftToVerifySpot(Vector3 shift)
    {
        CheckPos += shift;
        for (int idx = 0; idx < NumSphere; ++idx)
            GlobalSphere[idx].Center += shift;
    }

    public void PersistVerifySpot()
    {
        BackupCheckPos = CheckPos;
        BackupCheckCellId = CheckCellId;
    }

    public void ReinstateVerifySpot() => AssignVerifySpot(BackupCheckPos, BackupCheckCellId);

    public void AssignCollide(Vector3 impactNorm)
    {
        Collide = true;
        BackupCheckPos = CheckPos;
        BackupCheckCellId = CheckCellId;
        StepUpNormal = impactNorm;
        WalkInterp = 1.0f;
    }

    public void ApplyPassable(Plane plane, Vector3[] verts, Vector3 up)
    {
        ArgumentNullException.ThrowIfNull(verts);

        WalkableValid = true;
        WalkablePlane = plane;
        WalkableVertices = DuplicatePrecise(verts, ref _walkableVertexStorage);
        WalkableUp = up;
        WalkableAllowance = KineticConstants.FloorZ;

        LastWalkableValid = true;
        LastWalkablePlane = plane;
        LastWalkableVertices = DuplicatePrecise(verts, ref _lastWalkableVertexStorage);
        LastWalkableUp = up;
    }

    public void WipePassable()
    {
        WalkableValid = false;
        WalkableVertices = null;
    }

    public bool ReinstatePreviousPassable()
    {
        if (!HasPreviousPassablePolyg || LastWalkableVertices is null)
            return false;

        WalkableValid = true;
        WalkablePlane = LastWalkablePlane;
        WalkableVertices = DuplicatePrecise(LastWalkableVertices, ref _walkableVertexStorage);
        WalkableUp = LastWalkableUp;
        return true;
    }

    public void ResetForReuse()
    {
        NumSphere = 1;
        RestartOrbArr(LocalSphere);
        RestartOrbArr(GlobalSphere);
        RestartOrbArr(GlobalCurrCenter);

        BeginPos = Vector3.Zero;
        EndPos = Vector3.Zero;
        CurPos = Vector3.Zero;
        CheckPos = Vector3.Zero;
        BeginOrientation = Quaternion.Identity;
        EndOrientation = Quaternion.Identity;
        CurOrientation = Quaternion.Identity;
        CheckOrientation = Quaternion.Identity;
        CurCellId = 0;
        CheckCellId = 0;
        CarriedBlockOrigin = null;
        GlobalOffset = Vector3.Zero;
        StepUp = false;
        StepUpNormal = Vector3.Zero;
        Collide = false;
        StepDown = false;
        StepDownAmt = 0f;
        WalkInterp = 1.0f;
        WalkableValid = false;
        WalkablePlane = default;
        WalkableVertices = null;
        WalkableUp = Vector3.UnitZ;
        WalkableAllowance = KineticConstants.FloorZ;
        LastWalkableValid = false;
        LastWalkablePlane = default;
        LastWalkableVertices = null;
        LastWalkableUp = Vector3.UnitZ;
        BackupCheckPos = Vector3.Zero;
        BackupCheckCellId = 0;
        NegPolyHit = false;
        NegStepUp = false;
        NegCollisionNormal = Vector3.Zero;
        CheckWalkable = false;
        InsertType = SlotKind.Transition;
        PlacementAllowsSliding = true;
        ObstructionEthereal = false;
        BldgCheck = false;
        HitsInteriorCell = false;
        if (_walkableVertexStorage is not null)
            System.Array.Clear(_walkableVertexStorage);
        if (_lastWalkableVertexStorage is not null)
            System.Array.Clear(_lastWalkableVertexStorage);
        CellCandidates.Clear();
        CellCandidates.UnionMark = null;
        SetLocusAskFootprint.Clear();
        SequencedChamberTemp.RestartForReuse();
    }

    public ShiftVerdict StepUpSlide(Changeover changeover)
    {
        ContactLedger ledger = changeover.ContactLedger;
        ledger.ContactPlaneValid = false;
        ledger.ContactPlaneIsWater = false;
        return changeover.ShiftOrbInternal(StepUpNormal, GlobalCurrCenter[0].Center);
    }

    public ShiftVerdict PrecipiceSlide(Changeover changeover)
    {
        if (!HasPassablePolyg || WalkableVertices is null)
        {
            WipePassable();
            return ShiftVerdict.Collided;
        }

        if (!CellBspProbe.SeekCrossedRim(
                WalkablePlane,
                WalkableVertices,
                GlobalSphere[0].Center,
                WalkableUp,
                out var impactNorm))
        {
            WipePassable();
            return ShiftVerdict.Collided;
        }

        WipePassable();
        StepUp = false;

        Vector3 shift = GlobalSphere[0].Center - GlobalCurrCenter[0].Center;
        if (Vector3.Dot(impactNorm, shift) > 0f)
            impactNorm = -impactNorm;

        return changeover.ShiftOrbInternal(impactNorm, GlobalCurrCenter[0].Center);
    }

    public void InitPath(
        Vector3 commence,
        Vector3 finish,
        uint chamberIdent,
        float orbRadius,
        float orbHeight = 0f,
        Vector3? ownOrbOrigin = null,
        Quaternion? commenceFacing = null,
        Quaternion? finishFacing = null)
    {
        Vector3 origin0 = ownOrbOrigin ?? new Vector3(0, 0, orbRadius);
        if (orbHeight > 0)
        {
            PrimeTrailCore(
                commence, finish, chamberIdent, 2,
                origin0, orbRadius,
                new Vector3(0, 0, orbHeight - orbRadius), orbRadius,
                commenceFacing, finishFacing);
        }
        else
        {
            PrimeTrailCore(
                commence, finish, chamberIdent, 1,
                origin0, orbRadius,
                Vector3.Zero, 0f,
                commenceFacing, finishFacing);
        }
    }

    public void InitPath(
        Vector3 commence,
        Vector3 finish,
        uint chamberIdent,
        ImmutableArray<PackedContactSphere> orbs,
        float scaling = 1f,
        Quaternion? commenceFacing = null,
        Quaternion? finishFacing = null)
    {
        if (orbs.IsDefaultOrEmpty)
        {
            PrimeTrailCore(
                commence, finish, chamberIdent, 1,
                new Vector3(0, 0, KineticConstants.DummyOrbRadius), KineticConstants.DummyOrbRadius,
                Vector3.Zero, 0f,
                commenceFacing, finishFacing);
            return;
        }

        int tally = orbs.Length <= 2 ? orbs.Length : 2;
        var sphere = orbs[0];
        if (tally > 1)
        {
            var s1 = orbs[1];
            PrimeTrailCore(
                commence, finish, chamberIdent, 2,
                sphere.Origin * scaling, sphere.Radius * scaling,
                s1.Origin * scaling, s1.Radius * scaling,
                commenceFacing, finishFacing);
        }
        else
        {
            PrimeTrailCore(
                commence, finish, chamberIdent, 1,
                sphere.Origin * scaling, sphere.Radius * scaling,
                Vector3.Zero, 0f,
                commenceFacing, finishFacing);
        }
    }

    internal void AssignPassable(
        Plane plane,
        in TerrainTriVerts verts,
        Vector3 up)
    {
        Vector3[] passable = RentPrecise(3, ref _walkableVertexStorage);
        Vector3[] previous = RentPrecise(3, ref _lastWalkableVertexStorage);
        passable[0] = previous[0] = verts.V0;
        passable[1] = previous[1] = verts.V1;
        passable[2] = previous[2] = verts.V2;

        WalkableValid = true;
        WalkablePlane = plane;
        WalkableVertices = passable;
        WalkableUp = up;
        WalkableAllowance = KineticConstants.FloorZ;
        LastWalkableValid = true;
        LastWalkablePlane = plane;
        LastWalkableVertices = previous;
        LastWalkableUp = up;
    }

    internal void AssignPassableTransformed(
        Plane plane,
        ReadOnlySpan<Vector3> ownVerts,
        Quaternion ownToRealm,
        float scaling,
        Vector3 realmOrigin,
        Vector3 up)
    {
        Vector3[] passable = RentPrecise(ownVerts.Length, ref _walkableVertexStorage);
        Vector3[] previous = RentPrecise(ownVerts.Length, ref _lastWalkableVertexStorage);

        for (int idx = 0; idx < ownVerts.Length; ++idx)
        {
            Vector3 transformed =
                Vector3.Transform(ownVerts[idx] * scaling, ownToRealm) + realmOrigin;
            passable[idx] = transformed;
            previous[idx] = transformed;
        }

        WalkableValid = true;
        WalkablePlane = plane;
        WalkableVertices = passable;
        WalkableUp = up;
        WalkableAllowance = KineticConstants.FloorZ;

        LastWalkableValid = true;
        LastWalkablePlane = plane;
        LastWalkableVertices = previous;
        LastWalkableUp = up;
    }

    internal bool VerifyWalkables()
    {
        if (!HasPassablePolyg || WalkableVertices is null)
            return true;

        Orb footOrb = GlobalSphere[0];
        return CellBspProbe.VerifyPassableSupport(
            WalkablePlane,
            WalkableVertices,
            footOrb.Center,
            footOrb.Radius * 0.5f,
            WalkableUp);
    }

    internal void DuplicateFrom(SweepPath src)
    {
        ArgumentNullException.ThrowIfNull(src);
        NumSphere = src.NumSphere;
        DuplicateOrbArr(src.LocalSphere, LocalSphere);
        DuplicateOrbArr(src.GlobalSphere, GlobalSphere);
        DuplicateOrbArr(src.GlobalCurrCenter, GlobalCurrCenter);
        BeginPos = src.BeginPos;
        EndPos = src.EndPos;
        CurPos = src.CurPos;
        CheckPos = src.CheckPos;
        BeginOrientation = src.BeginOrientation;
        EndOrientation = src.EndOrientation;
        CurOrientation = src.CurOrientation;
        CheckOrientation = src.CheckOrientation;
        CurCellId = src.CurCellId;
        CheckCellId = src.CheckCellId;
        CarriedBlockOrigin = src.CarriedBlockOrigin;
        GlobalOffset = src.GlobalOffset;
        StepUp = src.StepUp;
        StepUpNormal = src.StepUpNormal;
        Collide = src.Collide;
        StepDown = src.StepDown;
        StepDownAmt = src.StepDownAmt;
        WalkInterp = src.WalkInterp;
        WalkableValid = src.WalkableValid;
        WalkablePlane = src.WalkablePlane;
        WalkableVertices = src.WalkableVertices is null
            ? null
            : DuplicatePrecise(
                src.WalkableVertices,
                ref _walkableVertexStorage);
        WalkableUp = src.WalkableUp;
        WalkableAllowance = src.WalkableAllowance;
        LastWalkableValid = src.LastWalkableValid;
        LastWalkablePlane = src.LastWalkablePlane;
        LastWalkableVertices = src.LastWalkableVertices is null
            ? null
            : DuplicatePrecise(
                src.LastWalkableVertices,
                ref _lastWalkableVertexStorage);
        LastWalkableUp = src.LastWalkableUp;
        BackupCheckPos = src.BackupCheckPos;
        BackupCheckCellId = src.BackupCheckCellId;
        NegPolyHit = src.NegPolyHit;
        NegStepUp = src.NegStepUp;
        NegCollisionNormal = src.NegCollisionNormal;
        CheckWalkable = src.CheckWalkable;
        InsertType = src.InsertType;
        PlacementAllowsSliding = src.PlacementAllowsSliding;
        ObstructionEthereal = src.ObstructionEthereal;
        BldgCheck = src.BldgCheck;
        HitsInteriorCell = src.HitsInteriorCell;
        CellCandidates.Clear();
        foreach (uint ident in src.CellCandidates)
            CellCandidates.Add(ident);
        SequencedChamberTemp.RestartForReuse();
    }

    private static Vector3[] DuplicatePrecise(
        ReadOnlySpan<Vector3> src,
        ref Vector3[]? depot)
    {
        Vector3[] dest = RentPrecise(src.Length, ref depot);
        src.CopyTo(dest);
        return dest;
    }

    private static Vector3[] RentPrecise(int len, ref Vector3[]? depot)
    {
        if (depot is null || depot.Length != len)
            depot = new Vector3[len];

        return depot;
    }

    private static void RestartOrbArr(Orb[] orbs)
    {
        for (int idx = 0; idx < orbs.Length; ++idx)
        {
            orbs[idx].Center = Vector3.Zero;
            orbs[idx].Radius = 0f;
        }
    }

    private static void DuplicateOrbArr(Orb[] src, Orb[] dest)
    {
        for (int idx = 0; idx < dest.Length; ++idx)
        {
            dest[idx].Center = src[idx].Center;
            dest[idx].Radius = src[idx].Radius;
        }
    }

    private void PrimeTrailCore(
        Vector3 commence,
        Vector3 finish,
        uint chamberIdent,
        int countOrb,
        Vector3 origin0,
        float radius0,
        Vector3 origin1,
        float radius1,
        Quaternion? commenceFacing,
        Quaternion? finishFacing)
    {
        BeginPos = commence;
        EndPos = finish;
        CurPos = commence;
        CurCellId = chamberIdent;

        BeginOrientation = commenceFacing ?? Quaternion.Identity;
        EndOrientation = finishFacing ?? BeginOrientation;
        CurOrientation = BeginOrientation;
        CheckOrientation = BeginOrientation;

        NumSphere = countOrb;
        LocalSphere[0].Center = origin0;
        LocalSphere[0].Radius = radius0;
        if (countOrb > 1)
        {
            LocalSphere[1].Center = origin1;
            LocalSphere[1].Radius = radius1;
        }

        AssignVerifySpot(commence, chamberIdent);

        for (int idx = 0; idx < NumSphere; ++idx)
        {
            GlobalCurrCenter[idx].Center =
                Vector3.Transform(LocalSphere[idx].Center, CurOrientation) + commence;
            GlobalCurrCenter[idx].Radius = LocalSphere[idx].Radius;
        }
    }
}

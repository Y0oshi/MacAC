using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class KineticBody
{
    public const float MaxVelocity = 50.0f;
    public const float UpperVelSquared = MaxVelocity * MaxVelocity;
    public const float Gravity = -9.8f;
    public const float SmallVel = 0.25f;
    public const float SmallVelSquared = SmallVel * SmallVel;
    public const float DefaultFriction = 0.95f;
    public const float LowerQuantum = 1.0f / 30.0f;   // ~0.0333 s
    public const float MaxQuantum = 0.2f;
    public const float HugeQuantum = 2.0f;            // discard stale dt

    private Vector3 _world;
    private Vector3[]? _passableDepot;

    /// <summary>World position; writes are mirrored into the cell frame.</summary>
    public Vector3 Position
    {
        get => _world;
        set
        {
            Vector3 diff = value - _world;
            _world = value;
            MoveChamberCycle(diff);
        }
    }

    public Locus CellPosition { get; private set; }

    public bool InWorld { get; set; }

    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    public void SnapToChamber(uint chamberIdent, Vector3 realmSpot, Vector3 chamberOwn)
    {
        JunctureDormantChamberCycle(chamberIdent, realmSpot, chamberOwn);
        InWorld = true;
    }

    public void JunctureDormantChamberCycle(uint chamberIdent, Vector3 realmSpot, Vector3 chamberOwn)
    {
        _world = realmSpot;
        uint chamber = chamberIdent;
        Vector3 own = chamberOwn;
        if (IsLandChamber(chamberIdent))
            MechLandDefs.TuneToBeyond(ref chamber, ref own);
        PutInChamber(chamber, own, Orientation);
    }

    public void AssignCycleInLatestChamber(Vector3 realmLocus, Quaternion facing)
    {
        Vector3 diff = realmLocus - _world;
        _world = realmLocus;
        Orientation = facing;

        if (CellPosition.ObjCellId is not 0)
            PutInChamber(CellPosition.ObjCellId, CellPosition.Frame.Origin + diff, facing);
    }

    public void SealChangeoverLocus(uint settledChamberIdent, Vector3 realmLocus)
    {
        Position = realmLocus;
        if (CellPosition.ObjCellId is 0 || settledChamberIdent is 0)
            return;

        uint chamber = settledChamberIdent;
        Vector3 own = CellPosition.Frame.Origin;
        if (IsLandChamber(chamber) && !MechLandDefs.TuneToBeyond(ref chamber, ref own))
            return;

        PutInChamber(chamber, own, CellPosition.Frame.Orientation);
    }

    // Copies the polygon into a retained buffer so the hot path does not allocate per contact
    internal void AssignPassableVertsPrecise(ReadOnlySpan<Vector3> src)
    {
        if (_passableDepot is null || _passableDepot.Length != src.Length)
            _passableDepot = new Vector3[src.Length];
        src.CopyTo(_passableDepot);
        WalkableVertices = _passableDepot;
    }

    // Outdoor land cells (low word 1..0x40) re-home as the body crosses cell edges
    private static bool IsLandChamber(uint chamberIdent) => (chamberIdent & 0xFFFFu) is >= 1u and <= 0x40u;

    private void PutInChamber(uint chamberIdent, Vector3 own, Quaternion facing) =>
        CellPosition = new Locus(chamberIdent, new CellPose(own, facing));

    public Vector3 Velocity { get; set; }

    public Vector3 StashedVel { get; set; }

    public int FramesStationaryFall { get; set; }

    public Vector3 Acceleration { get; set; }

    public Vector3 Omega { get; set; }

    public Vector3 GroundNormal { get; set; } = Vector3.UnitZ;

    public Vector3 SlidingNormal { get; set; }

    public float Elasticity { get; set; } = 0.05f;

    public float Friction { get; set; } = DefaultFriction;

    public double PreviousRefreshMoment { get; set; }

    public bool IsFullyConstrained { get; set; }

    public bool PreviousRelocateWasAutonomous { get; set; }

    public bool ContactPlaneValid { get; set; }

    public Plane ContactPlane { get; set; }

    public uint ContactPlaneCellId { get; set; }

    public bool ContactPlaneIsWater { get; set; }

    public bool WalkablePolygonValid { get; set; }

    public Plane WalkablePlane { get; set; }

    public Vector3[]? WalkableVertices { get; set; }

    public Vector3 WalkableUp { get; set; } = Vector3.UnitZ;

    private void MoveChamberCycle(Vector3 diff)
    {
        uint chamber = CellPosition.ObjCellId;
        if (chamber is 0)
            return;

        Vector3 own = CellPosition.Frame.Origin + diff;
        if (!IsLandChamber(chamber))
        {
            PutInChamber(chamber, own, CellPosition.Frame.Orientation);
            return;
        }

        uint adjusted = chamber;
        if (MechLandDefs.TuneToBeyond(ref adjusted, ref own))
            PutInChamber(adjusted, own, CellPosition.Frame.Orientation);
    }

    internal Vector3[]? KeptPassableVertDepot => _passableDepot;

    public KineticStateFlags State { get; set; } = KineticStateFlags.Gravity | KineticStateFlags.ReportCollisions;

    public TransientPhaseFlagSet TransientState { get; set; }

    public bool HasGravity => (State & KineticStateFlags.Gravity) != 0;
    public bool OnWalkable => Transient(TransientPhaseFlagSet.OnWalkable);
    public bool IsActive => Transient(TransientPhaseFlagSet.Active);
    public bool InContact => Transient(TransientPhaseFlagSet.Contact);
    public bool IsWaterLink => Transient(TransientPhaseFlagSet.WaterContact);

    private bool Transient(TransientPhaseFlagSet bit) => (TransientState & bit) != 0;
}

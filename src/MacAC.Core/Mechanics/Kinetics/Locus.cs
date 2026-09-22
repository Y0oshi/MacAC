using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>An origin and orientation expressed inside one cell.</summary>
public readonly record struct CellPose(Vector3 Origin, Quaternion Orientation);

/// <summary>A cell id plus the pose inside it: the client's notion of "where".</summary>
public readonly record struct Locus(uint ObjCellId, CellPose Frame)
{
    public Locus(uint objRefChamberIdent, Vector3 origin, Quaternion facing)
        : this(objRefChamberIdent, new CellPose(origin, facing)) { }
}

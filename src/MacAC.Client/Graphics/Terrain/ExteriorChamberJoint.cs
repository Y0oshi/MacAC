using System.Numerics;

namespace MacAC.Client.Graphics;

public static class ExteriorChamberJoint
{
    public static FetchedChamber Build(uint exteriorChamberIdent)
    {
        return new()
        {
            CellId = exteriorChamberIdent,
            SeenOutside = true,
            IsExteriorJoint = true,
            WorldTransform = Matrix4x4.Identity,
            InverseWorldTransform = Matrix4x4.Identity,
        };
    }
}

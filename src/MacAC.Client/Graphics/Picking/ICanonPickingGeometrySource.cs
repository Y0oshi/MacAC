using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Picking;

internal interface ICanonPickingGeometrySource
{
    CanonPickMesh? Resolve(uint gfxObjRefIdent);
}

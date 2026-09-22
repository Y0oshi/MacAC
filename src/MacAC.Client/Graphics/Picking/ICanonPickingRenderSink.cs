using System.Numerics;
using MacAC.Mechanics.Targeting;
namespace MacAC.Client.Graphics.Picking;

internal interface ICanonPickingRenderSink
{
    void AddVisiblePart(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 pieceRealm);
}

// Read-only mirror of the exact geometry and drawing-sphere acceptance used by the retained
// selection scene
internal interface ICanonPickingRenderOracle
{
    bool TryBuildShownPiece(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 pieceRealm,
        out CanonPickPart piece);
}

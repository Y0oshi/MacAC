using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Effects;

internal static class IndexedSetupPartPoseAssembler
{
    public static (Matrix4x4[] Poses, bool[] Available) Build(
        RigSpec rig,
        RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(actor);

        int pieceTally = rig.PartIds.Count;
        Matrix4x4[] postures = new Matrix4x4[pieceTally];
        var defaults = SetupPartPoses.Compute(
            rig,
            objectScaling: actor.Scale);
        for (int idx = 0; idx < pieceTally; ++idx)
            postures[idx] = idx < defaults.Count ? defaults[idx] : Matrix4x4.Identity;

        uint[] anticipatedGfx = new uint[pieceTally];
        for (int idx = 0; idx < pieceTally; ++idx)
            anticipatedGfx[idx] = (uint)rig.PartIds[idx];
        for (int idx = 0; idx < actor.PieceSubstitutions.Count; ++idx)
        {
            PartSwap substitute = actor.PieceSubstitutions[idx];
            if (substitute.PartIndex < anticipatedGfx.Length)
                anticipatedGfx[substitute.PartIndex] = substitute.GfxObjId;
        }

        bool[] onHand = new bool[pieceTally];
        bool[] consumed = new bool[actor.MeshRefs.Count];
        for (int pieceOrdinal = 0; pieceOrdinal < pieceTally; ++pieceOrdinal)
        {
            for (int drawableOrdinal = 0; drawableOrdinal < actor.MeshRefs.Count; ++drawableOrdinal)
            {
                if (consumed[drawableOrdinal]
                    || actor.MeshRefs[drawableOrdinal].GfxObjId != anticipatedGfx[pieceOrdinal])

                    continue;

                consumed[drawableOrdinal] = true;
                onHand[pieceOrdinal] = true;
                break;
            }
        }

        return (postures, onHand);
    }
}

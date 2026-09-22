using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Realm;

namespace MacAC.Mechanics.Geometry;

public readonly record struct WornChildPose(
    Matrix4x4 RootLocal,
    Matrix4x4[] PartLocal,
    TriMeshRef[] AttachedParts);

public static class WornChildMount
{
    public static bool TryCompose(
        RigSpec ancestorRig,
        IReadOnlyList<TriMeshRef> latestAncestorPosture,
        RigSpec descendantRig,
        AttachSlot ancestorLocale,
        PlacementId stance,
        IReadOnlyList<TriMeshRef> descendantPieceBlueprint,
        float descendantScaling,
        out IReadOnlyList<TriMeshRef> affixedPieces)
    {
        bool ok = TryComposePose(
            ancestorRig, latestAncestorPosture, descendantRig, ancestorLocale, stance,
            descendantPieceBlueprint, descendantScaling, out WornChildPose posture);
        affixedPieces = ok ? posture.AttachedParts : [];
        return ok;
    }

    public static bool TryComposePose(
        RigSpec ancestorRig,
        IReadOnlyList<TriMeshRef> latestAncestorPosture,
        RigSpec descendantRig,
        AttachSlot ancestorLocale,
        PlacementId stance,
        IReadOnlyList<TriMeshRef> descendantPieceBlueprint,
        float descendantScaling,
        out WornChildPose posture)
    {
        ArgumentNullException.ThrowIfNull(latestAncestorPosture);
        Matrix4x4[] ancestorPieces = new Matrix4x4[latestAncestorPosture.Count];
        for (int idx = 0; idx < ancestorPieces.Length; ++idx)
            ancestorPieces[idx] = latestAncestorPosture[idx].PartTransform;
        return TryComposePose(
            ancestorRig, ancestorPieces, descendantRig, ancestorLocale, stance,
            descendantPieceBlueprint, descendantScaling, out posture);
    }

    public static bool TryComposePose(
        RigSpec ancestorRig,
        IReadOnlyList<Matrix4x4> latestAncestorPosture,
        RigSpec descendantRig,
        AttachSlot ancestorLocale,
        PlacementId stance,
        IReadOnlyList<TriMeshRef> descendantPieceBlueprint,
        float descendantScaling,
        out WornChildPose posture)
    {
        return TryConstructPostureInto(
            ancestorRig, latestAncestorPosture, ancestorPieceReadiness: null, descendantRig,
            ancestorLocale, stance, descendantPieceBlueprint, descendantScaling,
            piecePostureBuf: null, affixedPieceBuf: null, out posture);
    }

    public static bool TryConstructPostureInto(
        RigSpec ancestorRig,
        IReadOnlyList<Matrix4x4> latestAncestorPosture,
        IReadOnlyList<bool>? ancestorPieceReadiness,
        RigSpec descendantRig,
        AttachSlot ancestorLocale,
        PlacementId stance,
        IReadOnlyList<TriMeshRef> descendantPieceBlueprint,
        float descendantScaling,
        Matrix4x4[]? piecePostureBuf,
        TriMeshRef[]? affixedPieceBuf,
        out WornChildPose posture)
    {
        ArgumentNullException.ThrowIfNull(ancestorRig);
        ArgumentNullException.ThrowIfNull(latestAncestorPosture);
        ArgumentNullException.ThrowIfNull(descendantRig);
        ArgumentNullException.ThrowIfNull(descendantPieceBlueprint);

        if (!ancestorRig.HoldingSlots.TryGetValue(ancestorLocale, out AttachPoint? holding))
        {
            posture = new WornChildPose(Matrix4x4.Identity, [], []);
            return false;
        }

        Matrix4x4 mooring = Matrix4x4.Identity;
        if (holding.PartIndex >= 0 && holding.PartIndex < latestAncestorPosture.Count)
        {
            int piece = (int)holding.PartIndex;
            if (ancestorPieceReadiness is not null
                && (piece >= ancestorPieceReadiness.Count || !ancestorPieceReadiness[piece]))
            {
                posture = default;
                return false;
            }
            mooring = latestAncestorPosture[piece];
        }
        Matrix4x4 descendantTrunk = Rigid(holding.Pose) * mooring;

        if (!descendantRig.Placements.TryGetValue(stance, out MotionFrame? stanceCycle))
            descendantRig.Placements.TryGetValue(PlacementId.Default, out stanceCycle);

        int pieceTally = Math.Min(descendantRig.PartIds.Count, descendantPieceBlueprint.Count);
        TriMeshRef[] affixed = affixedPieceBuf?.Length == pieceTally ? affixedPieceBuf : new TriMeshRef[pieceTally];
        Matrix4x4[] postures = piecePostureBuf?.Length == pieceTally ? piecePostureBuf : new Matrix4x4[pieceTally];

        for (int idx = 0; idx < pieceTally; ++idx)
        {
            Pose cycle = stanceCycle is not null && idx < stanceCycle.Poses.Count
                ? stanceCycle.Poses[idx]
                : new Pose { Orientation = Quaternion.Identity };
            Vector3 defaultScaling = idx < descendantRig.DefaultScale.Count ? descendantRig.DefaultScale[idx] : Vector3.One;

            Matrix4x4 visual = Matrix4x4.CreateScale(defaultScaling) * Rigid(cycle);
            if (descendantScaling != 1.0f)
                visual *= Matrix4x4.CreateScale(descendantScaling);

            postures[idx] = Matrix4x4.CreateFromQuaternion(cycle.Orientation)
                * Matrix4x4.CreateTranslation(cycle.Origin * descendantScaling);

            TriMeshRef blueprint = descendantPieceBlueprint[idx];
            affixed[idx] = new TriMeshRef(blueprint.GfxObjId, visual * descendantTrunk)
            {
                CanvasOverrides = blueprint.CanvasOverrides,
            };
        }

        posture = new WornChildPose(descendantTrunk, postures, affixed);
        return true;
    }

    private static Matrix4x4 Rigid(Pose cycle)
    {
        return Matrix4x4.CreateFromQuaternion(cycle.Orientation) * Matrix4x4.CreateTranslation(cycle.Origin);
    }
}

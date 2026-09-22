using System.Numerics;
using MacAC.Mechanics.Realm;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public static class ProxyShapeBuilder
{
    private const float DefaultBspRadius = 2f;

    public static IReadOnlyList<ProxyShape> FromSetup(
        RigSpec rig,
        float entScaling,
        Func<uint, bool> hasKineticsBsp,
        IReadOnlyList<Pose>? piecePostureOverride = null,
        IReadOnlyList<uint>? netPieceGfxObjRefIdents = null,
        Func<uint, ProxyPartGeometry?>? kineticsBspLimits = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(hasKineticsBsp);

        List<ProxyShape> forms = new List<ProxyShape>();

        bool anyBspPiece = false;
        for (int idx = 0; idx < rig.PartIds.Count && !anyBspPiece; ++idx)
            anyBspPiece = hasKineticsBsp(NetPieceGfxObjRefIdent(rig, netPieceGfxObjRefIdents, idx));

        // Cylinders and spheres only count for an object with no physics-BSP part.
        if (!anyBspPiece)
        {
            foreach (Capsule cyl in rig.Capsules)
            {
                if (cyl.Radius <= 0f)
                    continue;
                float height = cyl.Height > 0f ? cyl.Height : cyl.Radius * 4f;
                forms.Add(ProxyShape.Cylinder(
                    gfxObjRefIdent: 0u,
                    ownLocus: Scaled(cyl.Center, entScaling),
                    ownSpin: Quaternion.Identity,
                    scaling: entScaling,
                    radius: cyl.Radius * entScaling,
                    cylHeight: height * entScaling));
            }

            if (rig.Capsules.Count is 0)
            {
                foreach (Orb orb in rig.Orbs)
                {
                    if (orb.Radius <= 0f)
                        continue;
                    forms.Add(ProxyShape.Sphere(
                        gfxObjRefIdent: 0u,
                        ownLocus: Scaled(orb.Center, entScaling),
                        ownSpin: Quaternion.Identity,
                        scaling: entScaling,
                        radius: orb.Radius * entScaling));
                }
            }
        }

        var stance = LocateStanceCycle(rig);
        for (int idx = 0; idx < rig.PartIds.Count; ++idx)
        {
            uint gfxIdent = NetPieceGfxObjRefIdent(rig, netPieceGfxObjRefIdents, idx);
            if (!hasKineticsBsp(gfxIdent))
                continue;

            Pose posture = PiecePosture(idx, piecePostureOverride, stance);
            ProxyPartGeometry geo = kineticsBspLimits?.Invoke(gfxIdent)
                ?? ProxyPartGeometry.Create(new PackedContactSphere(Vector3.Zero, DefaultBspRadius), null);

            forms.Add(ProxyShape.Bsp(
                gfxObjRefIdent: gfxIdent,
                ownLocus: Scaled(posture.Origin, entScaling),
                ownSpin: posture.Orientation,
                scaling: entScaling,
                ownGeo: geo));
        }

        return forms;
    }

    public static List<ProxyShape> FromLbBspPieces(
        IReadOnlyList<TriMeshRef> triMeshRefs,
        bool isStructureShell,
        Func<uint, GfxObjKinetics?> fetchGfxObjRef)
    {
        ArgumentNullException.ThrowIfNull(fetchGfxObjRef);

        List<ProxyShape> forms = new List<ProxyShape>();
        if (isStructureShell || triMeshRefs is null)
            return forms;

        foreach (TriMeshRef triMeshRef in triMeshRefs)
        {
            var kinetics = fetchGfxObjRef(triMeshRef.GfxObjId);
            if (kinetics is null || !HasBsp(kinetics))
                continue; // graph-only fixture seam until I6 referee removal

            (Vector3 locus, Quaternion spin, float scaling) = Decompose(triMeshRef.PartTransform);
            var orb = BspOrb(kinetics, Vector3.Zero, 1f);
            forms.Add(ProxyShape.Bsp(
                gfxObjRefIdent: triMeshRef.GfxObjId,
                ownLocus: locus,
                ownSpin: spin,
                scaling: scaling,
                ownGeo: ProxyPartGeometry.Create(orb, kinetics.VisualLimits)));
        }

        return forms;
    }

    public static List<ProxyShape> FromStaticRasterizePieces(
        IReadOnlyList<TriMeshRef> triMeshRefs,
        Func<uint, GfxObjKinetics?> fetchGfxObjRef,
        Func<uint, GfxObjVisualExtent?> fetchVisualLimits,
        out bool hasKineticsBsp)
    {
        ArgumentNullException.ThrowIfNull(triMeshRefs);
        ArgumentNullException.ThrowIfNull(fetchGfxObjRef);
        ArgumentNullException.ThrowIfNull(fetchVisualLimits);

        hasKineticsBsp = false;
        List<ProxyShape> forms = new List<ProxyShape>(triMeshRefs.Count);
        foreach (TriMeshRef triMeshRef in triMeshRefs)
        {
            var kinetics = fetchGfxObjRef(triMeshRef.GfxObjId);
            bool bsp = HasBsp(kinetics);
            hasKineticsBsp |= bsp;

            if (ReachFor(kinetics, triMeshRef.GfxObjId, fetchVisualLimits) is not { } limits)
                continue;

            (Vector3 locus, Quaternion spin, float scaling) = Decompose(triMeshRef.PartTransform);
            forms.Add(ProxyShape.Bsp(
                triMeshRef.GfxObjId,
                locus,
                spin,
                scaling,
                ProxyPartGeometry.Create(PaintPieceOrb(kinetics, bsp, limits), limits)));
        }

        return forms;
    }

    public static List<ProxyShape> FromRigRasterizePieces(
        RigSpec rig,
        float entScaling,
        IReadOnlyList<uint>? netPieceGfxObjRefIdents,
        IReadOnlyList<Pose>? piecePostureOverride,
        Func<uint, GfxObjKinetics?> fetchGfxObjRef,
        Func<uint, GfxObjVisualExtent?> fetchVisualLimits)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(fetchGfxObjRef);
        ArgumentNullException.ThrowIfNull(fetchVisualLimits);

        List<ProxyShape> forms = new List<ProxyShape>(rig.PartIds.Count);
        var stance = LocateStanceCycle(rig);
        for (int idx = 0; idx < rig.PartIds.Count; ++idx)
        {
            uint gfxIdent = NetPieceGfxObjRefIdent(rig, netPieceGfxObjRefIdents, idx);
            var kinetics = fetchGfxObjRef(gfxIdent);
            bool bsp = HasBsp(kinetics);

            if (ReachFor(kinetics, gfxIdent, fetchVisualLimits) is not { } limits)
                continue;

            Pose posture = PiecePosture(idx, piecePostureOverride, stance);
            forms.Add(ProxyShape.Bsp(
                gfxIdent,
                Scaled(posture.Origin, entScaling),
                posture.Orientation,
                entScaling,
                ProxyPartGeometry.Create(PaintPieceOrb(kinetics, bsp, limits), limits)));
        }

        return forms;
    }

    // The frame a part rests in: an override, else the setup's placement frame, else identity
    private static Pose PiecePosture(int ordinal, IReadOnlyList<Pose>? substitutions, MotionFrame? stance)
    {
        if (substitutions is not null && ordinal < substitutions.Count)
            return substitutions[ordinal];
        if (stance is not null && ordinal < stance.Poses.Count)
            return stance.Poses[ordinal];
        return new Pose { Origin = Vector3.Zero, Orientation = Quaternion.Identity };
    }

    private static Vector3 Scaled(Vector3 v, float scaling) => new Vector3(v.X, v.Y, v.Z) * scaling;

    private static bool HasBsp(GfxObjKinetics? kinetics)
    {
        return kinetics?.DenseKineticBsp is { TrunkOrdinal: >= 0 } || kinetics?.BSP?.Root is not null;
    }

    // Visual extent from the packed asset, else from the live extent lookup
    private static PackedGfxObjVisualExtent? ReachFor(GfxObjKinetics? kinetics, uint gfxIdent, Func<uint, GfxObjVisualExtent?> fetchVisualLimits)
    {
        if (kinetics?.VisualLimits is { } dense)
            return dense;
        return fetchVisualLimits(gfxIdent) is { } online
            ? new PackedGfxObjVisualExtent(online.Min, online.Max, online.Center, online.Radius, online.HalfExtents)
            : null;
    }

    // The bounding sphere a BSP part reports: its packed root, else its graph sphere with fallbacks
    private static PackedContactSphere BspOrb(GfxObjKinetics kinetics, Vector3 backupOrigin, float backupRadius)
    {
        var dense = kinetics.DenseKineticBsp;
        if (dense is { TrunkOrdinal: >= 0 })
            return dense.Joints[dense.TrunkOrdinal].BoundingSphere;
        return new PackedContactSphere(
            kinetics.BoundingSphere?.Center ?? backupOrigin,
            kinetics.BoundingSphere?.Radius ?? backupRadius);
    }

    private static PackedContactSphere PaintPieceOrb(GfxObjKinetics? kinetics, bool hasBsp, in PackedGfxObjVisualExtent limits)
    {
        return hasBsp ? BspOrb(kinetics!, limits.Center, limits.Radius) : new PackedContactSphere(limits.Center, limits.Radius);
    }

    private static (Vector3 Position, Quaternion Rotation, float Scale) Decompose(in Matrix4x4 xform)
    {
        if (!Matrix4x4.Decompose(xform, out Vector3 scaling, out Quaternion spin, out Vector3 locus))
            return (xform.Translation, Quaternion.Identity, 1f);
        return (locus, spin, scaling.X > 0f ? scaling.X : 1f);   // AC objects are uniformly scaled
    }

    private static uint NetPieceGfxObjRefIdent(RigSpec rig, IReadOnlyList<uint>? netPieceGfxObjRefIdents, int ordinal)
    {
        return netPieceGfxObjRefIdents is not null && ordinal < netPieceGfxObjRefIdents.Count
            ? netPieceGfxObjRefIdents[ordinal]
            : (uint)rig.PartIds[ordinal];
    }

    // Resting, else Default, else whichever placement the setup lists first
    private static MotionFrame? LocateStanceCycle(RigSpec rig)
    {
        if (rig.Placements.TryGetValue(PlacementId.Resting, out MotionFrame? resting))
            return resting;
        if (rig.Placements.TryGetValue(PlacementId.Default, out MotionFrame? backup))
            return backup;
        foreach (MotionFrame cycle in rig.Placements.Values)
            return cycle;
        return null;
    }
}

using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal static class RenderMirrorRecordMint
{
    private const float DefaultAabbRadius = 5.0f;

    public static RenderMirrorRecord ProjectActor(
        RenderMirrorId ident,
        RenderMirrorClass projClass,
        RasterizeHolderIncarnation incarnation,
        uint holderLbIdent,
        uint wholeChamberIdent,
        RealmActor actor,
        bool spatiallyShown,
        RasterizeInvokerPersonaFlavor invokerPersona =
            RasterizeInvokerPersonaFlavor.Unclassified)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var fingerprint =
            CurrentRenderStageOracle.BuildProjFingerprint(
                holderLbIdent,
                actor);
        var flagSet = RenderMirrorFlags.Selectable;
        if (spatiallyShown)
            flagSet |= RenderMirrorFlags.SpatiallyResident;
        if (actor.MeshRefs.Count > 0
            && spatiallyShown
            && actor.IsPaintShown
            && actor.IsAncestorPaintShown)

            flagSet |= RenderMirrorFlags.Draw;
        if (!actor.IsPaintShown)
            flagSet |= RenderMirrorFlags.Hidden;
        if (!actor.IsAncestorPaintShown)
            flagSet |= RenderMirrorFlags.AncestorHidden;

        RasterizeTransform xform = RasterizeTransform.FromTrunk(
            actor.Position,
            actor.Rotation,
            actor.Scale);
        (Vector3 floor, Vector3 ceiling) = DeriveLimits(actor);
        return new RenderMirrorRecord(
            ident,
            projClass,
            incarnation,
            xform,
            new EarlierRasterizeTransform(xform.LocalToWorld),
            new RasterizeTriMeshGroup(
                RasterizeAssetHnd.FromRaw(
                    fingerprint.Geometry.Low ^ fingerprint.Geometry.High),
                actor.MeshRefs.Count,
                0),
            new RasterizeMatlVariant(
                fingerprint.Appearance.Low,
                fingerprint.Appearance.High,
                1.0f),
            new RenderSpatialTenancy(
                RasterizeSpatialBin.FromRaw(wholeChamberIdent),
                holderLbIdent,
                wholeChamberIdent),
            new RenderRealmBounds(floor, ceiling),
            flagSet,
            default,
            ident.ToOrderTag(),
            RasterizeStaleBitmask.All,
            new RasterizeOriginMetadata(
                actor.Id,
                actor.ServerGuid,
                actor.SrcGfxObjRefOrRigIdent,
                actor.ParentCellId ?? 0,
                actor.FxChamberIdent ?? 0,
                actor.StructureShellMooringChamberIdent ?? 0,
                fingerprint.Transform,
                fingerprint.Geometry,
                fingerprint.Appearance,
                fingerprint.Flags,
                CurrentRenderStageOracle
                    .BuildDirectedShadeWiringFingerprint(actor)),
            new RenderActorPayload(
                actor.MeshRefs,
                actor.SwatchOverride,
                actor.IsStructureShell,
                invokerPersona));
    }

    private static (Vector3 Minimum, Vector3 Maximum) DeriveLimits(
        RealmActor actor)
    {
        Vector3 locus = actor.Position;
        if (actor.HasOwnLimits)
        {
            Vector3 ownLower = actor.OwnTiedLower;
            Vector3 ownUpper = actor.OwnTiedUpper;
            Vector3 floor = default;
            Vector3 ceiling = default;
            for (int cornerOrdinal = 0; cornerOrdinal < 8; ++cornerOrdinal)
            {
                Vector3 corner = new(
                    (cornerOrdinal & 1) is 0 ? ownLower.X : ownUpper.X,
                    (cornerOrdinal & 2) is 0 ? ownLower.Y : ownUpper.Y,
                    (cornerOrdinal & 4) is 0 ? ownLower.Z : ownUpper.Z);
                Vector3 transformed =
                    Vector3.Transform(corner, actor.Rotation);
                if (cornerOrdinal is 0)
                {
                    floor = transformed;
                    ceiling = transformed;
                }
                else
                {
                    floor = Vector3.Min(floor, transformed);
                    ceiling = Vector3.Max(ceiling, transformed);
                }
            }

            Vector3 margin = new(DefaultAabbRadius);
            return (
                locus + floor - margin,
                locus + ceiling + margin);
        }

        float radius = DefaultAabbRadius;
        for (int idx = 0; idx < actor.MeshRefs.Count; ++idx)
        {
            radius = MathF.Max(
                radius,
                DefaultAabbRadius
                + actor.MeshRefs[idx].PartTransform.Translation.Length());
        }

        Vector3 reach = new(radius);
        return (locus - reach, locus + reach);
    }
}

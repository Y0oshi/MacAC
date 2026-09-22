using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal readonly record struct RasterizeInstContender(
    RenderMirrorId ProjectionId,
    uint LocalEntityId,
    uint ServerGuid,
    uint SourceId,
    uint ParentCellId,
    Matrix4x4 RootWorld,
    Vector3 Position,
    Quaternion Rotation,
    float Scale,
    RenderRealmBounds Bounds,
    SwatchOverride? PaletteOverride,
    bool IsBuildingShell,
    bool Animated,
    int MeshPartCount,
    uint TupleLandblockId)
{
    internal uint Id => LocalEntityId;

    internal uint? AncestorChamber =>
        ParentCellId is 0 ? null : ParentCellId;

    internal uint StashLbIdent
    {
        get
        {
            return ParentCellId is not 0
            ? (ParentCellId & 0xFFFF0000u) | 0xFFFFu
            : TupleLandblockId;
        }
    }

    internal static RasterizeInstContender FromRealmActor(
        RealmActor actor,
        bool moving,
        int meshPartCount,
        uint tupleLbIdent)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (meshPartCount < 0)
            throw new ArgumentOutOfRangeException(nameof(meshPartCount));
        if (actor.AabbStale)
            actor.RenewAabb();

        return new RasterizeInstContender(
            ProjectionId: default,
            LocalEntityId: actor.Id,
            ServerGuid: actor.ServerGuid,
            SourceId: actor.SrcGfxObjRefOrRigIdent,
            ParentCellId: actor.ParentCellId ?? 0u,
            RootWorld:
                Matrix4x4.CreateFromQuaternion(actor.Rotation)
                * Matrix4x4.CreateTranslation(actor.Position),
            Position: actor.Position,
            Rotation: actor.Rotation,
            Scale: actor.Scale,
            Bounds: new RenderRealmBounds(
                actor.AabbMin,
                actor.AabbMax),
            PaletteOverride: actor.SwatchOverride,
            IsBuildingShell: actor.IsStructureShell,
            Animated: moving,
            MeshPartCount: meshPartCount,
            TupleLandblockId: tupleLbIdent);
    }

    internal static RasterizeInstContender FromProj(
        in RenderMirrorRecord proj,
        uint tupleLbIdent,
        bool moving = false)
    {
        var cargo = proj.EntityPayload;
        return new RasterizeInstContender(
            ProjectionId: proj.Id,
            LocalEntityId: proj.Source.LocalEntityId,
            ServerGuid: proj.Source.ServerGuid,
            SourceId: proj.Source.SourceId,
            ParentCellId: proj.Source.ParentCellId,
            RootWorld: proj.Transform.LocalToWorld,
            Position: proj.Transform.Position,
            Rotation: proj.Transform.Rotation,
            Scale: proj.Transform.UniformScale,
            Bounds: proj.Bounds,
            PaletteOverride: cargo.PaletteOverride,
            IsBuildingShell: cargo.IsBuildingShell,
            Animated: moving,
            MeshPartCount: cargo.MeshRefs?.Count ?? 0,
            TupleLandblockId: tupleLbIdent);
    }

    internal static RasterizeInstContender FromCycle(
        in RenderFrameActorCandidate src,
        uint tupleLbIdent)
    {
        var proj = src.Projection;
        return new RasterizeInstContender(
            ProjectionId: proj.Id,
            LocalEntityId: proj.Source.LocalEntityId,
            ServerGuid: proj.Source.ServerGuid,
            SourceId: proj.Source.SourceId,
            ParentCellId: proj.Source.ParentCellId,
            RootWorld: proj.Transform.LocalToWorld,
            Position: proj.Transform.Position,
            Rotation: proj.Transform.Rotation,
            Scale: proj.Transform.UniformScale,
            Bounds: proj.Bounds,
            PaletteOverride:
                proj.EntityPayload.PaletteOverride,
            IsBuildingShell:
                proj.EntityPayload.IsBuildingShell,
            Animated: src.Animated,
            MeshPartCount: src.MeshPartCount,
            TupleLandblockId: tupleLbIdent);
    }
}

internal readonly record struct RasterizeInstTuple(
    RasterizeInstContender Candidate,
    int MeshRefIndex,
    TriMeshRef MeshRef);

using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal readonly record struct ChargenPreviewDrawablePiece(
    int SetupPartIndex,
    uint GfxObjId,
    Vector3 DefaultScale,
    IReadOnlyDictionary<uint, uint>? SurfaceOverrides);

internal sealed class ChargenPreviewMovingAssemble
{
    public required RealmActor Entity { get; init; }
    public required IReadOnlyList<ChargenPreviewDrawablePiece> DrawablePieces { get; init; }

    public required IReadOnlyList<TriMeshRef> RestTriMeshRefs { get; init; }

    public MotionClip? IdleAnim { get; init; }
    public int IdleLoCycle { get; init; }
    public int IdleHiCycle { get; init; }
}

internal static class ChargenPreviewActorAssembler
{
    public const uint PreviewSrvOid = 0xDA11_D031u;

    public const uint PreviewRasterizeIdent = 0xDA11_D032u;

    public const uint PreviewBackdropSrvOid = 0xDA11_D033u;

    public const uint PreviewBackdropRasterizeIdent = 0xDA11_D034u;

    public const uint SummaryPreviewRasterizeIdent = 0xDA11_D035u;

    public const uint SummaryPreviewBackdropRasterizeIdent = 0xDA11_D036u;

    public static RealmActor? TryAssemble(
        IDatAccess datFiles,
        IAnimReader anims,
        GenesisLook looks,
        uint lineageIdent,
        Quaternion bearing,
        object datMutex,
        uint rasterizeIdent = PreviewRasterizeIdent)
    {
        var assemble = TryAssembleMoving(
            datFiles, anims, looks, lineageIdent, bearing, datMutex, rasterizeIdent);
        if (assemble is null)
            return null;

        assemble.Entity.MeshRefs = assemble.RestTriMeshRefs;
        return assemble.Entity;
    }

    public static ChargenPreviewMovingAssemble? TryAssembleMoving(
        IDatAccess datFiles,
        IAnimReader anims,
        GenesisLook looks,
        uint lineageIdent,
        Quaternion bearing,
        object datMutex,
        uint rasterizeIdent = PreviewRasterizeIdent)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(anims);
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(datMutex);

        uint rigIdent = looks.SetupId;
        List<ChargenPreviewDrawablePiece> drawablePieces;
        List<TriMeshRef> restTriMeshRefs;
        MotionClip? idleAnim;
        int idleLoCycle = 0, idleHiCycle = -1;

        lock (datMutex)
        {
            RigSpec? rig = datFiles.Get<RigSpec>(rigIdent);
            if (rig is null)
                return null;

            List<TriMeshRef> flattened = new List<TriMeshRef>(RigTriMesh.Flatten(rig));

            foreach (GenesisAnimPartSwap edit in looks.ObjDesc.AnimPartChanges)
            {
                if (edit.PartIndex < flattened.Count)
                    flattened[edit.PartIndex] = new TriMeshRef(edit.PartId, flattened[edit.PartIndex].PartTransform);
            }

            ImposePinnedPostureXforms(datFiles, anims, rig, LocateRestPostureEnum(lineageIdent), flattened);

            var canvasSubstitutions =
                LocateCanvasSubstitutions(datFiles, flattened, looks.ObjDesc.TextureChanges);

            drawablePieces = new List<ChargenPreviewDrawablePiece>(flattened.Count);
            restTriMeshRefs = new List<TriMeshRef>(flattened.Count);
            for (int pieceOrdinal = 0; pieceOrdinal < flattened.Count; ++pieceOrdinal)
            {
                TriMeshRef piece = flattened[pieceOrdinal];
                if (datFiles.Get<PartMesh>(piece.GfxObjId) is null)
                    continue; // matches DatOnlineActorMirrorAssembler's drawable filter

                IReadOnlyDictionary<uint, uint>? substitutions = null;
                if (canvasSubstitutions is not null && canvasSubstitutions.TryGetValue(pieceOrdinal, out var perPiece))
                    substitutions = perPiece;

                restTriMeshRefs.Add(new TriMeshRef(piece.GfxObjId, piece.PartTransform) { CanvasOverrides = substitutions });

                Vector3 defaultScaling = pieceOrdinal < rig.DefaultScale.Count
                    ? rig.DefaultScale[pieceOrdinal]
                    : Vector3.One;
                drawablePieces.Add(new ChargenPreviewDrawablePiece(pieceOrdinal, piece.GfxObjId, defaultScaling, substitutions));
            }
            if (drawablePieces.Count is 0)
                return null;

            // Idle DID: independent lookup, no mutation of flattened
            uint idleDid = CanonHeldPose.LocatePostureDid(datFiles, LocateIdleAnimEnum(lineageIdent));
            idleAnim = (idleDid >> 24) == 0x03u ? anims.PullAnim(idleDid) : null;
            if (idleAnim is not null && idleAnim.Frames.Count > 0)
            {
                idleLoCycle = 0;
                idleHiCycle = idleAnim.Frames.Count - 1;
            }
            else
            {
                idleAnim = null;
            }
        }

        RealmActor actor = new RealmActor
        {
            Id = rasterizeIdent,
            ServerGuid = PreviewSrvOid,
            SrcGfxObjRefOrRigIdent = rigIdent,
            Position = Vector3.Zero,
            Rotation = bearing,
            MeshRefs = restTriMeshRefs,
            SwatchOverride = AssembleSwatchOverride(looks),
            PieceSubstitutions = AssemblePieceSubstitutions(looks),
            ParentCellId = null,
        };

        return new ChargenPreviewMovingAssemble
        {
            Entity = actor,
            DrawablePieces = drawablePieces,
            RestTriMeshRefs = restTriMeshRefs,
            IdleAnim = idleAnim,
            IdleLoCycle = idleLoCycle,
            IdleHiCycle = idleHiCycle,
        };
    }

    public static RealmActor? TryAssembleBackdrop(
        IDatAccess datFiles,
        uint surroundingsRigIdent,
        object datMutex,
        uint rasterizeIdent = PreviewBackdropRasterizeIdent)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(datMutex);

        if (surroundingsRigIdent is 0u)
            return null;

        lock (datMutex)
        {
            RigSpec? rig = datFiles.Get<RigSpec>(surroundingsRigIdent);
            if (rig is null)
                return null;

            var flattened = RigTriMesh.Flatten(rig);
            List<TriMeshRef> drawable = new List<TriMeshRef>(flattened.Count);
            foreach (TriMeshRef piece in flattened)
            {
                if (datFiles.Get<PartMesh>(piece.GfxObjId) is not null)
                    drawable.Add(piece);
            }
            return drawable.Count is 0
                ? null
                : new RealmActor
                {
                    Id = rasterizeIdent,
                    ServerGuid = PreviewBackdropSrvOid,
                    SrcGfxObjRefOrRigIdent = surroundingsRigIdent,
                    Position = Vector3.Zero,
                    Rotation = Quaternion.Identity,
                    MeshRefs = drawable,
                    ParentCellId = null,
                };
        }
    }

    private static uint LocateRestPostureEnum(uint lineageIdent)
    {
        return lineageIdent switch
        {
            (uint)GenesisHeritage.Olthoi => 0x10000011u,
            (uint)GenesisHeritage.OlthoiAcid => 0x10000013u,
            _ => 0x10000005u,
        };
    }

    private static uint LocateIdleAnimEnum(uint lineageIdent)
    {
        return lineageIdent switch
        {
            (uint)GenesisHeritage.Olthoi => 0x10000011u,
            (uint)GenesisHeritage.OlthoiAcid => 0x10000013u,
            _ => 0x10000006u,
        };
    }

    private static SwatchOverride? AssembleSwatchOverride(GenesisLook looks)
    {
        if (looks.ObjDesc.SubPalettes.Count is 0)
            return null;

        var spans = new SwatchOverride.SubPaletteSpan[looks.ObjDesc.SubPalettes.Count];
        for (int idx = 0; idx < looks.ObjDesc.SubPalettes.Count; ++idx)
        {
            var sub = looks.ObjDesc.SubPalettes[idx];
            spans[idx] = new SwatchOverride.SubPaletteSpan(sub.SubPaletteId, sub.Offset, sub.NumColors);
        }
        return new SwatchOverride(looks.BasePaletteId, spans);
    }

    private static PartSwap[] AssemblePieceSubstitutions(GenesisLook looks)
    {
        PartSwap[] pieceSubstitutions = new PartSwap[looks.ObjDesc.AnimPartChanges.Count];
        for (int idx = 0; idx < looks.ObjDesc.AnimPartChanges.Count; ++idx)
        {
            var edit = looks.ObjDesc.AnimPartChanges[idx];
            pieceSubstitutions[idx] = new PartSwap(edit.PartIndex, edit.PartId);
        }
        return pieceSubstitutions;
    }

    private static void ImposePinnedPostureXforms(
        IDatAccess datFiles,
        IAnimReader anims,
        RigSpec rig,
        uint postureEnum,
        List<TriMeshRef> flattened)
    {
        uint postureDid = CanonHeldPose.LocatePostureDid(datFiles, postureEnum);
        if ((postureDid >> 24) != 0x03u)
            return;

        MotionClip? anim = anims.PullAnim(postureDid);
        if (anim is null || anim.Frames.Count is 0)
            return;

        MotionFrame cycle = anim.Frames[^1];
        for (int ordinal = 0; ordinal < flattened.Count; ++ordinal)
        {
            ImposePinnedPostureXformsLoop(ordinal, rig, cycle, flattened);
        }
    }

    private static void ImposePinnedPostureXformsLoop(int ordinal, RigSpec rig, MotionFrame cycle, List<TriMeshRef> flattened)
    {
        Vector3 scaling = ordinal < rig.DefaultScale.Count ? rig.DefaultScale[ordinal] : Vector3.One;
        Vector3 origin = Vector3.Zero;
        Quaternion facing = Quaternion.Identity;
        if (ordinal < cycle.Poses.Count)
        {
            origin = cycle.Poses[ordinal].Origin;
            facing = cycle.Poses[ordinal].Orientation;
        }
        flattened[ordinal] = new TriMeshRef(
                        flattened[ordinal].GfxObjId,
                        CanonHeldPose.ConstructPieceXform(scaling, origin, facing));
    }

    private static Dictionary<int, Dictionary<uint, uint>>? LocateCanvasSubstitutions(
        IDatAccess datFiles,
        IReadOnlyList<TriMeshRef> pieces,
        IReadOnlyList<GenesisTextureSwap> textureEdits)
    {
        if (textureEdits.Count is 0)
            return null;

        var formerToNewByPiece = new Dictionary<int, Dictionary<uint, uint>>();
        foreach (GenesisTextureSwap edit in textureEdits)
        {
            if (!formerToNewByPiece.TryGetValue(edit.PartIndex, out var formerToNew))
            {
                formerToNew = [];
                formerToNewByPiece.Add(edit.PartIndex, formerToNew);
            }
            formerToNew[edit.OldTextureId] = edit.NewTextureId;
        }

        var outcome = new Dictionary<int, Dictionary<uint, uint>>();
        for (int pieceOrdinal = 0; pieceOrdinal < pieces.Count; ++pieceOrdinal)
        {
            if (!formerToNewByPiece.TryGetValue(pieceOrdinal, out var formerToNew))
                continue;

            PartMesh? gfx = datFiles.Get<PartMesh>(pieces[pieceOrdinal].GfxObjId);
            if (gfx is null)
                continue;

            Dictionary<uint, uint>? settled = null;
            foreach (uint canvasQid in gfx.SkinIds)
            {
                uint canvasIdent = (uint)canvasQid;
                Skin? canvas = datFiles.Get<Skin>(canvasIdent);
                if (canvas is null)
                    continue;
                uint originalTexture = (uint)canvas.TextureId;
                if (originalTexture is 0 || !formerToNew.TryGetValue(originalTexture, out uint newTexture))
                    continue;

                (settled ??= [])[canvasIdent] = newTexture;
            }

            if (settled is not null)
                outcome[pieceOrdinal] = settled;
        }

        return outcome.Count is 0 ? null : outcome;
    }
}

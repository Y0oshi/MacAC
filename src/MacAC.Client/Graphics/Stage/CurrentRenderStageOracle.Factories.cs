using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class CurrentRenderStageOracle
{
    internal static CurrentRenderMirrorFingerprint
        BuildProjFingerprint(
            uint lbIdent,
            RealmActor actor)
    {
        return BuildFingerprint(
            lbIdent,
            actor,
            ProjClassOf(actor));
    }

    internal static RenderStageHash128
        BuildDirectedShadeWiringFingerprint(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var wiring = StableRasterizeHash128.Create();
        wiring.Add(actor.MeshRefs.Count);
        for (int triMeshOrdinal = 0; triMeshOrdinal < actor.MeshRefs.Count; ++triMeshOrdinal)
        {
            TriMeshRef triMesh = actor.MeshRefs[triMeshOrdinal];
            wiring.Add(triMesh.GfxObjId);
            AppendCanvasSubstitutions(ref wiring, triMesh.CanvasOverrides);
        }

        return wiring.Finish();
    }

    internal static RenderStageHash128 CreateSurfaceOverrideFingerprint(
        IReadOnlyDictionary<uint, uint>? substitutions)
    {
        if (substitutions is null)
        {
            StableRasterizeHash128 absent = StableRasterizeHash128.Create();
            absent.Add(-1);
            return absent.Finish();
        }

        ulong xorLo = 0;
        ulong xorHi = 0;
        ulong totalLo = 0;
        ulong totalHi = 0;
        if (substitutions is Dictionary<uint, uint> dictionary)
        {
            foreach (KeyValuePair<uint, uint> duo in dictionary)
            {
                Amass(
                    duo.Key,
                    duo.Value,
                    ref xorLo,
                    ref xorHi,
                    ref totalLo,
                    ref totalHi);
            }
        }
        else
        {
            foreach ((uint canvasIdent, uint substituteIdent) in substitutions)
            {
                Amass(
                    canvasIdent,
                    substituteIdent,
                    ref xorLo,
                    ref xorHi,
                    ref totalLo,
                    ref totalHi);
            }
        }

        StableRasterizeHash128 geo = StableRasterizeHash128.Create();
        geo.Add(substitutions.Count);
        geo.Add(xorLo);
        geo.Add(xorHi);
        geo.Add(totalLo);
        geo.Add(totalHi);
        return geo.Finish();

        static void Amass(
            uint canvasTag,
            uint substituteTag,
            ref ulong xorLow,
            ref ulong xorHigh,
            ref ulong totalLow,
            ref ulong totalHigh)
        {
            StableRasterizeHash128 duoDigest = StableRasterizeHash128.Create();
            duoDigest.Add(canvasTag);
            duoDigest.Add(substituteTag);
            var val = duoDigest.Finish();
            xorLow ^= val.Low;
            xorHigh ^= val.High;
            unchecked
            {
                totalLow += val.Low;
                totalHigh += val.High;
            }
        }
    }

    private static CurrentRenderMirrorFingerprint BuildFingerprint(
        uint lbIdent,
        RealmActor actor,
        InteriorActorPartition.ProjClass projClass)
    {
        StableRasterizeHash128 xform = StableRasterizeHash128.Create();
        xform.Add(actor.Position);
        xform.Add(actor.Rotation);
        xform.Add(actor.Scale);

        var geo = StableRasterizeHash128.Create();
        geo.Add(actor.MeshRefs.Count);
        for (int triMeshOrdinal = 0;
             triMeshOrdinal < actor.MeshRefs.Count;
             ++triMeshOrdinal)
        {
            geo = BuildFingerprintLoop2(actor, triMeshOrdinal, geo);
        }

        StableRasterizeHash128 looks = StableRasterizeHash128.Create();
        if (actor.SwatchOverride is { } swatch)
        {
            looks = BuildFingerprintBranch(looks, swatch);
        }
        else
        {
            looks.Add(false);
        }

        looks.Add(actor.PieceSubstitutions.Count);
        for (int pieceOrdinal = 0;
             pieceOrdinal < actor.PieceSubstitutions.Count;
             ++pieceOrdinal)
        {
            looks = BuildFingerprintLoop(actor, pieceOrdinal, looks);
        }
        looks.Add(actor.ConcealedPiecesBitmask);

        uint flagSet = 0;
        if (actor.IsPaintShown)
            flagSet |= 1u << 0;
        if (actor.IsAncestorPaintShown)
            flagSet |= 1u << 1;
        if (actor.IsStructureShell)
            flagSet |= 1u << 2;
        if (actor.ParentCellId.HasValue)
            flagSet |= 1u << 3;
        if (actor.FxChamberIdent.HasValue)
            flagSet |= 1u << 4;
        if (actor.StructureShellMooringChamberIdent.HasValue)
            flagSet |= 1u << 5;

        return new CurrentRenderMirrorFingerprint(
            ProjectionClass: projClass,
            LandblockId: lbIdent,
            EntityId: actor.Id,
            ServerGuid: actor.ServerGuid,
            SourceId: actor.SrcGfxObjRefOrRigIdent,
            ParentCellId: actor.ParentCellId ?? 0,
            EffectCellId: actor.FxChamberIdent ?? 0,
            BuildingShellAnchorCellId: actor.StructureShellMooringChamberIdent ?? 0,
            Flags: flagSet,
            MeshCount: actor.MeshRefs.Count,
            Transform: xform.Finish(),
            Geometry: geo.Finish(),
            Appearance: looks.Finish());
    }

    private static StableRasterizeHash128 BuildFingerprintLoop(RealmActor actor, int pieceOrdinal, StableRasterizeHash128 looks)
    {
        PartSwap piece = actor.PieceSubstitutions[pieceOrdinal];
        looks.Add(piece.PartIndex);
        looks.Add(piece.GfxObjId);
        return looks;
    }

    private static StableRasterizeHash128 BuildFingerprintBranch(StableRasterizeHash128 looks, SwatchOverride swatch)
    {
        looks.Add(true);
        looks.Add(swatch.BasePaletteId);
        looks.Add(swatch.SubPalettes.Count);
        for (int spanOrdinal = 0;
                         spanOrdinal < swatch.SubPalettes.Count;
                         ++spanOrdinal)
        {
            var span =
                swatch.SubPalettes[spanOrdinal];
            looks.Add(span.SubPaletteId);
            looks.Add(span.Offset);
            looks.Add(span.Length);
        }

        return looks;
    }

    private static StableRasterizeHash128 BuildFingerprintLoop2(RealmActor actor, int triMeshOrdinal, StableRasterizeHash128 geo)
    {
        TriMeshRef triMesh = actor.MeshRefs[triMeshOrdinal];
        geo.Add(triMesh.GfxObjId);
        geo.Add(triMesh.PartTransform);
        AppendCanvasSubstitutions(ref geo, triMesh.CanvasOverrides);
        return geo;
    }
}

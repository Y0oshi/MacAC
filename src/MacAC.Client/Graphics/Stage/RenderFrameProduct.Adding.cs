using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class RasterizeCycleArena
{
    internal void AppendExterior(
        ulong epoch,
        in RenderMirrorRecord capture)
    {
        SecureStructure(epoch);
        SecureCap(ref _exterior, _exteriorTally + 1);
        _exterior[_exteriorTally++] = capture;
    }

    internal void AppendChamberSpan(
        ulong epoch,
        uint chamberIdent,
        int pviewOrder,
        ReadOnlySpan<RenderMirrorRecord> records)
    {
        SecureStructure(epoch);
        if (pviewOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(pviewOrder));

        int shift = _chamberStaticTally;
        SecureCap(ref _chamberStatics, checked(shift + records.Length));
        records.CopyTo(_chamberStatics.AsSpan(shift));
        _chamberStaticTally += records.Length;
        SecureCap(ref _chamberSpans, _chamberSpanTally + 1);
        _chamberSpans[_chamberSpanTally++] = new RasterizeCycleChamberSpan(
            chamberIdent,
            pviewOrder,
            shift,
            records.Length);
    }

    internal void AppendChamberSpan(
        ulong epoch,
        uint chamberIdent,
        int pviewOrder,
        in RenderMirrorRecord capture)
    {
        SecureStructure(epoch);
        if (pviewOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(pviewOrder));

        int shift = _chamberStaticTally;
        SecureCap(ref _chamberStatics, shift + 1);
        _chamberStatics[_chamberStaticTally++] = capture;
        SecureCap(ref _chamberSpans, _chamberSpanTally + 1);
        _chamberSpans[_chamberSpanTally++] = new RasterizeCycleChamberSpan(
            chamberIdent,
            pviewOrder,
            shift,
            1);
    }

    internal void AppendDynamic(
        ulong epoch,
        in RenderMirrorRecord capture)
    {
        SecureStructure(epoch);
        SecureCap(ref _dynamics, _dynamicTally + 1);
        _dynamics[_dynamicTally++] = capture;
    }

    internal void AppendXform(
        ulong epoch,
        in RasterizeCycleTransformCapture xform)
    {
        SecureStructure(epoch);
        SecureCap(ref _xforms, _xformTally + 1);
        _xforms[_xformTally++] = xform;
    }

    internal void AppendActorContender(
        ulong epoch,
        in RenderMirrorRecord proj,
        bool moving)
    {
        SecureStructure(epoch);
        IReadOnlyList<TriMeshRef>? triMeshRefs =
            proj.EntityPayload.MeshRefs;
        int triMeshPieceTally = triMeshRefs?.Count ?? 0;
        if (triMeshPieceTally != proj.MeshSet.MeshCount)
        {
            throw new InvalidOperationException(
                $"Projection {proj.Id} declares "
                + $"{proj.MeshSet.MeshCount} mesh parts but its "
                + $"presentation payload contains {triMeshPieceTally}.");
        }

        int triMeshPieceShift = _triMeshPieceTally;
        SecureCap(
            ref _triMeshPieces,
            checked(triMeshPieceShift + triMeshPieceTally));
        for (int pieceOrdinal = 0; pieceOrdinal < triMeshPieceTally; ++pieceOrdinal)
        {
            _triMeshPieces[_triMeshPieceTally++] = new RasterizeCycleTriMeshPiece(
                proj.Id,
                pieceOrdinal,
                triMeshRefs![pieceOrdinal]);
        }

        SecureCap(
            ref _actorContenders,
            _actorContenderTally + 1);
        _actorContenders[_actorContenderTally++] =
            new RenderFrameActorCandidate(
                proj,
                triMeshPieceShift,
                triMeshPieceTally,
                moving);
    }

    internal void AppendTaxonomy(
        ulong epoch,
        in RasterizeCycleTaxonomyCapture taxonomy)
    {
        SecureStructure(epoch);
        SecureCap(ref _classifications, _taxonomyTally + 1);
        _classifications[_taxonomyTally++] = taxonomy;
        if (taxonomy.BlendClass is RasterizeCycleBlendClass.Alpha)
            ++_alphaTaxonomyTally;
        else
            ++_solidTaxonomyTally;
    }

    internal void AppendLampSet(
        ulong epoch,
        in RasterizeCycleThingLampGroup lampSet)
    {
        SecureStructure(epoch);
        SecureCap(ref _lampSets, _lampSetTally + 1);
        _lampSets[_lampSetTally++] = lampSet;
    }

    internal void AppendPickPiece(
        ulong epoch,
        in RenderFramePickingPart piece)
    {
        SecureStructure(epoch);
        ArgumentNullException.ThrowIfNull(piece.Mesh);
        SecureCap(ref _pickPieces, _pickPieceTally + 1);
        _pickPieces[_pickPieceTally++] = piece;
    }

    internal void AppendCourseSpan(
        ulong epoch,
        RasterizeCycleContenderCourse course,
        int routeIndex,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records)
    {
        SecureStructure(epoch);
        if (routeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(routeIndex));
        if (records.Length is 0)
            return;

        int shift = _courseContenderTally;
        SecureCap(
            ref _courseContenders,
            checked(shift + records.Length));
        records.CopyTo(_courseContenders.AsSpan(shift));
        _courseContenderTally += records.Length;
        SecureCap(ref _courseSpans, _courseSpanTally + 1);
        _courseSpans[_courseSpanTally++] = new RasterizeCycleContenderSpan(
            course,
            routeIndex,
            chamberIdent,
            shift,
            records.Length);
    }
}

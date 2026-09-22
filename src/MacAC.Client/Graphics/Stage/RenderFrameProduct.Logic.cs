namespace MacAC.Client.Graphics.Stage;

internal sealed partial class RasterizeCycleArena
{
    public RenderStageEpoch Generation { get; private set; }

    public ulong CycleSequence { get; private set; }

    public RenderStageDigest SourceDigest { get; private set; }

    public RenderFrameTelemetryCounts DiagnosticCounts
    {
        get
        {
            return new(
            _exteriorTally,
            _chamberStaticTally,
            _dynamicTally,
            _xformTally,
            _solidTaxonomyTally,
            _alphaTaxonomyTally,
            _lampSetTally,
            _pickPieceTally,
            _courseContenderTally,
            _actorContenderTally,
            _triMeshPieceTally);
        }
    }

    internal ReadOnlySpan<RenderMirrorRecord> ExteriorStaticContenders =>
        _exterior.AsSpan(0, _exteriorTally);

    internal ReadOnlySpan<RenderMirrorRecord> ChamberStaticContenders =>
        _chamberStatics.AsSpan(0, _chamberStaticTally);

    internal ReadOnlySpan<RasterizeCycleChamberSpan> ChamberSpans =>
        _chamberSpans.AsSpan(0, _chamberSpanTally);

    internal ReadOnlySpan<RenderMirrorRecord> DynamicContenders =>
        _dynamics.AsSpan(0, _dynamicTally);

    internal ReadOnlySpan<RasterizeCycleTransformCapture> Xforms =>
        _xforms.AsSpan(0, _xformTally);

    internal ReadOnlySpan<RenderFrameActorCandidate> ActorContenders =>
        _actorContenders.AsSpan(0, _actorContenderTally);

    internal ReadOnlySpan<RasterizeCycleTriMeshPiece> TriMeshPieces =>
        _triMeshPieces.AsSpan(0, _triMeshPieceTally);

    internal ReadOnlySpan<RasterizeCycleTaxonomyCapture> Classifications =>
        _classifications.AsSpan(0, _taxonomyTally);

    internal ReadOnlySpan<RasterizeCycleThingLampGroup> LampSets =>
        _lampSets.AsSpan(0, _lampSetTally);

    internal ReadOnlySpan<RenderFramePickingPart> PickPieces =>
        _pickPieces.AsSpan(0, _pickPieceTally);

    internal ReadOnlySpan<RenderMirrorRecord> CourseContenders =>
        _courseContenders.AsSpan(0, _courseContenderTally);

    internal ReadOnlySpan<RasterizeCycleContenderSpan> CourseSpans =>
        _courseSpans.AsSpan(0, _courseSpanTally);

    internal void Cancel(ulong epoch)
    {
        SecureStructure(epoch);
        WipeReferenceDepot();
        SourceDigest = default;
        _srcDigestSet = false;
        _phase = ArenaPhase.Available;
    }

    internal ulong Borrow(ulong epoch, ulong borrowToken)
    {
        if (_phase is not ArenaPhase.Published || Epoch != epoch)
        {
            throw new InvalidOperationException(
                "Only the exact published render-frame arena may be borrowed");
        }
        if (borrowToken is 0)
            throw new ArgumentOutOfRangeException(nameof(borrowToken));

        _engagedBorrowTicket = borrowToken;
        _phase = ArenaPhase.Borrowed;
        return _engagedBorrowTicket;
    }

    internal ulong Epoch { get; private set; }

    internal ulong CommenceEmit(
        RenderStageEpoch gen,
        ulong frameSequence)
    {
        if (_phase is ArenaPhase.Building or ArenaPhase.Borrowed)
        {
            throw new InvalidOperationException(
                "A render-frame arena can't be reused while building or borrowed");
        }
        if (frameSequence is 0)
            throw new ArgumentOutOfRangeException(nameof(frameSequence));

        WipeReferenceDepot();
        _exteriorTally = 0;
        _chamberStaticTally = 0;
        _chamberSpanTally = 0;
        _dynamicTally = 0;
        _xformTally = 0;
        _actorContenderTally = 0;
        _triMeshPieceTally = 0;
        _taxonomyTally = 0;
        _lampSetTally = 0;
        _pickPieceTally = 0;
        _courseContenderTally = 0;
        _courseSpanTally = 0;
        _solidTaxonomyTally = 0;
        _alphaTaxonomyTally = 0;
        SourceDigest = default;
        _srcDigestSet = false;
        Generation = gen;
        CycleSequence = frameSequence;
        _engagedBorrowTicket = 0;
        Epoch = checked(Epoch + 1);
        _phase = ArenaPhase.Building;
        return Epoch;
    }

    internal void AssignSrcDigest(ulong epoch, in RenderStageDigest digest)
    {
        SecureStructure(epoch);
        if (digest.Generation != Generation)
        {
            throw new InvalidOperationException(
                $"The source digest belongs to {digest.Generation}, "
                + $"but the frame arena is building {Generation}.");
        }

        SourceDigest = digest;
        _srcDigestSet = true;
    }

    internal void Publish(ulong epoch)
    {
        SecureStructure(epoch);
        if (!_srcDigestSet)
        {
            throw new InvalidOperationException(
                "A render-frame arena can't publish without its source digest");
        }
        _phase = ArenaPhase.Published;
    }

    internal void Release(ulong epoch, ulong borrowTicket)
    {
        if (!IsBorrowValid(epoch, borrowTicket))
        {
            throw new InvalidOperationException(
                "The render-frame borrow is stale or has by now been released");
        }

        _engagedBorrowTicket = 0;
        _phase = ArenaPhase.Published;
    }

    internal bool IsBorrowValid(ulong epoch, ulong borrowTicket)
    {
        return _phase is ArenaPhase.Borrowed
        && Epoch == epoch
        && borrowTicket is not 0
        && _engagedBorrowTicket == borrowTicket;
    }

    internal bool CanAssemble => _phase is ArenaPhase.Available or ArenaPhase.Published;

    private void SecureStructure(ulong epoch)
    {
        if (_phase is not ArenaPhase.Building || Epoch != epoch)
        {
            throw new InvalidOperationException(
                "The render-frame writer no longer owns this arena");
        }
    }

    private void WipeReferenceDepot()
    {
        if (_pickPieceTally > 0)
            Array.Clear(_pickPieces, 0, _pickPieceTally);
        if (_actorContenderTally > 0)
            Array.Clear(_actorContenders, 0, _actorContenderTally);
        if (_triMeshPieceTally > 0)
            Array.Clear(_triMeshPieces, 0, _triMeshPieceTally);
    }
}

using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics.Batching;

public sealed class AnimatedActorLedger
{
    private readonly Dictionary<int, ulong> _pieceGfxObjRefSubstitutions = [];
    private ulong _concealedBitmask = 0;

    public AnimSequencer Scheduler { get; }

    public AnimatedActorLedger(AnimSequencer scheduler)
    {
        System.ArgumentNullException.ThrowIfNull(scheduler);
        Scheduler = scheduler;
    }

    public void ConcealPieces(ulong concealedBitmask) => _concealedBitmask = concealedBitmask;

    public bool IsPieceConcealed(int pieceIndex)
    {
        return pieceIndex is < 0 or >= 64 ? false : (_concealedBitmask & (1ul << pieceIndex)) is not 0;
    }

    public void AssignPieceOverride(int pieceIndex, ulong gfxObjRefIdent)
        => _pieceGfxObjRefSubstitutions[pieceIndex] = gfxObjRefIdent;

    public bool TryFetchPieceOverride(int pieceIndex, out ulong gfxObjRefIdent)
    {
        return _pieceGfxObjRefSubstitutions.TryGetValue(pieceIndex, out gfxObjRefIdent);
    }

    public ulong LocatePieceGfxObjRef(int pieceIndex, ulong rigDefault)
        => TryFetchPieceOverride(pieceIndex, out var ov) ? ov : rigDefault;
}

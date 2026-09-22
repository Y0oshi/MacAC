namespace MacAC.Client.Graphics.Batching;

public sealed class TriMeshRefAlterationFault(
    string msg,
    bool alterationSealed,
    Exception interiorException) : Exception(msg, interiorException)
{
    public bool AlterationSealed { get; } = alterationSealed;
}

public interface IBatchMeshBridge
{
    void IncrementRefTally(ulong ident);

    void DecrementRefTally(ulong ident);

    void PinReadiedRasterizeBlob(ulong ident) => IncrementRefTally(ident);

    bool IsRasterizeBlobPrimed(ulong ident) => true;
}

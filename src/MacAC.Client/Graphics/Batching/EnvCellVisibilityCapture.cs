namespace MacAC.Client.Graphics.Batching;

public sealed class EnvCellVisibilityCapture
{
    public List<EnvironChamberLandblock> VisibleLandblocks { get; init; } = [];

    public Dictionary<uint, Dictionary<ulong, List<InstanceData>>> BatchedByCell { get; init; } = [];

    public int PostReadyReservoirOrdinal { get; init; }

    public bool IsEmpty => VisibleLandblocks.Count is 0 && BatchedByCell.Count is 0;
}

namespace MacAC.Mechanics.Landscape;

public sealed class TerrainSlotPool
{
    private readonly Queue<int> _recycled = new();
    private readonly HashSet<int> _inUse = [];
    private int _hiWater;

    public TerrainSlotPool(int startingCap = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startingCap);
        Capacity = startingCap;
    }

    public int Capacity { get; private set; }

    public int FetchedCount => _inUse.Count;

    public int Carve(out bool needsExpand)
    {
        int socket = _recycled.TryDequeue(out int recycled) ? recycled : _hiWater++;
        _inUse.Add(socket);
        needsExpand = socket >= Capacity;
        return socket;
    }

    public void Unpin(int socket)
    {
        if (!_inUse.Remove(socket))
        {
            throw new InvalidOperationException(
                $"Slot {socket} wasn't allocated (double-free or unrecognized slot)");
        }
        _recycled.Enqueue(socket);
    }

    public void EnlargeTo(int newCapacity)
    {
        if (newCapacity < Capacity)
            throw new ArgumentException("Capacity can only grow", nameof(newCapacity));
        Capacity = newCapacity;
    }
}

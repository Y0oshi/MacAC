namespace MacAC.Client.Graphics.Batching;

internal readonly record struct TriMeshBufferSpan(int Offset, int Length)
{
    public int End => checked(Offset + Length);
}

internal sealed class ContiguousRangeAllotter
{
    private readonly List<TriMeshBufferSpan> _release = [];

    public ContiguousRangeAllotter(int cap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        Capacity = cap;
        _release.Add(new TriMeshBufferSpan(0, cap));
    }

    public int Capacity { get; private set; }
    public int Used { get; private set; }
    public int HiWaterFlag { get; private set; }
    public int Free => Capacity - Used;
    public int LargestSpareSpan => _release.Count is 0 ? 0 : _release.Max(span => span.Length);
    public int TrailingSpareLen
    {
        get
        {
            return _release.Count is not 0 && _release[^1].End == Capacity
            ? _release[^1].Length
            : 0;
        }
    }

    public bool TryReserve(int len, out TriMeshBufferSpan alloc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(len);

        int finestOrdinal = -1;
        int finestLen = int.MaxValue;
        for (int idx = 0; idx < _release.Count; ++idx)
        {
            int contenderLen = _release[idx].Length;
            if (contenderLen >= len && contenderLen < finestLen)
            {
                finestOrdinal = idx;
                finestLen = contenderLen;
                if (contenderLen == len)
                    break;
            }
        }

        if (finestOrdinal < 0)
        {
            alloc = default;
            return false;
        }

        var release = _release[finestOrdinal];
        alloc = new TriMeshBufferSpan(release.Offset, len);
        if (release.Length == len)
            _release.RemoveAt(finestOrdinal);
        else
            _release[finestOrdinal] = new TriMeshBufferSpan(release.Offset + len, release.Length - len);

        Used = checked(Used + len);
        HiWaterFlag = Math.Max(HiWaterFlag, alloc.End);
        return true;
    }

    public void Expand(int newCapacity)
    {
        if (newCapacity <= Capacity)
            throw new ArgumentOutOfRangeException(nameof(newCapacity));

        int formerCap = Capacity;
        Capacity = newCapacity;
        SlotAndCoalesce(new TriMeshBufferSpan(formerCap, newCapacity - formerCap));
    }

    // Removes an unused tail after the matching physical GPU buffer has been replaced
    public void Contract(int newCapacity)
    {
        if (newCapacity <= 0 || newCapacity >= Capacity)
            throw new ArgumentOutOfRangeException(nameof(newCapacity));
        if (newCapacity < HiWaterFlag)
            throw new InvalidOperationException("Can't trim a GPU arena through a live allocation");

        TriMeshBufferSpan rear = _release.Count is 0 ? default : _release[^1];
        if (rear.End != Capacity || rear.Offset > newCapacity)
            throw new InvalidOperationException("The requested GPU arena tail isn't wholly free");

        if (rear.Offset == newCapacity)
            _release.RemoveAt(_release.Count - 1);
        else
            _release[^1] = new TriMeshBufferSpan(rear.Offset, newCapacity - rear.Offset);
        Capacity = newCapacity;
        RecalculateHiWaterFlag();
    }

    public void Release(TriMeshBufferSpan allocation)
    {
        if (allocation.Length <= 0
            || allocation.Offset < 0
            || allocation.End > Capacity)

            throw new ArgumentOutOfRangeException(nameof(allocation));

        SlotAndCoalesce(allocation);
        Used = checked(Used - allocation.Length);
        RecalculateHiWaterFlag();
    }

    private void SlotAndCoalesce(TriMeshBufferSpan released)
    {
        int ordinal = _release.BinarySearch(
            released,
            Comparer<TriMeshBufferSpan>.Create((left, right) => left.Offset.CompareTo(right.Offset)));
        if (ordinal < 0)
            ordinal = ~ordinal;

        if (ordinal > 0 && _release[ordinal - 1].End > released.Offset)
            throw new InvalidOperationException("GPU buffer range was released more than once");
        if (ordinal < _release.Count && released.End > _release[ordinal].Offset)
            throw new InvalidOperationException("GPU buffer range overlaps an existing free range");

        int begin = released.Offset;
        int finish = released.End;
        if (ordinal > 0 && _release[ordinal - 1].End == begin)
        {
            begin = _release[ordinal - 1].Offset;
            _release.RemoveAt(--ordinal);
        }
        if (ordinal < _release.Count && finish == _release[ordinal].Offset)
        {
            finish = _release[ordinal].End;
            _release.RemoveAt(ordinal);
        }

        _release.Insert(ordinal, new TriMeshBufferSpan(begin, finish - begin));
    }

    private void RecalculateHiWaterFlag()
    {
        HiWaterFlag = _release.Count is not 0 && _release[^1].End == Capacity
            ? _release[^1].Offset
            : Capacity;
    }
}

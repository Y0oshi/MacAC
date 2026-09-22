namespace MacAC.Mechanics.Kinetics;

// Retail's fixed arena of transition records
internal sealed class ShiftScratch
{
    internal const int Capacity = 10;

    private readonly Changeover?[] _arena = new Changeover?[Capacity];
    private int _top;
    private int _holderThread;

    internal int EngagedZDepth => _top;

    internal Changeover Rent()
    {
        int thread = System.Environment.CurrentManagedThreadId;
        if (_top is not 0 && _holderThread != thread)
            throw new InvalidOperationException("Physics transition scratch can't be shared across threads");
        if (_top >= Capacity)
            throw new InvalidOperationException($"Physics transition nesting exceeds retail's {Capacity}-record scratch arena");

        if (_top is 0)
            _holderThread = thread;

        Changeover capture = _arena[_top] ??= new Changeover();
        ++_top;
        capture.ResetForReuse();
        return capture;
    }

    internal void Yield(Changeover capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        int below = _top - 1;
        bool lifo = below >= 0
            && _holderThread == System.Environment.CurrentManagedThreadId
            && ReferenceEquals(_arena[below], capture);
        if (!lifo)
            throw new InvalidOperationException("Physics transition scratch has to be returned in LIFO order on its owning thread");

        _top = below;
        if (_top is 0)
            _holderThread = 0;
    }
}

namespace MacAC.Mechanics.Kinetics;

public enum CanonClockVerdict
{
    Advance,
    Suspend,
}

public readonly record struct CanonQuantumBatch(int FullSteps, float Remainder, bool Discarded)
{
    public int Count => FullSteps + (Remainder > 0f ? 1 : 0);

    public float FetchQuantum(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index < FullSteps ? KineticBody.MaxQuantum : Remainder;
    }
}

public sealed class CanonQuantumClock
{
    private const double CycleEpsilon = 0.000199999995;

    private double _banked;

    public double QueuedSecs => _banked;
    public bool IsActive { get; private set; } = true;

    public CanonQuantumBatch Advance(double passedSecs)
    {
        if (double.IsNaN(passedSecs) || passedSecs < 0.0)
            return Toss();

        double owed = _banked + passedSecs;
        if (owed <= CycleEpsilon)
        {
            _banked = 0.0;
            return default;
        }
        if (owed > KineticBody.HugeQuantum)
            return Toss();

        int wholeHops = 0;
        while (owed > KineticBody.MaxQuantum)
        {
            ++wholeHops;
            owed -= KineticBody.MaxQuantum;
        }

        float rear = 0f;
        if (owed > KineticBody.LowerQuantum)
        {
            rear = (float)owed;
            owed = 0.0;
        }

        _banked = owed;
        return new CanonQuantumBatch(wholeHops, rear, Discarded: false);
    }

    public void Deactivate() => IsActive = false;

    public bool Engage()
    {
        if (IsActive)
            return false;
        IsActive = true;
        _banked = 0.0;
        return true;
    }

    public void Reset() => _banked = 0.0;

    public void RestartForJoinRealm(bool isStatic = false)
    {
        _banked = 0.0;
        if (!isStatic)
            IsActive = true;
    }

    private CanonQuantumBatch Toss()
    {
        _banked = 0.0;
        return new CanonQuantumBatch(0, 0f, Discarded: true);
    }
}

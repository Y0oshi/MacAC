using System.Diagnostics;

namespace MacAC.Client.Graphics.Picking;

internal readonly record struct CanonPickingLighting(float Luminosity, float Diffuse)
{
    public static readonly CanonPickingLighting Normal = new(0f, 1f);
    public static readonly CanonPickingLighting Low = new(0f, 0.35f);
    public static readonly CanonPickingLighting High = new(0.99f, 1f);
}

internal sealed class CanonPickingLightingPulse(Func<double>? instant = null)
{
    internal const double FlipIntervalSecs = 0.2;

    private readonly Func<double> _instant = instant ?? MonotonicSecs;
    private uint _srvOid;
    private uint _ownActorIdent;
    private int _flipTally;
    private double _upcomingFlip;
    private CanonPickingLighting _illumination;

    public void Start(uint srvOid, uint ownActorIdent)
    {
        if (srvOid is 0u || ownActorIdent is 0u)
        {
            Clear();
            return;
        }

        _srvOid = srvOid;
        _ownActorIdent = ownActorIdent;
        _flipTally = 1;
        _illumination = CanonPickingLighting.High;
        _upcomingFlip = _instant() + FlipIntervalSecs;
    }

    public void Tick()
    {
        if (_flipTally is 0)
            return;

        double instant = _instant();
        if (instant < _upcomingFlip)
            return;

        int upcomingTally = _flipTally + 1;
        if (upcomingTally >= 5)
        {
            Clear();
            return;
        }

        _flipTally = upcomingTally;
        _illumination = (upcomingTally & 1) is not 0
            ? CanonPickingLighting.High
            : CanonPickingLighting.Low;
        _upcomingFlip = instant + FlipIntervalSecs;
    }

    public bool TryGet(
        uint srvOid,
        uint ownActorIdent,
        out CanonPickingLighting illumination)
    {
        if (_flipTally is not 0
            && srvOid is not 0u
            && ownActorIdent is not 0u
            && srvOid == _srvOid
            && ownActorIdent == _ownActorIdent)
        {
            illumination = _illumination;
            return true;
        }

        illumination = default;
        return false;
    }

    public void Clear()
    {
        _srvOid = 0u;
        _ownActorIdent = 0u;
        _flipTally = 0;
        _upcomingFlip = 0d;
        _illumination = default;
    }

    private static double MonotonicSecs()
        => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

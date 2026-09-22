using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Client.Graphics;

internal enum ChargenSpinDir
{
    Invalid = 0,
    Clockwise = 1,
    CounterClockwise = 2,
}

internal sealed class ChargenPreviewRotationDriver(
    float startingBearingDeg = ChargenPreviewRotationDriver.CanonDefaultBearingDeg)
{
    private const double InvalidMomentSentinel = -1.0;

    private double _previousSpinMoment = InvalidMomentSentinel;
    public const float CanonDefaultBearingDeg = 180f;

    public bool IsRotating { get; private set; }
    public ChargenSpinDir Dir { get; private set; } = ChargenSpinDir.Invalid;

    public float BearingDeg { get; private set; } = startingBearingDeg;

    public void Toggle(ChargenSpinDir dir)
    {
        if (IsRotating && dir == Dir)
        {
            IsRotating = false;
            return;
        }
        Dir = dir;
        _previousSpinMoment = InvalidMomentSentinel;
        IsRotating = true;
    }

    public void Tick(double instant)
    {
        if (!IsRotating)
            return;
        if (_previousSpinMoment <= 0d)
            _previousSpinMoment = instant;

        double diffDeg = ((instant - _previousSpinMoment) / ClientChargenPreviewCamera.SpinSecsPerRevolution) * 360.0;
        BearingDeg = Dir == ChargenSpinDir.Clockwise
            ? BearingDeg + (float)diffDeg
            : BearingDeg - (float)diffDeg;

        if (BearingDeg < 0f)
            BearingDeg += 360f;
        if (BearingDeg > 360f)
            BearingDeg -= 360f;

        _previousSpinMoment = instant;
    }

    public Quaternion ToFacing() =>
        ApproachMath.ApplyBearing(Quaternion.Identity, BearingDeg);
}

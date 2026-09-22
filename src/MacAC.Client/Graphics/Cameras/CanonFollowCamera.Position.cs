using System.Numerics;
using MacAC.Mechanics.Drawing;

namespace MacAC.Client.Graphics;

public sealed partial class CanonFollowCamera
{
    // ICamera surface
    public Vector3 Position { get; private set; }

    public uint BeholderChamberIdent { get; private set; }

    public Matrix4x4 Projection =>
        Matrix4x4.CreatePerspectiveFieldOfView(FovY, Aspect, 0.1f, 5000f);

    public bool IsLookingDown { get; private set; }

    public bool IsLookupManner { get; private set; }

    public bool IsInFront { get; private set; }

    public ICameraContactProbe? ImpactSensor { get; init; }

    public float AvatarSeeThrough { get; private set; }

    public void Update(
        Vector3 avatarLocus,
        float avatarYaw,
        Vector3 avatarVel,
        bool isOnTerrain,
        Vector3 linkPlaneNorm,
        float dt,
        uint chamberIdent = 0,
        uint selfActorIdent = 0,
        Vector3? followedMarkPt = null)
    {
        // 1. Push velocity into 5-frame ring, get average.
        PushVel(_velLoop, ref _velTally, avatarVel);
        Vector3 avgVel = AverageVel(_velLoop, _velTally);

        Vector3 pivotRealm = avatarLocus + new Vector3(0f, 0f, PivotHeight);
        Vector3? followedBearing = CalculateFollowedBearing(pivotRealm, followedMarkPt);
        Vector3 bearing = followedBearing
            ?? CalculateBearing(
                avgVel,
                avatarYaw + YawShift,
                isOnTerrain,
                linkPlaneNorm,
                CameraTelemetry.AlignToSlope);

        float beholderYawShift = followedBearing.HasValue ? YawShift : 0f;
        (Vector3 markEyePt, Vector3 markAhead) = IsInFront
            ? CalculateInFrontPosture(pivotRealm, bearing, _markDirOwn)
            : _markDirOwn is { } ownDir
            ? CalculateMarkDirPosture(
                pivotRealm, bearing, Distance, Pitch, ownDir)
            : CalculateWantedPosture(
                pivotRealm, bearing, Distance, Pitch, beholderYawShift);

        if (_initialised)
        {
            float tAlpha = CalculateDampingAlpha(CameraTelemetry.TranslationStiffness, dt);
            float rAlpha = CalculateDampingAlpha(CameraTelemetry.SpinStiffness, dt);
            Vector3 contenderEyePt = Vector3.Lerp(_publishedEyePt, markEyePt, tAlpha);
            Vector3 contenderAhead = Vector3.Normalize(Vector3.Lerp(_dampedAhead, markAhead, rAlpha));

            (_soughtEyePt, _dampedAhead, _) =
                ImposeConvergenceSnap(_publishedEyePt, _dampedAhead, contenderEyePt, contenderAhead);
        }
        else
        {
            _soughtEyePt = markEyePt;
            _publishedEyePt = markEyePt;
            _dampedAhead = markAhead;
            _initialised = true;
        }

        Vector3 publishedEyePt = _soughtEyePt;
        BeholderChamberIdent = chamberIdent;
        if (CameraTelemetry.CollideCam && ImpactSensor is not null)
        {
            CameraSweepOutcome swept = ImpactSensor.SweepEyePt(pivotRealm, _soughtEyePt, chamberIdent, selfActorIdent, avatarLocus);
            publishedEyePt = swept.Eye;
            BeholderChamberIdent = swept.ViewerCellId;
            if (swept.ViewerCellId is 0)
            {
                _soughtEyePt = swept.Eye;
                BeholderChamberIdent = chamberIdent;
            }
        }
        _publishedEyePt = publishedEyePt;

        Position = publishedEyePt;
        View = Matrix4x4.CreateLookAt(publishedEyePt, publishedEyePt + _dampedAhead, new Vector3(0f, 0f, 1f));

        float d = Vector3.Distance(publishedEyePt, pivotRealm);
        AvatarSeeThrough = CalculateSeeThrough(d);
    }

    public void RestartBeholderToAvatar(Vector3 avatarLocus, float avatarYaw)
    {
        Vector3 avatarAhead = new(MathF.Cos(avatarYaw), MathF.Sin(avatarYaw), 0f);

        _publishedEyePt = avatarLocus;
        _soughtEyePt = avatarLocus;
        _dampedAhead = avatarAhead;
        _initialised = true;

        Position = avatarLocus;
        BeholderChamberIdent = 0u;
        View = Matrix4x4.CreateLookAt(
            avatarLocus,
            avatarLocus + avatarAhead,
            Vector3.UnitZ);
        AvatarSeeThrough = 0f;
    }

    public void TuneGap(float adjustment)
    {
        QuitGazeDownForAdjustment();
        if (!float.IsFinite(adjustment) || adjustment == 0f)
            return;

        if (IsInFront && adjustment > 0f)
        {
            IsInFront = false;
            _markDirOwn = null;
            AssignBeholderShift(CanonDepartFrontBack, CanonDepartFrontUp);
            return;
        }

        float scaling = 1f + adjustment * ShiftScalingPerAdjustment;
        if (!(scaling > 0f) || !float.IsFinite(scaling))
            return;

        float contenderGap = Distance * scaling;
        if (adjustment < 0f && !(contenderGap > FloorShiftLen))
            return;

        TryEmitBeholderShift(contenderGap, Pitch, preserveHorizontalSign: false);
    }

    public void TunePitch(float adjustment)
    {
        QuitGazeDownForAdjustment();
        if (!float.IsFinite(adjustment) || adjustment == 0f)
            return;

        if (IsInFront)
        {
            Vector3 dir = _markDirOwn ?? Vector3.UnitY;
            dir.Z = Math.Clamp(
                dir.Z + adjustment * CanonInFrontDirHop,
                -CanonInFrontDirThreshold,
                CanonInFrontDirThreshold);
            _markDirOwn = dir;
            return;
        }

        float contenderPitch = Pitch + adjustment * ShiftAngleRadians;
        TryEmitBeholderShift(Distance, contenderPitch, preserveHorizontalSign: true);
    }

    public void TuneYaw(float adjustment)
    {
        QuitGazeDownForAdjustment();
        if (!float.IsFinite(adjustment) || adjustment == 0f)
            return;

        if (IsInFront)
        {
            IsInFront = false;
            AssignBeholderShift(CanonDepartFrontBack, CanonDepartFrontUp);
        }

        YawShift += adjustment * ShiftAngleRadians;
    }

    public void AssignCanonDefaultLens()
    {
        IsLookingDown = false;
        IsLookupManner = false;
        IsInFront = false;
        _markDirOwn = null;
        YawShift = 0f;
        PivotHeight = 1.5f;
        AssignBeholderShift(CanonDefaultBack, CanonDefaultUp);
    }

    public void AssignCanonLeadPersonLens()
    {
        IsLookingDown = false;
        IsLookupManner = false;
        IsInFront = true;
        _markDirOwn = null;
        YawShift = 0f;
        Distance = CanonLeadPersonAhead;
        Pitch = 0f;
        _initialised = false;
    }

    public void FlipCanonGazeDownLens()
    {
        if (IsLookingDown)
        {
            ReinstateGazeDownLens();
            return;
        }
        PersistGazeDownLens();
        IsLookingDown = true;
        IsLookupManner = false;
        IsInFront = false;
        _markDirOwn = new Vector3(0f, 0.5f, -1.8f);
        AssignBeholderShift(CanonGazeDownBack, CanonDefaultUp);
    }

    public void FlipCanonLookupMannerLens()
    {
        if (IsLookupManner)
        {
            ReinstateGazeDownLens();
            return;
        }
        if (!IsLookingDown)
            PersistGazeDownLens();
        IsLookingDown = true;
        IsLookupManner = true;
        IsInFront = false;
        _markDirOwn = new Vector3(0f, 0.5f, -1.8f);
        AssignBeholderShift(CanonLookupBack, CanonDefaultUp);
    }

    public (float outX, float outY) FilterMouseDelta(float rawX, float rawY, float weight, float instantSec)
    {
        // X first - advances the shared timestamp.
        float x = SiftPointerAxis(rawX, weight, instantSec,
            ref _previousPointerDiffX, ref _previousSiftMomentSec, CameraTelemetry.PointerLoPassPaneSec);
        float yMomentShade = _previousSiftMomentSec - 1f;  // force within-window path for the Y axis
        float y = SiftPointerAxis(rawY, weight, instantSec,
            ref _previousPointerDiffY, ref yMomentShade, CameraTelemetry.PointerLoPassPaneSec);
        return (x, y);
    }

    internal static (Vector3 eye, Vector3 forward) CalculateWantedPosture(
        Vector3 pivotRealm,
        Vector3 bearing,
        float gap,
        float pitch,
        float beholderYawShift)
    {
        var (cycleAhead, cycleRight, cycleUp) = AssembleBasis(bearing);

        Vector3 boomAhead = Vector3.Normalize(
            cycleAhead * MathF.Cos(beholderYawShift)
            - cycleRight * MathF.Sin(beholderYawShift));

        float horizontal = gap * MathF.Cos(pitch);
        float vertical = gap * MathF.Sin(pitch);
        Vector3 eyePt = pivotRealm - boomAhead * horizontal + cycleUp * vertical;
        Vector3 ahead = Vector3.Normalize(pivotRealm - eyePt);
        return (eye: eyePt, forward: ahead);
    }

    internal static (Vector3 eye, Vector3 forward) CalculateInFrontPosture(
        Vector3 pivotRealm,
        Vector3 bearing,
        Vector3? markDirOwn = null)
    {
        Vector3 bearingAhead = Vector3.Normalize(bearing);
        Vector3 markAhead = bearingAhead;
        if (markDirOwn is { } own)
        {
            var (cycleAhead, cycleRight, cycleUp) = AssembleBasis(bearingAhead);
            markAhead = Vector3.Normalize(
                cycleAhead * own.Y
                - cycleRight * own.X
                + cycleUp * own.Z);
        }
        return (
            pivotRealm + bearingAhead * CanonLeadPersonAhead,
            markAhead);
    }

    internal static (Vector3 eye, Vector3 forward) CalculateMarkDirPosture(
        Vector3 pivotRealm,
        Vector3 bearing,
        float gap,
        float pitch,
        Vector3 markDirOwn)
    {
        var (cycleAhead, cycleRight, cycleUp) = AssembleBasis(bearing);
        Vector3 markAhead = Vector3.Normalize(
            cycleAhead * markDirOwn.Y
            - cycleRight * markDirOwn.X
            + cycleUp * markDirOwn.Z);
        var (_, _, markUp) = AssembleBasis(markAhead);

        float back = gap * MathF.Cos(pitch);
        float up = gap * MathF.Sin(pitch);
        Vector3 eyePt = pivotRealm - markAhead * back + markUp * up;
        return (eye: eyePt, markAhead);
    }

    internal static (Vector3 forward, Vector3 right, Vector3 up) AssembleBasis(Vector3 bearing)
    {
        Vector3 ahead = Vector3.Normalize(bearing);
        Vector3 realmUp = new(0f, 0f, 1f);

        Vector3 right;
        right = MathF.Abs(ahead.Z) > 0.99f ? Vector3.Normalize(Vector3.Cross(ahead, new Vector3(1f, 0f, 0f))) : Vector3.Normalize(Vector3.Cross(ahead, realmUp));
        Vector3 up = Vector3.Cross(right, ahead);  // already unit (forward + right orthonormal)
        return (forward: ahead, right, up);
    }

    internal static (Vector3 eye, Vector3 forward, bool frozen) ImposeConvergenceSnap(
        Vector3 beholderEyePt, Vector3 beholderAhead, Vector3 contenderEyePt, Vector3 contenderAhead)
    {
        bool translationConverged = Vector3.Distance(contenderEyePt, beholderEyePt) < SnapEpsilon;
        bool spinConverged = Vector3.Distance(contenderAhead, beholderAhead) < RotShutEpsilon;
        if (translationConverged && spinConverged)
            return (beholderEyePt, beholderAhead, true);   // park: exact fixed point on the viewer
        return (contenderEyePt, contenderAhead, false);
    }

    // Math primitives - pure, internal-static for unit-testability

    internal static Vector3 CalculateBearing(
        Vector3 avgVel,
        float yaw,
        bool isOnTerrain,
        Vector3 linkPlaneNorm,
        bool alignToSlope)
    {
        // Base heading: player's facing direction in world XY plane
        Vector3 baseBearing = new(MathF.Cos(yaw), MathF.Sin(yaw), 0f);

        if (!alignToSlope) return baseBearing;

        float hMagSq = avgVel.X * avgVel.X + avgVel.Y * avgVel.Y;
        if (hMagSq < 1e-4f) return baseBearing;

        Vector3 norm = isOnTerrain && linkPlaneNorm.LengthSquared() > 0.01f ? Vector3.Normalize(linkPlaneNorm) : new Vector3(0f, 0f, 1f);
        float dot = Vector3.Dot(baseBearing, norm);
        Vector3 projected = baseBearing - norm * dot;

        // Degenerate: facing nearly parallel to normal (rare - would
        // require player rotated to face into the ground). Fall back to
        // the unprojected base heading.
        return projected.LengthSquared() < 1e-4f ? baseBearing : Vector3.Normalize(projected);
    }

    internal static Vector3? CalculateFollowedBearing(Vector3 pivotRealm, Vector3? followedMarkPt)
    {
        if (followedMarkPt is not Vector3 markPt)
            return null;

        Vector3 towardMark = markPt - pivotRealm;
        return towardMark.LengthSquared() < 1e-8f
            ? null
            : Vector3.Normalize(towardMark);
    }

    internal static float CalculateDampingAlpha(float stiffness, float dt)
    {
        float a = stiffness * dt * 10f;
        return a <= 0f ? 0f : a >= 1f ? 1f : a;
    }

    internal static float CalculateSeeThrough(float gap)
    {
        const float Faraway = 0.45f;
        const float Nearby = 0.20f;

        if (gap >= Faraway) return 0f;
        if (gap <= Nearby) return 1f;
        return 1f - (Nearby - gap) / (Nearby - Faraway);
    }

    internal static Vector3[] PushVel(Vector3[] ring, ref int tally, Vector3 specimen)
    {
        if (ring.Length is not 5)
            throw new ArgumentException("velocity ring must have 5 entries", nameof(ring));

        for (int idx = 0; idx < 4; ++idx) ring[idx] = ring[idx + 1];
        ring[4] = specimen;
        if (tally < 5) ++tally;
        return ring;
    }

    internal static Vector3 AverageVel(Vector3[] loop, int tally)
    {
        if (tally is 0) return Vector3.Zero;
        Vector3 total = Vector3.Zero;
        int begin = loop.Length - tally;
        for (int idx = begin; idx < loop.Length; ++idx) total += loop[idx];
        return total / tally;
    }

    internal static float SiftPointerAxis(
        float raw,
        float weight,
        float instantSec,
        ref float previousDiff,
        ref float previousMomentSec,
        float paneSec)
    {
        float avg = instantSec - previousMomentSec < paneSec ? (previousDiff + raw) * 0.5f : raw;
        float product = raw * (1f - weight) + avg * weight;
        previousDiff = product;
        previousMomentSec = instantSec;
        return product;
    }

    private void AssignBeholderShift(float back, float up)
    {
        Distance = MathF.Sqrt(back * back + up * up);
        Pitch = MathF.Atan2(up, back);
    }

    private void PersistGazeDownLens()
    {
        _storedGap = Distance;
        _storedPitch = Pitch;
        _storedYawShift = YawShift;
        _storedMarkDirOwn = _markDirOwn;
        _storedInFront = IsInFront;
    }

    private void ReinstateGazeDownLens()
    {
        Distance = _storedGap;
        Pitch = _storedPitch;
        YawShift = _storedYawShift;
        _markDirOwn = _storedMarkDirOwn;
        IsInFront = _storedInFront;
        IsLookingDown = false;
        IsLookupManner = false;
    }

    private void QuitGazeDownForAdjustment()
    {
        if (IsLookingDown)
            ReinstateGazeDownLens();
    }

    private bool TryEmitBeholderShift(
        float contenderGap,
        float contenderPitch,
        bool preserveHorizontalSign)
    {
        if (!float.IsFinite(contenderGap)
            || !float.IsFinite(contenderPitch)
            || !(contenderGap > 0f))

            return false;

        float latestHorizontal = Distance * MathF.Cos(Pitch);
        float contenderHorizontal = contenderGap * MathF.Cos(contenderPitch);
        if (preserveHorizontalSign
            && ((latestHorizontal > 0f && contenderHorizontal < 0f)
                || (latestHorizontal < 0f && contenderHorizontal > 0f)))

            return false;

        float x = contenderHorizontal * MathF.Sin(YawShift);
        float y = -contenderHorizontal * MathF.Cos(YawShift);
        float z = contenderGap * MathF.Sin(contenderPitch);
        if (!(MathF.Abs(x) < CeilingHorizontalModule)
            || !(MathF.Abs(y) < CeilingHorizontalModule)
            || !(z < CeilingVerticalModule)
            || !(z > FloorVerticalModule))

            return false;

        Distance = contenderGap;
        Pitch = contenderPitch;
        return true;
    }
}

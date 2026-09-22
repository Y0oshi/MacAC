using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed class FollowCamera : IClientCamera
{
    private const float CanonDefaultBack = 2.5f;
    private const float CanonDefaultUp = 0.75f;
    private bool _storedInFront;
    private float _storedGap;
    private float _storedPitch;
    private float _storedYawShift;
    private Vector3? _markDirOwn;
    private Vector3? _storedMarkDirOwn;

    public bool IsLookingDown { get; private set; }
    public bool IsLookupMode { get; private set; }
    public bool IsInHead { get; private set; }
    public Vector3 Position { get; private set; }
    public float Aspect { get; set; } = 16f / 9f;
    public float FovY { get; set; } = CanonFieldOfView.DefaultImposedFovY;

    public float Distance { get; set; } = 8f;
    public const float GapLower = 2f;
    public const float GapUpper = 40f;

    public float Pitch { get; set; } = 0.35f;  // ~20 degrees

    public float YawOffset { get; set; } = 0f;

    public float EyePtHeight { get; set; } = 1.5f;

    private const float PitchLower = -0.7f;
    private const float PitchUpper = 1.4f;

    private float _avatarYaw;
    private Vector3 _gazeAt;

    private float _followedZ;
    private bool _followedZInitialised;

    public Matrix4x4 View =>
        Matrix4x4.CreateLookAt(Position, _gazeAt, Vector3.UnitZ);

    public Matrix4x4 Projection =>
        Matrix4x4.CreatePerspectiveFieldOfView(FovY, Aspect, 0.1f, 5000f);

    public void Update(Vector3 avatarLocus, float avatarYaw, bool isOnTerrain = true, float dt = 1f / 60f)
    {
        _avatarYaw = avatarYaw;

        if (!_followedZInitialised)
        {
            _followedZ = avatarLocus.Z;
            _followedZInitialised = true;
        }
        else if (isOnTerrain)
        {
            _followedZ = avatarLocus.Z;
        }
        else if (avatarLocus.Z < _followedZ)
        {
            _followedZ = avatarLocus.Z;
        }
        // else: airborne and rising - keep _trackedZ pinned

        _gazeAt = avatarLocus + new Vector3(0f, 0f, EyePtHeight);

        float netYaw = avatarYaw + YawOffset;
        float aheadX = MathF.Cos(netYaw);
        float aheadY = MathF.Sin(netYaw);

        float horizontalDistance = Distance * MathF.Cos(Pitch);
        float verticalDistance = Distance * MathF.Sin(Pitch);

        if (IsInHead)
        {
            Vector3 ahead = new(MathF.Cos(avatarYaw), MathF.Sin(avatarYaw), 0f);
            Position = new Vector3(
                avatarLocus.X,
                avatarLocus.Y,
                _followedZ + EyePtHeight) + ahead * 0.18f;
            _gazeAt = Position + ahead;
        }
        else if (_markDirOwn is { } ownDir)
        {
            Vector3 pivot = new(avatarLocus.X, avatarLocus.Y, _followedZ + EyePtHeight);
            var directedPosture = CanonFollowCamera.CalculateMarkDirPosture(
                pivot,
                new Vector3(MathF.Cos(avatarYaw), MathF.Sin(avatarYaw), 0f),
                Distance,
                Pitch,
                ownDir);
            Position = directedPosture.eye;
            Vector3 dir = directedPosture.forward;
            _gazeAt = Position + dir;
        }
        else
        {
            Position = new Vector3(
                avatarLocus.X - aheadX * horizontalDistance,
                avatarLocus.Y - aheadY * horizontalDistance,
                _followedZ + EyePtHeight + verticalDistance);   // ← uses tracked Z (pinned to ground while airborne)
        }
    }

    /// <summary>Adjust pitch by a delta (from mouse Y movement).</summary>
    public void TweakPitch(float diff)
    {
        QuitGazeDownForAdjustment();
        QuitInFrontForAdjustment();
        Pitch = Math.Clamp(Pitch + diff, PitchLower, PitchUpper);
    }

    public void TweakGap(float diff)
    {
        QuitGazeDownForAdjustment();
        QuitInFrontForAdjustment();
        Distance = Math.Clamp(Distance + diff, GapLower, GapUpper);
    }

    public void ApplyCanonDefaultLens()
    {
        IsLookingDown = false;
        IsLookupMode = false;
        IsInHead = false;
        _markDirOwn = null;
        YawOffset = 0f;
        EyePtHeight = 1.5f;
        AssignBeholderShift(CanonDefaultBack, CanonDefaultUp);
    }

    public void ApplyCanonLeadPersonLens()
    {
        IsLookingDown = false;
        IsLookupMode = false;
        IsInHead = true;
        _markDirOwn = null;
        YawOffset = 0f;
        Distance = 0.18f;
        Pitch = 0f;
    }

    public void SwitchCanonGazeDownLens()
    {
        if (IsLookingDown)
        {
            ReinstateGazeDownLens();
            return;
        }
        PersistGazeDownLens();
        IsLookingDown = true;
        IsLookupMode = false;
        IsInHead = false;
        _markDirOwn = new Vector3(0f, 0.5f, -1.8f);
        AssignBeholderShift(2f, CanonDefaultUp);
    }

    public void SwitchCanonLookupMannerLens()
    {
        if (IsLookupMode)
        {
            ReinstateGazeDownLens();
            return;
        }
        if (!IsLookingDown)
            PersistGazeDownLens();
        IsLookingDown = true;
        IsLookupMode = true;
        IsInHead = false;
        _markDirOwn = new Vector3(0f, 0.5f, -1.8f);
        AssignBeholderShift(450f, CanonDefaultUp);
    }

    private void PersistGazeDownLens()
    {
        _storedGap = Distance;
        _storedPitch = Pitch;
        _storedYawShift = YawOffset;
        _storedMarkDirOwn = _markDirOwn;
        _storedInFront = IsInHead;
    }

    private void ReinstateGazeDownLens()
    {
        Distance = _storedGap;
        Pitch = _storedPitch;
        YawOffset = _storedYawShift;
        _markDirOwn = _storedMarkDirOwn;
        IsInHead = _storedInFront;
        IsLookingDown = false;
        IsLookupMode = false;
    }

    private void QuitGazeDownForAdjustment()
    {
        if (IsLookingDown)
            ReinstateGazeDownLens();
    }

    private void QuitInFrontForAdjustment()
    {
        if (!IsInHead)
            return;
        IsInHead = false;
        Distance = GapLower;
    }

    private void AssignBeholderShift(float back, float up)
    {
        Distance = MathF.Sqrt(back * back + up * up);
        Pitch = MathF.Atan2(up, back);
    }
}

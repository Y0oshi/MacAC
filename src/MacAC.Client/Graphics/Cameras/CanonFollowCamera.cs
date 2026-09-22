using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed partial class CanonFollowCamera : IClientCamera
{
    private const float CanonDefaultBack = 2.5f;

    private const float CanonDefaultUp = 0.75f;

    private const float CanonGazeDownBack = 2f;

    private const float CanonLookupBack = 450f;

    private const float CanonLeadPersonAhead = 0.18f;

    private const float CanonDepartFrontBack = 0.6f;

    private const float CanonDepartFrontUp = 0.5f;

    private const float CanonInFrontDirHop = 0.200000003f;

    private const float CanonInFrontDirThreshold = 0.800000012f;

    internal const float ShiftScalingPerAdjustment = 0.200000003f;

    internal const float ShiftAngleRadians = 8f * MathF.PI / 180f;

    internal const float FloorShiftLen = 0.5f;

    internal const float CeilingHorizontalModule = 10f;

    internal const float CeilingVerticalModule = 450f;

    internal const float FloorVerticalModule = -1.8f;

    public float Aspect { get; set; } = 16f / 9f;

    public float FovY { get; set; } = CanonFieldOfView.DefaultImposedFovY;

    public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;

    public float Distance { get; set; } = 2.61f;

    public float Pitch { get; set; } = 0.291f;

    public float YawShift { get; set; } = 0f;

    public float PivotHeight { get; set; } = 1.5f;

    private bool _storedInFront;

    private float _storedGap;

    private float _storedPitch;

    private float _storedYawShift;

    private Vector3? _markDirOwn;

    private Vector3? _storedMarkDirOwn;

    private const float SnapEpsilon = 0.000199999995f * 2f;

    private const float RotShutEpsilon = 0.000199999995f;

    private readonly Vector3[] _velLoop = new Vector3[5];

    private int _velTally;

    private Vector3 _soughtEyePt;

    private Vector3 _publishedEyePt;

    private Vector3 _dampedAhead = new(1f, 0f, 0f);

    private bool _initialised;

    // Mouse-filter state - shared by FilterMouseDelta entrypoint
    private float _previousPointerDiffX;

    private float _previousPointerDiffY;

    private float _previousSiftMomentSec;
}

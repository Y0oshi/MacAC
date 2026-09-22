using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed class ClientDollCamera : IClientCamera
{
    internal static readonly Vector3 CanonEyePt = new(0.12f, -2.4f, 0.88f);
    // Identity view orientation ⇒ look straight down +Y (no yaw/pitch). Target = Eye + (0,1,0).
    private static readonly Vector3 Target = new(0.12f, -1.4f, 0.88f);
    private static readonly Vector3 Up = Vector3.UnitZ;

    public float FovRadians { get; set; } = MathF.PI / 4f;

    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 50f;     // doll scene is small; 50 m is ample
    public float Aspect { get; set; } = 1f;

    public Matrix4x4 View =>
        Matrix4x4.CreateLookAt(CanonEyePt, Target, Up);

    public Matrix4x4 Projection
    {
        get
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, Aspect <= 0f ? 1f : Aspect, Near, Far);
        }
    }
}

internal sealed class DollViewRectCamera : IPrivateActorViewportCamera
{
    private readonly ClientDollCamera _cam = new();

    public Vector3 Eye => ClientDollCamera.CanonEyePt;
    public float FovRadians
    {
        get => _cam.FovRadians;
        set => _cam.FovRadians = value;
    }
    public float Near
    {
        get => _cam.Near;
        set => _cam.Near = value;
    }
    public float Far
    {
        get => _cam.Far;
        set => _cam.Far = value;
    }
    public float Aspect
    {
        get => _cam.Aspect;
        set => _cam.Aspect = value;
    }
    public Matrix4x4 View => _cam.View;
    public Matrix4x4 Projection => _cam.Projection;
}

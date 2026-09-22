using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed class GatewayTunnelCamera : IClientCamera
{
    public static readonly Vector3 CanonEyePt = new(0.24f, -2.7f, 0.88f);

    public float DirDeg { get; set; }
    public float FovRadians { get; set; } = MathF.PI / 4f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 4000f;
    public float Aspect { get; set; } = 1f;

    public void EmploySmartBboxFov(Matrix4x4 smartBboxProj)
    {
        float verticalScaling = smartBboxProj.M22;
        if (!float.IsFinite(verticalScaling) || verticalScaling <= 0f)
            return;

        float fov = 2f * MathF.Atan(1f / verticalScaling);
        if (float.IsFinite(fov) && fov > 0f && fov < MathF.PI)
            FovRadians = fov;

        float nearby = smartBboxProj.M33 != 0f
            ? smartBboxProj.M43 / smartBboxProj.M33
            : float.NaN;
        float faraway = smartBboxProj.M33 != -1f
            ? smartBboxProj.M43 / (smartBboxProj.M33 + 1f)
            : float.NaN;
        if (float.IsFinite(faraway) && faraway > 0f)
            Far = faraway;
        if (float.IsFinite(nearby) && nearby > 0f && nearby < Far)
            Near = nearby;
    }

    public Matrix4x4 View
    {
        get
        {
            float radians = DirDeg * (MathF.PI / 180f);
            Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);
            Vector3 ahead = Vector3.UnitY;
            Vector3 up = Vector3.Transform(Vector3.UnitZ, spin);
            return Matrix4x4.CreateLookAt(CanonEyePt, CanonEyePt + ahead, up);
        }
    }

    public Matrix4x4 Projection
    {
        get
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(
        FovRadians,
        Aspect <= 0f ? 1f : Aspect,
        Near,
        Far);
        }
    }
}

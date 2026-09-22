// src/MacAC.Client/Rendering/FlyCamera.cs
using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed class ClientFlyCamera : IClientCamera
{
    public Vector3 Position { get; set; } = new(96, 96, 150);
    public float Yaw { get; set; } = MathF.PI / 2f;  // facing +Y
    public float Pitch { get; set; } = -0.3f;         // looking slightly down
    public float FovY { get; set; } = CanonFieldOfView.DefaultImposedFovY;
    public float Aspect { get; set; } = 16f / 9f;

    public float RelocatePace { get; set; } = 12f;
    public float BoostPace { get; set; } = 50f;
    public float MouseSensitivity { get; set; } = 0.003f;

    private const float PitchThreshold = 1.5533f;  // ~89 degrees

    public Matrix4x4 View
    {
        get
        {
            Vector3 ahead = Forward();
            return Matrix4x4.CreateLookAt(Position, Position + ahead, Vector3.UnitZ);
        }
    }

    public Matrix4x4 Projection
        => Matrix4x4.CreatePerspectiveFieldOfView(FovY, Aspect, 0.1f, 5000f);

    public void Update(double dt, bool w, bool a, bool s, bool d, bool up, bool down, bool boost = false)
    {
        float pace = boost ? BoostPace : RelocatePace;
        float hop = (float)(pace * dt);

        Vector3 planarAhead = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
        Vector3 right = new Vector3(MathF.Sin(Yaw), -MathF.Cos(Yaw), 0f);

        if (w) Position += planarAhead * hop;
        if (s) Position -= planarAhead * hop;
        if (a) Position -= right * hop;
        if (d) Position += right * hop;
        if (up) Position += Vector3.UnitZ * hop;
        if (down) Position -= Vector3.UnitZ * hop;
    }

    public void Look(float diffX, float diffY)
    {
        Yaw -= diffX * MouseSensitivity;
        Pitch = Math.Clamp(Pitch - diffY * MouseSensitivity, -PitchThreshold, PitchThreshold);
    }

    private Vector3 Forward()
    {
        float cp = MathF.Cos(Pitch);
        return new Vector3(
            cp * MathF.Cos(Yaw),
            cp * MathF.Sin(Yaw),
            MathF.Sin(Pitch));
    }
}

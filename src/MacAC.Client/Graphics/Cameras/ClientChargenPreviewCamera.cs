using System.Numerics;
using MacAC.Mechanics.Genesis;

namespace MacAC.Client.Graphics;

public sealed class ClientChargenPreviewCamera(uint lineageIdent = 0u) : IClientCamera
{
    private static readonly Vector3 Up = Vector3.UnitZ;

    private Vector3 _eyePt = LocateDefaultEyePt(lineageIdent);

    public Vector3 Eye
    {
        get => _eyePt;
        set => _eyePt = value;
    }

    public void AssignLineage(uint lineageIdent) => _eyePt = LocateDefaultEyePt(lineageIdent);

    public static Vector3 LocateDefaultEyePt(uint lineageIdent)
    {
        return lineageIdent switch
        {
            (uint)GenesisHeritage.Olthoi => new Vector3(0f, -1.85000002f, 1.85000002f),
            (uint)GenesisHeritage.OlthoiAcid => new Vector3(0f, -3.04999995f, 2.75f),
            (uint)GenesisHeritage.Tumerok => new Vector3(0f, -0.850000024f, 1.64999998f),
            _ => new Vector3(0f, -0.550000012f, 1.64999998f),
        };
    }

    public static Vector3 LocateZoomedOutEyePt(uint lineageIdent)
    {
        return lineageIdent switch
        {
            (uint)GenesisHeritage.Olthoi => new Vector3(0f, -3.79999995f, 1.14999998f),
            (uint)GenesisHeritage.OlthoiAcid => new Vector3(0f, -5.69999981f, 1.64999998f),
            _ => new Vector3(0f, -2.5f, 0.95f),
        };
    }

    public const float SpinSecsPerRevolution = 3.0f;

    public const float ZoomTweenIntervalSecs = 0.6f;

    public float FovRadians { get; set; } = MathF.PI / 4f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 50f;
    public float Aspect { get; set; } = 1f;

    public Matrix4x4 View =>
        Matrix4x4.CreateLookAt(_eyePt, _eyePt + Vector3.UnitY, Up);

    public Matrix4x4 Projection
    {
        get
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, Aspect <= 0f ? 1f : Aspect, Near, Far);
        }
    }
}

internal sealed class ChargenPreviewViewRectCamera : IPrivateActorViewportCamera
{
    private readonly ClientChargenPreviewCamera _cam;

    public ChargenPreviewViewRectCamera(uint lineageIdent = 0u)
    {
        _cam = new ClientChargenPreviewCamera(lineageIdent);
    }

    public ChargenPreviewViewRectCamera(ClientChargenPreviewCamera camera)
    {
        _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public void ApplyLineage(uint lineageIdent) => _cam.AssignLineage(lineageIdent);

    public Vector3 Eye => _cam.Eye;
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

using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

public sealed class WarpViewPlaneDriver
{
    public const float ChangeoverLensPlaneGap = 0.001f;

    private float _playLensPlaneGap = 1f;
    private PortalAnimState _phase = PortalAnimState.Off;
    private readonly MirrorOverrideCamera _projCam = new();

    public bool Enabled { get; private set; }
    public float LatestLensPlaneGap { get; private set; } = 1f;

    public void Begin(Matrix4x4 playProj)
    {
        float gap = playProj.M22;
        if (!float.IsFinite(gap) || gap <= 0f)
            gap = 1f;

        _playLensPlaneGap = gap;
        LatestLensPlaneGap = gap;
        _phase = PortalAnimState.Off;
        Enabled = false;
    }

    public void Update(PortalAnimFrame capture)
    {
        _phase = capture.State;
        switch (capture.State)
        {
            case PortalAnimState.WorldFadeOut:
            case PortalAnimState.TunnelFadeIn:
            case PortalAnimState.TunnelFadeOut:
            case PortalAnimState.WorldFadeIn:
                Enabled = true;
                LatestLensPlaneGap = Lerp(
                    _playLensPlaneGap,
                    ChangeoverLensPlaneGap,
                    capture.ViewPlaneBlend);
                break;

            case PortalAnimState.Tunnel:
                if (Enabled)
                    LatestLensPlaneGap = _playLensPlaneGap;
                break;

            case PortalAnimState.TunnelContinue:
            case PortalAnimState.Off:
            default:
                Enabled = false;
                LatestLensPlaneGap = _playLensPlaneGap;
                break;
        }
    }

    public void Reset()
    {
        _phase = PortalAnimState.Off;
        Enabled = false;
        LatestLensPlaneGap = _playLensPlaneGap;
    }

    public Matrix4x4 Apply(Matrix4x4 baseProj)
    {
        if (!Enabled)
            return baseProj;

        float aspect = baseProj.M11 != 0f
            ? baseProj.M22 / baseProj.M11
            : 1f;
        float faraway = baseProj.M33 != -1f
            ? baseProj.M43 / (baseProj.M33 + 1f)
            : 5000f;

        if (!float.IsFinite(aspect) || aspect <= 0f)
            aspect = 1f;
        if (!float.IsFinite(faraway) || faraway <= 0.1f)
            faraway = 5000f;

        float gap = MathF.Max(LatestLensPlaneGap, ChangeoverLensPlaneGap);
        float fov = 2f * MathF.Atan(1f / gap);
        float nearby = RealmChangeoverNearbyPlane(gap);
        if (nearby >= faraway)
            nearby = MathF.Min(0.1f, faraway * 0.5f);

        return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, nearby, faraway);
    }

    public IClientCamera ImposeTo(IClientCamera baseCam)
    {
        ArgumentNullException.ThrowIfNull(baseCam);
        _projCam.Update(baseCam, Apply(baseCam.Projection));
        return _projCam;
    }

    private float RealmChangeoverNearbyPlane(float gap)
    {
        return _phase is PortalAnimState.WorldFadeOut or PortalAnimState.WorldFadeIn
            && gap < 0.4f
            ? MathF.Max(0.0001f, gap * 0.25f)
            : MathF.Max(0.1f, gap * 0.25f);
    }

    private static float Lerp(float from, float to, float quantity) =>
        from + (to - from) * Math.Clamp(quantity, 0f, 1f);

    private sealed class MirrorOverrideCamera : IClientCamera
    {
        private IClientCamera _src = null!;

        public Matrix4x4 View => _src.View;
        public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;
        public float Aspect
        {
            get => _src.Aspect;
            set => _src.Aspect = value;
        }

        public void Update(IClientCamera src, Matrix4x4 proj)
        {
            _src = src;
            Projection = proj;
        }
    }
}

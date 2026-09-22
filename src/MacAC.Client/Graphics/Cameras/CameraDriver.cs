using MacAC.Mechanics.Drawing;

namespace MacAC.Client.Graphics;

public sealed class CameraDriver
{
    internal readonly record struct CameraLedger(
        int ModeCode,
        FollowCamera? Chase,
        CanonFollowCamera? RetailChase);

    public ClientOrbitCamera Orbit { get; }
    public ClientFlyCamera Fly { get; }
    public FollowCamera? Chase { get; private set; }
    public CanonFollowCamera? RetailChase { get; private set; }

    public IClientCamera Active
    {
        get
        {
            if (_mode == Manner.Fly) return Fly;
            if (_mode == Manner.Chase)
            {
                if (CameraTelemetry.UseCanonPursueCam && RetailChase is not null)
                    return RetailChase;
                if (Chase is not null) return Chase;
            }
            return Orbit;
        }
    }

    public bool IsFlyManner => _mode == Manner.Fly;
    public bool IsPursueManner => _mode == Manner.Chase;

    public event Action<bool>? ModeChanged;

    private enum Manner { Orbit, Fly, Chase }
    private Manner _mode = Manner.Orbit;

    public float PlayFovRadians { get; private set; } = CanonFieldOfView.DefaultPlayFovRadians;

    private float _aspect = 16f / 9f;

    public CameraDriver(ClientOrbitCamera orbit, ClientFlyCamera fly)
    {
        Orbit = orbit;
        Fly = fly;
        ImposeProj();
    }

    public void FlipFly()
    {
        _mode = IsFlyManner ? Manner.Orbit : Manner.Fly;
        ModeChanged?.Invoke(IsFlyManner);
    }

    public void EnterChaseMode(FollowCamera legacy, CanonFollowCamera canon)
    {
        Chase = legacy;
        RetailChase = canon;
        ImposeProj();
        _mode = Manner.Chase;
        ModeChanged?.Invoke(IsPursueManner);
    }

    public void QuitPursueManner()
    {
        Chase = null;
        RetailChase = null;
        if (_mode == Manner.Orbit)
            return;
        _mode = Manner.Orbit;
        ModeChanged?.Invoke(false);
    }

    public void SetAspect(float aspect)
    {
        _aspect = aspect;
        ImposeProj();
    }

    public void AssignPlayFov(float playFovRadians)
    {
        PlayFovRadians = playFovRadians;
        ImposeProj();
    }

    internal CameraLedger GrabPhase() =>
        new((int)_mode, Chase, RetailChase);

    internal void RestoreState(CameraLedger state)
    {
        if (state.ModeCode is < ((int)Manner.Orbit) or > ((int)Manner.Chase))
            throw new ArgumentOutOfRangeException(nameof(state));

        Chase = state.Chase;
        RetailChase = state.RetailChase;
        ImposeProj();
        _mode = (Manner)state.ModeCode;
        ModeChanged?.Invoke(IsFlyManner || IsPursueManner);
    }

    private void ImposeProj()
    {
        bool fovApproved = CanonFieldOfView.TryImposedVerticalFov(
            PlayFovRadians, _aspect, out float fovY);

        Orbit.Aspect = _aspect;
        Fly.Aspect = _aspect;
        if (Chase is { } pursue) pursue.Aspect = _aspect;
        if (RetailChase is { } canonPursue) canonPursue.Aspect = _aspect;

        if (!fovApproved)
            return;
        Orbit.FovY = fovY;
        Fly.FovY = fovY;
        if (Chase is { } pursueFov) pursueFov.FovY = fovY;
        if (RetailChase is { } canonPursueFov) canonPursueFov.FovY = fovY;
    }
}

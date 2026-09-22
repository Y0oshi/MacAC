using MacAC.Client.Controls;
using MacAC.Client.Fighting;
using MacAC.Client.Pulse;
using MacAC.Mechanics.Drawing;

namespace MacAC.Client.Graphics;

internal sealed class CameraFrameDriver(
    CameraDriver camera,
    IFeedGrabOrigin capture,
    ICameraCycleFeedOrigin input,
    IAvatarDisplayEngine player,
    IFollowCameraSource chase,
    CanonAvatarFrameDriver localFrame,
    IOnlineSpatialReconcilePhase spatialReconciler,
    IFightingCameraTargetSource combatTarget) : ICameraCycleStage
{
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IFeedGrabOrigin _grab = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly ICameraCycleFeedOrigin _feed = input ?? throw new ArgumentNullException(nameof(input));
    private readonly IAvatarDisplayEngine _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly IFollowCameraSource _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
    private readonly CanonAvatarFrameDriver _ownCycle = localFrame ?? throw new ArgumentNullException(nameof(localFrame));
    private readonly IOnlineSpatialReconcilePhase _spatialReconciler = spatialReconciler
            ?? throw new ArgumentNullException(nameof(spatialReconciler));
    private readonly IFightingCameraTargetSource _fightingMark = combatTarget ?? throw new ArgumentNullException(nameof(combatTarget));

    public void Tick(PulseFrameTiming timing)
    {
        if (_grab.DevToolsWantGrabKeyboard || !_feed.IsOnHand)
            return;

        if (_cam.IsFlyManner)
        {
            var feed = _feed.GrabFly();
            _cam.Fly.Update(
                timing.SimulationDeltaSeconds,
                feed.Forward,
                feed.Left,
                feed.Backward,
                feed.Right,
                feed.Up,
                feed.Down,
                feed.Boost);
            return;
        }

        var driver = _avatar.Controller;
        var legacy = _pursue.Legacy;
        var canon = _pursue.Retail;
        if (!_avatar.CanPresentAvatar || driver is null || legacy is null)
            return;

        if (CameraTelemetry.UseCanonPursueCam && canon is not null)
        {
            var feed = _feed.GrabPursueAdjustment();
            float adjustment = CameraTelemetry.CamAdjustmentPace
                * timing.SimulationDeltaSecondsSingle;
            if (feed.ZoomIn)
                canon.TuneGap(-adjustment);
            if (feed.ZoomOut)
                canon.TuneGap(+adjustment);
            if (feed.Raise)
                canon.TunePitch(+adjustment);
            if (feed.Lower)
                canon.TunePitch(-adjustment);
            if (feed.RotateLeft)
                canon.TuneYaw(+adjustment);
            if (feed.RotateRight)
                canon.TuneYaw(-adjustment);
        }
        else
        {
            var feed = _feed.GrabPursueAdjustment();
            float adjustment = CameraTelemetry.CamAdjustmentPace
                * timing.SimulationDeltaSecondsSingle;
            if (feed.ZoomIn)
                legacy.TweakGap(-adjustment);
            if (feed.ZoomOut)
                legacy.TweakGap(+adjustment);
            if (feed.Raise)
                legacy.TweakPitch(+adjustment * 0.02f);
            if (feed.Lower)
                legacy.TweakPitch(-adjustment * 0.02f);
            if (feed.RotateLeft)
                legacy.YawOffset += adjustment * 0.02f;
            if (feed.RotateRight)
                legacy.YawOffset -= adjustment * 0.02f;
        }

        if (!_ownCycle.TryGetPresentationAfterNetwork(out var avatarCycle))
            return;

        if (!avatarCycle.AdvancedBeforeNetwork)
            _spatialReconciler.Reconcile();

        var outcome = avatarCycle.Movement;
        float camDt = driver.PresentedDiffSecs;
        legacy.Update(
            outcome.RenderPosition,
            driver.Yaw,
            isOnTerrain: outcome.IsOnGround,
            dt: camDt);

        canon?.Update(
            outcome.RenderPosition,
            driver.Yaw,
            avatarVel: driver.CorpusVel,
            isOnTerrain: outcome.IsOnGround,
            linkPlaneNorm: driver.ContactPlane.Normal,
            dt: camDt,
            chamberIdent: driver.CellId,
            selfActorIdent: driver.OwnEntityId,
            followedMarkPt: _fightingMark.FetchFollowedMarkPt());
    }
}

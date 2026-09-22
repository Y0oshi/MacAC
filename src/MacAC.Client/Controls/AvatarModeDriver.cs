using System.Numerics;
using MacAC.Assets;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Kinetics;
using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Controls;

internal sealed class AvatarModeDriver(
    AvatarModeLedger mode,
    SimAvatarLocomotionLedger controllerSlot,
    AvatarKineticsHostSlot hostSlot,
    FollowCameraInputLedger chase,
    CameraDriver camera,
    KineticEngine physics,
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    OnlineRealmOriginLedger origin,
    IOnlineActorMotionEngineWiring motionBindings,
    IDatAccess dats,
    object datLock,
    OnlineContactAssetHerald collisionAssets,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animations,
    AvatarMotionDriver animation,
    OwnAvatarShadeSyncer shadow,
    IAvatarApproachCompletionLifetimeOwner approachCompletions,
    IAvatarWarpInputLifetime input,
    IOnlineInRealmSource session,
    LocomotionTruthTelemetryDriver movementDiagnostics,
    SimLocomotionSkillLedger skills,
    IViewRectAspectOrigin viewport) :
    IAvatarWarpModeOperations,
    IDevToolsAvatarModeTarget
{
    private readonly AvatarModeLedger _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly SimAvatarLocomotionLedger _controllerSlot = controllerSlot ?? throw new ArgumentNullException(nameof(controllerSlot));
    private readonly AvatarKineticsHostSlot _hubSocket = hostSlot ?? throw new ArgumentNullException(nameof(hostSlot));
    private readonly FollowCameraInputLedger _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly IOnlineActorMotionEngineWiring _locomotionMappings = motionBindings ?? throw new ArgumentNullException(nameof(motionBindings));
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));
    private readonly OnlineContactAssetHerald _impactHoldings = collisionAssets ??
            throw new ArgumentNullException(nameof(collisionAssets));
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _anims = animations ?? throw new ArgumentNullException(nameof(animations));
    private readonly AvatarMotionDriver _anim = animation ?? throw new ArgumentNullException(nameof(animation));
    private readonly OwnAvatarShadeSyncer _shade = shadow ?? throw new ArgumentNullException(nameof(shadow));
    private readonly IAvatarApproachCompletionLifetimeOwner _approachCompletions = approachCompletions
            ?? throw new ArgumentNullException(nameof(approachCompletions));
    private readonly IAvatarWarpInputLifetime _feed = input ?? throw new ArgumentNullException(nameof(input));
    private readonly IOnlineInRealmSource _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly LocomotionTruthTelemetryDriver _travelTelemetry = movementDiagnostics
            ?? throw new ArgumentNullException(nameof(movementDiagnostics));
    private readonly SimLocomotionSkillLedger _aptitudes = skills ?? throw new ArgumentNullException(nameof(skills));
    private readonly IViewRectAspectOrigin _viewRect = viewport ?? throw new ArgumentNullException(nameof(viewport));
    private AvatarMannerAutoListing? _autoListing;
    private IAvatarApproachCompletionSink? _approachLifespan;

    public AvatarLocomotionDriver? Driver => _controllerSlot.Controller;
    public Matrix4x4 Projection => _cam.Active.Projection;

    public void AttachAutoListing(AvatarMannerAutoListing autoListing)
    {
        ArgumentNullException.ThrowIfNull(autoListing);
        if (_autoListing is not null)
            throw new InvalidOperationException("Player-mode auto-entry is by now bound");
        _autoListing = autoListing;
    }

    public void Toggle()
    {
        if (!_session.IsInWorld)
            return;

        _autoListing?.Cancel();
        if (_mode.IsPlayerMode)
            Exit();
        else
            _ = TryEnter("Tab");
    }

    public void JoinFromAutoListing()
    {
        if (TryEnter("auto-entry"))
        {
            Console.WriteLine(
                $"live: auto-entered player mode for 0x{_identity.SrvOid:X8}");
        }
    }

    public bool TryJoinGatewaySpace()
    {
        if (Driver is null && !TryEnter("teleport"))
            return false;

        if (Driver is not { CanPerformOnlineTravel: true } driver)
            return false;

        driver.State = AvatarPhase.PortalSpace;
        _autoListing?.Cancel();
        return true;
    }

    public bool TryJoinGatewaySpaceForSignin()
    {
        return !_mode.IsPlayerMode && !TryEnter("login") ? false : TryJoinGatewaySpace();
    }

    public void EnterWorld()
    {
        if (Driver is { } driver)
            driver.State = AvatarPhase.InWorld;
    }

    public void Exit()
    {
        List<Exception> misses = new List<Exception>();
        try { _feed.EndMouseLook(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _cam.QuitPursueManner(); }
        catch (Exception problem) { misses.Add(problem); }
        try { RetireApproachLifespan(); }
        catch (Exception problem) { misses.Add(problem); }
        _mode.IsPlayerMode = false;
        _hubSocket.Host = null;
        _pursue.Legacy = null;
        _pursue.Retail = null;

        if (misses.Count is not 0)
            throw new AggregateException("Player-mode exit was incomplete", misses);
    }

    public void FlipFlyOrPursue()
    {
        _autoListing?.Cancel();
        if (_cam.IsFlyManner
            && _mode.IsPlayerMode
            && _pursue.Legacy is { } legacy)
        {
            _pursue.Retail ??= new CanonFollowCamera
            {
                Aspect = legacy.Aspect,
                ImpactSensor = new KineticsCameraContactProbe(_physics),
            };
            _cam.EnterChaseMode(legacy, _pursue.Retail);
            return;
        }

        _cam.FlipFly();
    }

    public void RestartSess()
    {
        _autoListing?.Cancel();
        List<Exception> misses = new List<Exception>();
        try { _cam.QuitPursueManner(); }
        catch (Exception problem) { misses.Add(problem); }
        try { RetireApproachLifespan(); }
        catch (Exception problem) { misses.Add(problem); }
        _mode.RestartSession();
        _hubSocket.Host = null;
        _pursue.Legacy = null;
        _pursue.Retail = null;
        try { _travelTelemetry.ResetSess(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _shade.ResetSession(); }
        catch (Exception problem) { misses.Add(problem); }

        if (misses.Count is not 0)
            throw new AggregateException("Player-mode session reset was incomplete", misses);
    }

    private bool TryEnter(string loggingTag)
    {
        uint avatarOid = _identity.SrvOid;
        if (!_onlineActors.TryFetchRealmActor(
                avatarOid,
                out RealmActor? avatarActor))
        {
            Console.WriteLine(
                $"live: {loggingTag} - player entity 0x{avatarOid:X8} not found yet");
            return false;
        }

        if (!_onlineActors.TryFetchRecord(avatarOid, out OnlineActorRecord avatarCapture))
        {
            Console.WriteLine(
                $"live: {loggingTag} - player record 0x{avatarOid:X8} not found yet");
            return false;
        }

        if (_controllerSlot.Controller is not { } publishedDriver
            || !publishedDriver.IsRuntimePublished
            || avatarCapture.PhysicsHost is not ActorKineticsHarbor)
        {
            Console.WriteLine(
                $"live: {loggingTag} - Runtime first-entry controller for "
                + $"0x{avatarOid:X8} not committed yet");
            return false;
        }

        BuildControllerAndCamera(
            loggingTag,
            avatarOid,
            avatarActor,
            avatarCapture);
        return true;
    }

    private void BuildControllerAndCamera(
        string loggingTag,
        uint avatarOid,
        RealmActor avatarActor,
        OnlineActorRecord avatarCapture)
    {
        if (_controllerSlot.Controller is not { } driver
            || !driver.IsRuntimePublished)
        {
            throw new InvalidOperationException(
                $"Player mode ({loggingTag}) needs the Runtime-published "
                + "local movement controller; the first-entry conductor has "
                + "not committed it yet");
        }
        if (avatarCapture.PhysicsHost is not ActorKineticsHarbor avatarHub)
        {
            throw new InvalidOperationException(
                $"Player mode ({loggingTag}) needs the Runtime-committed "
                + "local physics host");
        }

        var approachLifespan =
            _approachCompletions.CommenceDriverLifespan();
        bool lifespanSealed = false;
        bool camAttempted = false;
        bool shadeAttempted = false;
        var precedingCam = _cam.GrabPhase();
        var precedingShade = _shade.Capture();
        try
        {
            if (driver.MoveTo is { } relocateTo)
            {
                relocateTo.RelocateToDone = problem =>
                {
                    if (problem == WeenieProblem.None)
                        approachLifespan.BroadcastNaturalWrapUp();
                    else
                        approachLifespan.BroadcastAbort(problem);
                };
                relocateTo.RelocateToCancelled = problem =>
                    approachLifespan.BroadcastAbort(problem);
            }

            if (_anims.TryGetValue(avatarActor.Id, out OnlineActorMotionLedger? anim)
                && anim.Sequencer is { } scheduler)
            {
                driver.FastenCycleVelAccessor(() => scheduler.LatestVel);
                driver.ObjectScaling = anim.Scale;
                driver.FastenAnimTrunkLocomotionSrc(
                    _anim.ProgressTrunk,
                    _anim.GrabTaps);
                driver.Locomotion.RemoveLinkAnimations =
                    scheduler.Manager.HandleEnterWorld;
                driver.Locomotion.BootstrapLocomotionCharts =
                    scheduler.Manager.BootstrapCondition;
                driver.Locomotion.VerifyForCompletedMotions =
                    scheduler.Manager.VerifyForFinishedMotions;
                driver.Locomotion.DefaultSink =
                    new MotionTableDispatchTap(scheduler);
                scheduler.Manager.HandleEnterWorld();
                driver.Locomotion.ProcessExitWorld();
            }

            FollowCamera legacyCam = new FollowCamera { Aspect = _viewRect.Aspect };
            CanonFollowCamera canonCam = new CanonFollowCamera
            {
                Aspect = _viewRect.Aspect,
                ImpactSensor = new KineticsCameraContactProbe(_physics),
            };
            camAttempted = true;
            _cam.EnterChaseMode(legacyCam, canonCam);

            shadeAttempted = true;
            _shade.SyncPose(
                avatarActor,
                driver.Position,
                avatarActor.Rotation,
                driver.CellId,
                force: true);

            _hubSocket.Host = avatarHub;
            _pursue.Legacy = legacyCam;
            _pursue.Retail = canonCam;
            _mode.IsPlayerMode = true;
            _mode.PursueMannerEverEntered = true;
            _approachLifespan = approachLifespan;
            lifespanSealed = true;
        }
        catch (Exception error)
        {
            List<Exception> misses = new List<Exception> { error };
            if (shadeAttempted)
            {
                try { _shade.Restore(avatarActor, precedingShade); }
                catch (Exception tidyProblem) { misses.Add(tidyProblem); }
            }
            if (camAttempted)
            {
                try { _cam.RestoreState(precedingCam); }
                catch (Exception tidyProblem) { misses.Add(tidyProblem); }
            }

            _mode.IsPlayerMode = false;
            _hubSocket.Host = null;
            _pursue.Legacy = null;
            _pursue.Retail = null;

            if (misses.Count is not 1)
                throw new AggregateException(
                    "Player-mode entry failed and rollback was incomplete",
                    misses);
            throw;
        }
        finally
        {
            if (!lifespanSealed)
                _approachCompletions.RetireDriverLifespan(approachLifespan);
        }
    }

    private void RetireApproachLifespan()
    {
        if (_approachLifespan is not { } lifespan)
            return;
        _approachLifespan = null;
        _approachCompletions.RetireDriverLifespan(lifespan);
    }

}

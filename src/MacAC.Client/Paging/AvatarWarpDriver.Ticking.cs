using MacAC.Mechanics.Realm;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Client.Paging;

internal sealed partial class AvatarWarpDriver
{

    public void Tick(float diffSecs)
    {
        HurlIfDestroyed();
        if (_passage.IsSignoutEngaged)
        {
            PulseSignout(diffSecs);
            return;
        }

        TryEngageQueuedExhibit();
        TryAimApprovedDest();
        if (!_passage.IsWarpEngaged)
        {
            PulseSigninExhibit(diffSecs);
            return;
        }

        long gen = _lifespanGen;
        ushort series = _passage.EngagedWarpSeries;
        if (!_mode.TryJoinGatewaySpace()
            || !IsLatestLifespan(gen, series)
            || _mode.Driver is null)

            return;

        bool haveDest = _queuedChamber is not 0u;
        bool originPrimed = !_paging.IsRecenterQueued;
        bool blobPrimed = haveDest
            && originPrimed
            && _worldReveal.Evaluate(_queuedChamber).IsPrimed;
        if (!IsLatestLifespan(gen, series))
            return;

        bool stancePrimed = blobPrimed && TryProceedGatewaySeal(series);
        if (!IsLatestLifespan(gen, series))
            return;

        if (haveDest && !stancePrimed)
            _gripSecs += diffSecs;
        _exhibit.ApplyPauseCue(
            haveDest
            && !stancePrimed
            && _worldReveal.WatchPause(
                TimeSpan.FromSeconds(_gripSecs)));

        var (_, signals) = _exhibit.Tick(diffSecs, stancePrimed);
        if (!IsLatestLifespan(gen, series))
            return;

        foreach (PortalAnimEvent warpSignal in signals)
        {
            switch (warpSignal)
            {
                case PortalAnimEvent.Place:
                    if (!_stanceSealed)
                        return;
                    if (!_worldReveal.CanPlaceGatewayDest(
                            _queuedUnveilGen, series, _queuedChamber))
                    {
                        return;
                    }
                    _stance.Place(_queuedSpin);
                    if (!IsLatestLifespan(gen, series))
                        return;
                    _worldReveal.WatchMaterialized(
                        _queuedUnveilGen,
                        series,
                        _queuedChamber);
                    if (!IsLatestLifespan(gen, series))
                        return;
                    break;
                case PortalAnimEvent.PlayEnterSound:
                    _exhibit.PlayJoinCue();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    break;
                case PortalAnimEvent.EnterTunnel:
                    _exhibit.EnterTunnel();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    break;
                case PortalAnimEvent.PlayExitSound:
                    _exhibit.QuitTunnel();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    _worldReveal.UnveilRealmViewRect();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    _exhibit.PlayQuitCue();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    break;
                case PortalAnimEvent.FireLoginComplete:
                    _mode.EnterWorld();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    _session.TransmitSigninDone();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    _worldReveal.Complete();
                    if (!IsLatestLifespan(gen, series))
                        return;
                    RestartPassage(wipeSess: false);
                    return;
                default:
                    break;
            }
        }

        _exhibit.PulseTunnel(diffSecs);
    }
    private void PulseSignout(float diffSecs)
    {
        long gen = _lifespanGen;

        if (_passage.SignoutJuncture is SimLogoutStage.Requested
                or SimLogoutStage.PresentationActive
            && _signout.IsToonLogOffConfirmed)

            _passage.AcknowledgeSignoutConfirmed();

        switch (_passage.SignoutJuncture)
        {
            case SimLogoutStage.Requested:
                if (_passage.ProgressSignoutGrip(diffSecs))
                {
                    _exhibit.CommenceSignout(_mode.Projection);
                    if (_lifespanGen != gen)
                        return;
                    PumpSignoutExhibit(0f, gen);
                }
                return;
            case SimLogoutStage.PresentationActive:
                PumpSignoutExhibit(diffSecs, gen);
                return;
            case SimLogoutStage.Confirmed:
                ConcludeSignoutHandoff(gen);
                return;
            default:
                return;
        }
    }

    private void PulseSigninExhibit(float diffSecs)
    {
        TryEngageSigninExhibit();

        var capture = _passage.Snapshot;
        bool unveilEngaged = capture.Kind == SimPortalKind.Login
            && capture.Generation is not 0
            && !capture.Completed
            && !capture.Cancelled;
        if (!unveilEngaged || _signinUnveilGen != capture.Generation)
        {
            if (_signinExhibitEngaged)
            {
                _signinUnveilGen = 0;
                _signinExhibitEngaged = false;
                _signinMannerEntered = false;
                _signinTunnelLoaded = false;
                _signinGripSecs = 0f;
                _exhibit.Reset();
            }
            else if (_signinTunnelLoaded)
            {
                PulseLoadedSigninTunnel(diffSecs);
            }
            return;
        }

        if (!_signinExhibitEngaged)
            return;

        long gen = _lifespanGen;
        long unveilGen = capture.Generation;
        uint destChamber = capture.Readiness.DestinationCell;

        bool originPrimed = !_paging.IsRecenterQueued;
        bool realmPrimed = _signinMannerEntered
            && _signinStanceFinished
            && originPrimed
            && _worldReveal.Evaluate(destChamber).IsPrimed;
        if (!IsLatestSigninLifespan(gen, unveilGen))
            return;

        if (!realmPrimed)
            _signinGripSecs += diffSecs;
        _exhibit.ApplyPauseCue(
            !realmPrimed
            && _worldReveal.WatchPause(
                TimeSpan.FromSeconds(_signinGripSecs)));

        var (_, signals) = _exhibit.Tick(diffSecs, realmPrimed);
        if (!IsLatestSigninLifespan(gen, unveilGen))
            return;

        foreach (PortalAnimEvent warpSignal in signals)
        {
            switch (warpSignal)
            {
                case PortalAnimEvent.PlayEnterSound:
                    Console.WriteLine(
                        "live: login portal-space enter cue "
                        + "(Sound_UI_EnterPortal)");
                    _exhibit.PlayJoinCue();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    break;
                case PortalAnimEvent.EnterTunnel:
                    _exhibit.EnterTunnel();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    break;
                case PortalAnimEvent.Place:
                    _worldReveal.WatchSigninMaterialized(unveilGen);
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    break;
                case PortalAnimEvent.PlayExitSound:
                    _exhibit.QuitTunnel();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    _worldReveal.UnveilRealmViewRect();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    _exhibit.PlayQuitCue();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    break;
                case PortalAnimEvent.FireLoginComplete:
                    _mode.EnterWorld();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    _session.TransmitSigninDone();
                    if (!IsLatestSigninLifespan(gen, unveilGen))
                        return;
                    _worldReveal.Complete();
                    _signinUnveilGen = 0;
                    _signinExhibitEngaged = false;
                    _signinMannerEntered = false;
                    _signinGripSecs = 0f;
                    Console.WriteLine(
                        "live: login portal-space presentation complete");
                    return;
                default:
                    break;
            }
        }

        _exhibit.PulseTunnel(diffSecs);
    }

    private void PulseLoadedSigninTunnel(float diffSecs)
    {
        var lifecycle =
            _signinLifecycle.PickLifecycle;
        if (lifecycle is not (
            SimToonPickLifespan.EnteringWorld
            or SimToonPickLifespan.InWorld))
        {
            _signinTunnelLoaded = false;
            _signinGripSecs = 0f;
            _exhibit.Reset();
            Console.WriteLine(
                $"live: login tunnel disarmed (lifecycle={lifecycle})");
            return;
        }

        long gen = _lifespanGen;
        _signinGripSecs += diffSecs;
        var (_, signals) = _exhibit.Tick(diffSecs, realmPrimed: false);
        if (_lifespanGen != gen || !_signinTunnelLoaded)
            return;
        if (!HandleLoadedSigninTunnelSignals(signals, gen))
            return;
        _exhibit.PulseTunnel(diffSecs);
    }
}

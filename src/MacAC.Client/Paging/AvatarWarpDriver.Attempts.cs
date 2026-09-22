using MacAC.Mechanics.Kinetics;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.Paging;

internal sealed partial class AvatarWarpDriver
{
    public bool TryReqSignout()
    {
        HurlIfDestroyed();
        if (_passage.IsSignoutEngaged
            || _passage.IsWarpEngaged
            || _passage.HasQueuedWarpBegin
            || _signinExhibitEngaged
            || _signinTunnelLoaded)

            return false;

        _signoutPagingSunsetReadied = false;

        if (!_passage.TryCommenceSignoutReq(_signout.IsOwnAvatarKiller))
            return false;

        long gen = _lifespanGen;
        if (!_signout.CommenceToonLogOff())
        {
            if (_lifespanGen == gen)
                _passage.AbortSignoutReq();
            return false;
        }
        if (_lifespanGen != gen)
            return true;

        _feed.EndMouseLook();
        Console.WriteLine("live: character logoff requested");
        return true;
    }

    private bool TryProceedGatewaySeal(ushort series)
    {
        if (_stanceSealed)
            return true;

        if (_expectingPostponedWake)
        {
            if (_approvedLocusSteer.QueuedTally is not 0)
                return false;
            _expectingPostponedWake = false;
            if (_approvedLocusSteer.TryAbsorbGatewaySeal(
                    _queuedUnveilGen, series))
            {
                _stanceSealed = true;
                return true;
            }
        }

        if (!_worldReveal.CanPlaceGatewayDest(
                _queuedUnveilGen,
                series,
                _queuedChamber))
        {
            KineticTelemetry.TraceWarp(
                "REFUSED", _queuedChamber, "cause=stale-reveal");
            KineticTelemetry.TraceOwnWarpArrival(
                cause: "stale-reveal",
                stanceCondition: "Refused",
                gatewayGen: _queuedUnveilGen,
                warpSeries: series,
                destChamber: _queuedChamber,
                settledChamber: 0u,
                tapRearRan: false,
                leashLoaded: false,
                autorunCancelled: false);
            return false;
        }

        var condition =
            TryPerformCanonGatewayStanceCore(series);
        switch (condition)
        {
            case SimGrantedPositionExecutionStatus.Committed:
                _stanceSealed = true;
                return true;
            case SimGrantedPositionExecutionStatus.DeferredCell:
                _expectingPostponedWake = true;
                return false;
            default:
                return false;
        }
    }

    private SimGrantedPositionExecutionStatus
        TryPerformCanonGatewayStanceCore(ushort series)
    {
        System.Diagnostics.Debug.Assert(
            !_hasQueuedDest
                || _queuedDest.TeleportSequence == series,
            "The transit's active sequence and the Aim-time destination's "
            + "own sequence must never diverge (A9)");
        if (!_hasQueuedDest
            || !_passage.TryEnrollHubProj(
                _queuedUnveilGen,
                _queuedChamber,
                out SimRealmHarborMirrorTicket hubTicket))
        {
            KineticTelemetry.TraceWarp(
                "REFUSED", _queuedChamber, "cause=host-token-unavailable");
            KineticTelemetry.TraceOwnWarpArrival(
                cause: "host-token-unavailable",
                stanceCondition: "Refused",
                gatewayGen: _queuedUnveilGen,
                warpSeries: series,
                destChamber: _queuedChamber,
                settledChamber: 0u,
                tapRearRan: false,
                leashLoaded: false,
                autorunCancelled: false);
            return SimGrantedPositionExecutionStatus.Rejected;
        }

        var dest = _queuedDest;
        var gateway = new SimPortalPlacementAuthority(
            Present: true,
            RevealGeneration: _queuedUnveilGen,
            TeleportSequence: dest.TeleportSequence,
            Projection: hubTicket);
        return _approvedLocusSteer.TryPerformApprovedGatewayArrival(
            dest,
            gateway);
    }

    private void TryEngageQueuedExhibit()
    {
        if (!_passage.HasQueuedWarpBegin)
            return;

        long gen = _lifespanGen;
        if (!_mode.TryJoinGatewaySpace()
            || _lifespanGen != gen
            || !_passage.HasQueuedWarpBegin)

            return;

        _gripSecs = 0f;
        _exhibit.Begin(_mode.Projection);
        if (_lifespanGen != gen
            || !_passage.HasQueuedWarpBegin)

            return;

        if (!_passage.EngageQueuedWarp())
            return;
        TryAimApprovedDest();
        Console.WriteLine(
            $"live: teleport presentation started "
            + $"(seq={_passage.EngagedWarpSeries})");
    }

    private void TryEngageSigninExhibit()
    {
        if (_passage.HasQueuedWarpBegin || _passage.IsWarpEngaged)
            return;

        var capture = _passage.Snapshot;
        if (capture.Kind != SimPortalKind.Login
            || capture.Generation is 0
            || capture.Completed
            || capture.Cancelled
            || (_signinUnveilGen == capture.Generation && _signinMannerEntered))

            return;

        long gen = _lifespanGen;
        if (_signinTunnelLoaded)
        {
            _signinTunnelLoaded = false;
            _signinUnveilGen = capture.Generation;
            _signinExhibitEngaged = true;
            _signinMannerEntered = false;
        }

        if (!_mode.TryJoinGatewaySpaceForSignin()
            || _lifespanGen != gen
            || _mode.Driver is not { CanPerformOnlineTravel: true })

            return;

        var latest = _passage.Snapshot;
        if (latest.Kind != SimPortalKind.Login
            || latest.Generation != capture.Generation
            || latest.Completed
            || latest.Cancelled
            || _passage.HasQueuedWarpBegin
            || _passage.IsWarpEngaged)

            return;

        bool adoptedLoadedTunnel = _signinExhibitEngaged
            && _signinUnveilGen == capture.Generation;
        _signinUnveilGen = capture.Generation;
        _signinExhibitEngaged = true;
        _signinMannerEntered = true;
        if (!adoptedLoadedTunnel)
        {
            _signinGripSecs = 0f;
            _exhibit.Begin(_mode.Projection);
        }
        Console.WriteLine(
            $"live: login portal-space presentation started "
            + $"(gen={capture.Generation} "
            + $"cell=0x{capture.Readiness.DestinationCell:X8} "
            + $"adoptedArmedTunnel={(adoptedLoadedTunnel ? 1 : 0)})");
    }

    private void TryAimApprovedDest()
    {
        if (!_passage.TryFetchApprovedWarpDest(
                out SimWarpDestination dest))

            return;

        long gen = _lifespanGen;
        ushort series = _passage.EngagedWarpSeries;
        var driver = _mode.Driver;
        if (driver is null)
        {
            if (!_mode.TryJoinGatewaySpace()
                || !IsLatestLifespan(gen, series))

                return;

            driver = _mode.Driver;
            if (driver is null)
                return;
        }

        if (!AimDest(dest, driver, gen, series))
            return;
    }
}

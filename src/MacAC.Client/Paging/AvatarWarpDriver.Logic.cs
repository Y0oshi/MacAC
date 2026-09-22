using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim;

namespace MacAC.Client.Paging;

internal sealed partial class AvatarWarpDriver
{
    public bool IsActive => _passage.IsWarpEngaged;

    public bool IsPortalViewRectVisible => _exhibit.IsGatewayViewportVisible;

    public void OfferDest(
        SimWarpDestination dest,
        bool warpStampAdvanced)
    {
        HurlIfDestroyed();
        _passage.OfferWarpDest(
            dest,
            warpStampAdvanced);
        TryAimApprovedDest();
    }

    public void ReqSignout()
    {
        if (!TryReqSignout())
            Console.WriteLine("live: character logoff request refused");
    }

    public uint EngagedDestinationCell
    {
        get
        {
            if (_passage.IsWarpEngaged)
                return _queuedChamber;
            if (_signinExhibitEngaged)
            {
                var capture = _passage.Snapshot;
                if (capture.Kind == SimPortalKind.Login
                    && capture.Generation == _signinUnveilGen)

                    return capture.Readiness.DestinationCell;
            }
            return 0u;
        }
    }

    public void OnWarpBegun(uint series)
    {
        HurlIfDestroyed();
        if (_passage.IsSignoutEngaged)
        {
            Console.WriteLine(
                $"live: teleport start ignored during logout (seq={series})");
            return;
        }
        ushort warpSeries = (ushort)series;
        if (!_arbiter.IsFreshBegin(warpSeries)
            || !_passage.CanFifoWarpBegin(warpSeries))

            return;

        long observedGen = _lifespanGen;
        _feed.EndMouseLook();
        if (_lifespanGen != observedGen)
            return;

        long restartGen = RestartPassage(wipeSess: false);
        if (_lifespanGen != restartGen)
            return;

        if (!_passage.TryFifoWarpBegin(warpSeries))
            return;
        TryEngageQueuedExhibit();
        Console.WriteLine($"live: teleport queued (seq={series})");
    }

    public void OnOwnAvatarLeadListingFinished()
    {
        HurlIfDestroyed();
        _signinStanceFinished = true;
    }

    public void ArmSigninTunnel()
    {
        HurlIfDestroyed();
        if (_signinTunnelLoaded
            || _signinExhibitEngaged
            || _passage.IsWarpEngaged
            || _passage.HasQueuedWarpBegin)

            return;

        long gen = _lifespanGen;
        _exhibit.Begin(_mode.Projection);
        if (_lifespanGen != gen)
            return;

        var (_, signals) = _exhibit.Tick(0f, realmPrimed: false);
        if (_lifespanGen != gen)
            return;
        if (!HandleLoadedSigninTunnelSignals(signals, gen))
            return;

        _signinTunnelLoaded = true;
        _signinGripSecs = 0f;
        Console.WriteLine("live: login tunnel armed at enter click");
    }

    public void ResetSession()
    {
        HurlIfDestroyed();
        RestartPassage(
            wipeSess: true,
            restartCanonPassage: true);
    }

    public void RestartGenExhibit()
    {
        HurlIfDestroyed();
        RestartPassage(
            wipeSess: true,
            restartCanonPassage: false);
    }

    public Matrix4x4 ApplyViewPlane(Matrix4x4 proj) =>
        _exhibit.ApplyViewPlane(proj);

    public IClientCamera ApplyViewPlane(IClientCamera cam) =>
        _exhibit.ApplyViewPlane(cam);

    public void SketchGatewayViewRect(int width, int height, Matrix4x4 proj) =>
        _exhibit.PaintGatewayViewRect(width, height, proj);

    public void Dispose()
    {
        if (_destroyed)
            return;
        _exhibit.Dispose();
        _destroyed = true;
    }

    private bool IsLatestSigninLifespan(
        long lifespanGen,
        long unveilGen)
    {
        return _lifespanGen == lifespanGen
        && !_passage.IsWarpEngaged
        && _signinUnveilGen == unveilGen
        && _passage.Snapshot.Generation == unveilGen;
    }

    private bool IsLatestLifespan(long gen, ushort series)
    {
        return _lifespanGen == gen
        && _passage.IsWarpEngaged
        && _passage.EngagedWarpSeries == series;
    }

    private bool HandleLoadedSigninTunnelSignals(
        IReadOnlyList<PortalAnimEvent> signals,
        long gen)
    {
        foreach (PortalAnimEvent warpSignal in signals)
        {
            switch (warpSignal)
            {
                case PortalAnimEvent.PlayEnterSound:
                    Console.WriteLine(
                        "live: login portal-space enter cue "
                        + "(Sound_UI_EnterPortal)");
                    _exhibit.PlayJoinCue();
                    if (_lifespanGen != gen)
                        return false;
                    break;
                case PortalAnimEvent.EnterTunnel:
                    _exhibit.EnterTunnel();
                    if (_lifespanGen != gen)
                        return false;
                    break;
                default:
                    break;
            }
        }

        return true;
    }

    private void PumpSignoutExhibit(float diffSecs, long gen)
    {
        var (_, signals) = _exhibit.Tick(diffSecs, realmPrimed: false);
        if (_lifespanGen != gen)
            return;

        foreach (PortalAnimEvent warpSignal in signals)
        {
            switch (warpSignal)
            {
                case PortalAnimEvent.PlayEnterSound:
                    Console.WriteLine(
                        "live: logout portal-space enter cue "
                        + "(Sound_UI_EnterPortal)");
                    _exhibit.PlayJoinCue();
                    if (_lifespanGen != gen)
                        return;
                    break;
                case PortalAnimEvent.EnterTunnel:
                    _exhibit.EnterTunnel();
                    if (_lifespanGen != gen)
                        return;
                    break;
                default:
                    break;
            }
        }

        _exhibit.PulseTunnel(diffSecs);
    }

    private bool AimDest(
        SimWarpDestination dest,
        AvatarLocomotionDriver driver,
        long gen,
        ushort series)
    {
        Locus locus = dest.Position;
        int lbX = (int)((locus.ObjCellId >> 24) & 0xFFu);
        int lbY = (int)((locus.ObjCellId >> 16) & 0xFFu);
        uint pagingOriginLbIdent = PagingRegion.PackLbIdent(
            _paging.MiddleX,
            _paging.MiddleY);
        Vector3 origin = new Vector3(
            (lbX - _paging.MiddleX) * 192f,
            (lbY - _paging.MiddleY) * 192f,
            0f);
        Vector3 translated = locus.Frame.Origin + origin;

        WarpLandblockTransition changeover = WarpLandblockTransition.Classify(
            driver.CellId,
            locus.ObjCellId,
            pagingOriginLbIdent);
        int formerX = (int)((changeover.SourceLandblockId >> 24) & 0xFFu);
        int formerY = (int)((changeover.SourceLandblockId >> 16) & 0xFFu);

        Console.WriteLine(
            $"live: teleport arrival - old lb=({formerX},{formerY}) "
            + $"new lb=({lbX},{lbY}) "
            + $"dist={Vector3.Distance(translated, driver.Position):F1}");

        if (!_worldReveal.TryCommenceGateway(
                series,
                locus.ObjCellId,
                out long unveilGen))

            return false;
        _queuedUnveilGen = unveilGen;
        if (!IsLatestLifespan(gen, series))
            return false;

        if (changeover.EditsPagingMiddle)
        {
            bool isSealedDungeon = _paging.IsSealedDungeon(
                locus.ObjCellId);
            if (!IsLatestLifespan(gen, series))
                return false;

            _paging.CommenceRecenter(
                lbX,
                lbY,
                isSealedDungeon);
            if (!IsLatestLifespan(gen, series))
                return false;
        }

        _queuedSpin = locus.Frame.Orientation;
        _queuedChamber = locus.ObjCellId;
        _queuedDest = dest;
        _hasQueuedDest = true;
        _gripSecs = 0f;
        KineticTelemetry.TraceWarp(
            "AIM",
            locus.ObjCellId,
            $"seq={dest.TeleportSequence} lb={lbX},{lbY} "
            + $"indoor={((locus.ObjCellId & 0xFFFFu) >= 0x0100u)} "
            + $"playerCross={changeover.CrossesLb} "
            + $"centerChange={changeover.EditsPagingMiddle}");
        return true;
    }

    private void HurlIfDestroyed() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);

    private void ConcludeSignoutHandoff(long gen)
    {
        if (!_paging.RestartRecenter(sessEnding: true)
            || _lifespanGen != gen)

            return;

        _signoutPagingSunsetReadied = true;
        if (!_passage.ConcludeSignout() || _lifespanGen != gen)
        {
            _signoutPagingSunsetReadied = false;
            return;
        }

        Console.WriteLine(
            "live: logout confirmed - returning to character select");
        if (_signout.ConcludeToonLogOff())

            return;

        _signoutPagingSunsetReadied = false;

        if (_lifespanGen == gen)
        {
            Console.Error.WriteLine(
                "live: return-to-character-select refused - retiring the "
                + "logout presentation");
            _exhibit.Reset();
        }
    }

    private long RestartPassage(
        bool wipeSess,
        bool restartCanonPassage = false)
    {
        bool pagingSunsetReadied = wipeSess
            && _signoutPagingSunsetReadied;
        if (wipeSess)
            _signoutPagingSunsetReadied = false;

        long gen = checked(++_lifespanGen);

        _queuedChamber = 0u;
        _queuedSpin = Quaternion.Identity;
        _queuedUnveilGen = 0;
        _queuedDest = default;
        _hasQueuedDest = false;
        _stanceSealed = false;
        _expectingPostponedWake = false;
        _gripSecs = 0f;
        _signinUnveilGen = 0;
        _signinExhibitEngaged = false;
        _signinMannerEntered = false;
        _signinTunnelLoaded = false;
        _signinGripSecs = 0f;
        if (wipeSess)
            _signinStanceFinished = false;

        if (!pagingSunsetReadied)
            _paging.RestartRecenter(wipeSess);
        if (_lifespanGen != gen)
            return gen;

        if (wipeSess)
        {
            if (restartCanonPassage)
                _worldReveal.ResetSession();
            else
                _worldReveal.RestartHubSess();
        }
        else
        {
            _passage.FinishWarp();
            _worldReveal.Cancel();
        }
        if (_lifespanGen != gen)
            return gen;

        _exhibit.Reset();
        return _lifespanGen != gen ? gen : gen;
    }
}

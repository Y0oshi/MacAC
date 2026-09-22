using MacAC.Sim;
using MacAC.Sim.Realm;

namespace MacAC.Client.Paging;

internal sealed partial class RealmRevealMarshal
{
    public SimPortalCapture Snapshot => _passage.Snapshot;

    public int GatewayMaterializationTally =>
        _passage.Snapshot.PortalMaterializationCount;

    public bool PauseCueShown => _passage.Snapshot.WaitCueShown;

    public SimRealmCrossingHoldingCapture Ownership =>
        _passage.Ownership;

    public long CommenceSignin(uint destinationCell)
    {
        if (destinationCell is 0u)
            throw new ArgumentOutOfRangeException(nameof(destinationCell));

        WithdrawHubForSubstitute();
        RealmEpochQuiescenceEdge stillnessRim =
            _stillness?.GrabCommence() ?? default;
        _readiness.Begin();
        long gen = _passage.CommenceSigninUnveil(destinationCell);
        _timing?.Begin(
            "login",
            gen,
            destinationCell,
            _readiness.NeededPane(destinationCell));
        CommenceHubLifespan(
            gen,
            destinationCell,
            stillnessRim);
        return gen;
    }

    public bool TryCommenceGateway(
        ushort warpSeries,
        uint destinationCell,
        out long gen)
    {
        if (destinationCell is 0u)
            throw new ArgumentOutOfRangeException(nameof(destinationCell));
        if (!_passage.CanCommenceGatewayUnveil(
                warpSeries,
                destinationCell))
        {
            gen = 0;
            return false;
        }

        WithdrawHubForSubstitute();
        RealmEpochQuiescenceEdge stillnessRim =
            _stillness?.GrabCommence() ?? default;
        if (!_passage.TryCommenceGatewayUnveil(
                warpSeries,
                destinationCell,
                out gen))

            return false;

        _readiness.Begin();
        _timing?.Begin(
            "portal",
            gen,
            destinationCell,
            _readiness.NeededPane(destinationCell));
        CommenceHubLifespan(
            gen,
            destinationCell,
            stillnessRim);
        return true;
    }

    public RealmRevealReadinessCapture ReadyAndEvaluate(uint destChamber)
    {
        _readiness.Prepare(destChamber);
        return Evaluate(destChamber);
    }

    public RealmRevealReadinessCapture Evaluate(uint destChamber)
    {
        ReattemptQueuedHubJob();
        var capture = _readiness.Evaluate(destChamber);
        SettleDestReservationRadius(capture);
        var gateway = _passage.Snapshot;
        _timing?.Observe(capture, gateway);
        if (gateway.Generation is not 0)
        {
            _passage.AcknowledgeDestReadiness(
                new SimDestinationFitness(
                    gateway.Generation,
                    capture.DestinationCell,
                    capture.IsIndoor,
                    capture.IsUnhydratable,
                    capture.RequiredRenderRadius,
                    capture.IsRenderNeighborhoodReady,
                    capture.AreCompositeTexturesReady,
                    capture.IsCollisionReady));
        }
        return capture;
    }

    public bool CanPlaceGatewayDest(
        long gen,
        ushort warpSeries,
        uint destChamber)
    {
        return _passage.CanPlaceGatewayDestination(
            gen,
            warpSeries,
            destChamber);
    }

    public bool WatchMaterialized(
        long gen,
        ushort warpSeries,
        uint destChamber)
    {
        bool acknowledged = _passage.AcknowledgeGatewayMaterialized(
            gen,
            warpSeries,
            destChamber);
        ReattemptQueuedHubJob();
        return acknowledged;
    }

    public bool WatchSigninMaterialized(long gen)
    {
        bool acknowledged =
            _passage.AcknowledgeSigninMaterialized(gen);
        ReattemptQueuedHubJob();
        return acknowledged;
    }

    public void WatchRealmViewRectShown()
    {
        ReattemptQueuedHubJob();
        long gen = _passage.Snapshot.Generation;
        if (gen is not 0)
            _passage.AcknowledgeRealmViewRectShown(gen);
    }

    public bool WatchPause(TimeSpan passed)
    {
        long gen = _passage.Snapshot.Generation;
        return gen is not 0
            && _passage.NotePause(gen, passed);
    }

    public void UnveilRealmViewRect()
    {
        HostMirror? hub = SeekLatestHubProj();
        if (hub is null)
            return;

        _passage.DemandDestReservationFree(hub.Token);
        ReattemptQueuedHubJob();
    }

    public void Complete()
    {
        long gen = _passage.Snapshot.Generation;
        if (gen is 0)
            return;

        _passage.Complete(gen);
        ReattemptQueuedHubJob();
    }

    public void Cancel()
    {
        long gen = _passage.Snapshot.Generation;
        if (gen is 0)
            return;

        _passage.Cancel(gen);
        ReattemptQueuedHubJob();
    }

    public void ResetSession()
    {
        RestartHubSess();
        _passage.ResetSession();
    }

    internal void RestartHubSess()
    {
        long gen = _passage.Snapshot.Generation;
        if (gen is not 0)
            _passage.Cancel(gen);
        ReattemptQueuedHubJob();
    }

    internal void ReattemptQueuedHubJob()
    {
        _hubReattemptAsked = true;
        if (_hubReattemptEngaged)
            return;

        _hubReattemptEngaged = true;
        try
        {
            do
            {
                _hubReattemptAsked = false;
                int ordinal = 0;
                while (ordinal < _hubProjections.Count)
                {
                    HostMirror hub = _hubProjections[ordinal];
                    EmptyHubProj(hub);
                    if (ordinal < _hubProjections.Count
                        && ReferenceEquals(
                            _hubProjections[ordinal],
                            hub))

                        ++ordinal;
                }
            }
            while (_hubReattemptAsked);
        }
        finally
        {
            _hubReattemptEngaged = false;
        }
    }

    private void CommenceHubLifespan(
        long gen,
        uint destChamber,
        in RealmEpochQuiescenceEdge stillnessRim)
    {
        if (!_passage.TryEnrollHubProj(
                gen,
                destChamber,
                out SimRealmHarborMirrorTicket ticket))
        {
            throw new InvalidOperationException(
                "Runtime rejected the exact reveal host projection");
        }

        _hubProjections.Add(new HostMirror(
            ticket,
            _readiness.NeededRasterizeRadius(destChamber),
            stillnessRim));
        ReattemptQueuedHubJob();
    }

    private void ConcludeHubEnrollment(HostMirror hub)
    {
        if (!hub.StillnessSealed)
        {
            _stillness?.SealCommence(hub.StillnessRim);
            hub.StillnessSealed = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .ProjectionRegistered))

            return;
        if (!hub.PagingRegistered)
        {
            _paging?.OpenDestReservation(
                hub.Token.Generation,
                hub.Token.DestinationCell,
                hub.NeededRasterizeRadius);
            hub.PagingRegistered = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .ProjectionRegistered))

            return;
        if (!hub.RasterizeAssetListRegistered)
        {
            _rasterizeAssetList?.CommenceDestUnveil(
                hub.Token.Generation);
            hub.RasterizeAssetListRegistered = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .ProjectionRegistered))

            return;

        Acknowledge(
            hub,
            SimRealmHarborAckStage.ProjectionRegistered);
    }

    private void ConcludeSimulationFree(HostMirror hub)
    {
        if (!hub.SimulationFreeProjected)
        {
            _stillness?.WatchReleased();
            hub.SimulationFreeProjected = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .SimulationReleaseProjected))

            return;

        Acknowledge(
            hub,
            SimRealmHarborAckStage
                .SimulationReleaseProjected);
    }

    private void ConcludeDestReservationFree(HostMirror hub)
    {
        if (!hub.PagingReleased)
        {
            if (hub.PagingRegistered)
            {
                _paging?.ConcludeDestReservation(
                    hub.Token.Generation);
            }
            hub.PagingReleased = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .DestinationReservationReleased))

            return;
        if (!hub.RasterizeAssetListReleased)
        {
            if (hub.RasterizeAssetListRegistered)
            {
                _rasterizeAssetList?.FinishDestUnveil(
                    hub.Token.Generation);
            }
            hub.RasterizeAssetListReleased = true;
        }
        if (!IsJunctureQueued(
                hub,
                SimRealmHarborAckStage
                    .DestinationReservationReleased))

            return;

        Acknowledge(
            hub,
            SimRealmHarborAckStage
                .DestinationReservationReleased);
    }

    private void EmptyHubProj(HostMirror hub)
    {
        if (!TryFetchHubProj(hub, out var proj))
            return;

        var queued =
            proj.PendingAcknowledgements;
        if ((queued & SimRealmHarborAckStage
                .ProjectionRegistered) != 0)

            ConcludeHubEnrollment(hub);

        if (!TryFetchHubProj(hub, out proj))
            return;
        queued = proj.PendingAcknowledgements;
        if ((queued & SimRealmHarborAckStage
                .SimulationReleaseProjected) != 0)

            ConcludeSimulationFree(hub);

        if (!TryFetchHubProj(hub, out proj))
            return;
        queued = proj.PendingAcknowledgements;
        if ((queued & SimRealmHarborAckStage
                .DestinationReservationReleased) != 0)

            ConcludeDestReservationFree(hub);

        if (!TryFetchHubProj(hub, out proj))
            return;
        if ((proj.PendingAcknowledgements
                & SimRealmHarborAckStage.TerminalProjected)
            == 0)

            return;

        Acknowledge(
            hub,
            SimRealmHarborAckStage.TerminalProjected);
        DropHubProj(hub);
    }

    private void SettleDestReservationRadius(
        in RealmRevealReadinessCapture capture)
    {
        if (_paging is null || !capture.HasDest)
            return;

        HostMirror? hub = SeekLatestHubProj();
        if (hub is null
            || !hub.PagingRegistered
            || hub.PagingReleased
            || hub.Token.DestinationCell != capture.DestinationCell
            || hub.NeededRasterizeRadius == capture.RequiredRenderRadius)

            return;

        _paging.ConcludeDestReservation(hub.Token.Generation);
        _paging.OpenDestReservation(
            hub.Token.Generation,
            hub.Token.DestinationCell,
            capture.RequiredRenderRadius);
        hub.NeededRasterizeRadius = capture.RequiredRenderRadius;
    }

    private void WithdrawHubForSubstitute()
    {
        HostMirror? hub = SeekLatestHubProj();
        if (hub is null)
            return;

        if (!_passage.CommenceHubProjSupersession(hub.Token))
        {
            throw new InvalidOperationException(
                "Runtime rejected reveal-host supersession");
        }
        ReattemptQueuedHubJob();
    }

    private HostMirror? SeekLatestHubProj()
    {
        long gen = _passage.Snapshot.Generation;
        for (int idx = 0; idx < _hubProjections.Count; ++idx)
        {
            HostMirror contender = _hubProjections[idx];
            if (contender.Token.Generation == gen)
                return contender;
        }

        return null;
    }

    private bool TryFetchHubProj(
        HostMirror hub,
        out SimRealmHarborMirrorCapture proj)
    {
        if (_passage.TryFetchHubProj(hub.Token, out proj))
            return true;

        DropHubProj(hub);
        return false;
    }

    private bool IsJunctureQueued(
        HostMirror hub,
        SimRealmHarborAckStage juncture)
    {
        return _passage.TryFetchHubProj(
            hub.Token,
            out SimRealmHarborMirrorCapture proj)
        && (proj.PendingAcknowledgements & juncture) != 0;
    }

    private void DropHubProj(HostMirror hub)
    {
        for (int idx = 0; idx < _hubProjections.Count; ++idx)
        {
            if (!ReferenceEquals(_hubProjections[idx], hub))
                continue;

            _hubProjections.RemoveAt(idx);
            return;
        }
    }

    private void Acknowledge(
        HostMirror hub,
        SimRealmHarborAckStage juncture)
    {
        if (!_passage.AcknowledgeHubProj(
                new SimRealmHarborAck(
                    hub.Token,
                    juncture)))
        {
            throw new InvalidOperationException(
                $"Runtime rejected reveal host acknowledgement {juncture} "
                + $"for generation {hub.Token.Generation}.");
        }
    }
}

using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger
{
    public long CommenceSigninUnveil(uint destChamber) =>
        OpenUnveil(SimPortalKind.Login, destChamber);

    public bool CanCommenceGatewayUnveil(ushort warpSeries, uint destChamber)
    {
        if (!_teleport.Active || warpSeries != _teleport.EngagedSeries || !_teleport.Held)
            return false;

        var pinned = _teleport.Destination;
        return pinned.TeleportSequence == warpSeries
            && pinned.CellId is not 0u
            && pinned.CellId == destChamber;
    }

    public bool TryCommenceGatewayUnveil(
        ushort warpSeries,
        uint destChamber,
        out long gen)
    {
        gen = 0;
        if (!CanCommenceGatewayUnveil(warpSeries, destChamber))
        {
            TraceRejected(
                "portal-begin-without-destination",
                $"active={_teleport.Active} "
                + $"expectedSequence={_teleport.EngagedSeries} "
                + $"actualSequence={warpSeries} "
                + $"accepted={_teleport.Held}");
            return false;
        }

        gen = OpenUnveil(SimPortalKind.Portal, destChamber);
        _teleport.Held = false;
        _teleport.Destination = default;
        return true;
    }

    public bool AcknowledgeDestReadiness(in SimDestinationFitness acknowledgement)
    {
        if (!VetEngaged(acknowledgement.Generation, acknowledgement.DestinationCell, "readiness"))
            return false;
        if (_capture.Readiness.IsReady)
            return false;

        bool inside = IsInside(acknowledgement.DestinationCell);
        bool radiusFits = inside
            ? acknowledgement.RequiredRenderRadius is 0
            : acknowledgement.RequiredRenderRadius >= 1;
        if (acknowledgement.IsIndoor != inside || !radiusFits)
        {
            FailInvariant(
                "invalid-readiness-shape",
                $"indoor={acknowledgement.IsIndoor} "
                + $"expectedIndoor={inside} "
                + $"radius={acknowledgement.RequiredRenderRadius} "
                + $"expectedRadius={(inside ? "0" : ">=1")}");
            return false;
        }

        if (_capture.Readiness == acknowledgement)
            return false;

        _capture = _capture with { Readiness = acknowledgement };
        Trace("readiness", _capture);
        return true;
    }

    public bool AcknowledgeGatewayMaterialized(
        long gen,
        ushort warpSeries,
        uint destChamber)
    {
        if (!IsLatestGatewayDest(gen, warpSeries, destChamber))
        {
            TraceRejected(
                "materialized-portal-mismatch",
                $"generation={gen} "
                + $"sequence={warpSeries} "
                + $"cell=0x{destChamber:X8}");
            return false;
        }

        if (!MayMaterialize(gen, destChamber))
            return false;

        int tally = _capture.PortalMaterializationCount;
        if (_capture.Kind == SimPortalKind.Portal)
            tally = checked(tally + 1);
        Materialize(gen, tally);
        return true;
    }

    public bool AcknowledgeSigninMaterialized(long gen)
    {
        if (gen is 0
            || gen != _capture.Generation
            || _capture.Kind != SimPortalKind.Login)
        {
            TraceRejected(
                "materialized-login-mismatch",
                $"generation={gen} kind={_capture.Kind}");
            return false;
        }

        if (!MayMaterialize(gen, _capture.DestinationCell))
            return false;

        Materialize(gen, _capture.PortalMaterializationCount);
        return true;
    }

    public bool AcknowledgeRealmViewRectShown(long gen)
    {
        if (!VetGen(gen, "world-visible", allowFinished: true))
            return false;
        if (_capture.WorldViewportObserved)
            return false;
        if (!_capture.Readiness.IsReady)
        {
            FailInvariant("viewport-before-ready", null);
            return false;
        }

        _capture = _capture with { WorldViewportObserved = true };
        Trace("world-visible", _capture);
        return true;
    }

    public bool NotePause(long gen, TimeSpan passed)
    {
        if (!IsOnline(gen) || passed < CanonPauseCueDelay)
            return false;

        if (!_capture.WaitCueShown)
        {
            _capture = _capture with { WaitCueShown = true };
            SafeTrace(
                $"{Tag}wait-cue "
                + $"elapsedMs={passed.TotalMilliseconds:F0} "
                + Depict(_capture));
        }

        return true;
    }

    public bool Complete(long gen)
    {
        if (!VetGen(gen, "complete", allowFinished: true))
            return false;
        if (_capture.Completed)
            return false;
        if (!_capture.Readiness.IsReady)
        {
            FailInvariant("complete-before-ready", null);
            return false;
        }
        if (_capture.Kind == SimPortalKind.Portal && !_capture.Materialized)
        {
            FailInvariant("portal-complete-before-materialized", null);
            return false;
        }

        Close(gen, "complete", finished: true);
        return true;
    }

    public bool Cancel(long gen)
    {
        if (!IsOnline(gen))
            return false;

        Close(gen, "cancel", finished: false);
        return true;
    }

    // Starts a fresh reveal generation, superseding whatever was in flight
    private long OpenUnveil(SimPortalKind kind, uint destinationCell)
    {
        if (kind is SimPortalKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (destinationCell is 0u)
            throw new ArgumentOutOfRangeException(nameof(destinationCell));

        if (_capture.Generation is not 0
            && !_capture.Cancelled
            && !_capture.WorldViewportObserved)

            Trace("superseded", _capture);
        if (_mirrors.TryGetValue(_capture.Generation, out Mirror? stale))
            stale.Supersede();

        long gen = checked(++_generations);
        _capture = new SimPortalCapture(
            gen,
            kind,
            new SimDestinationFitness(
                gen,
                destinationCell,
                IsIndoor: IsInside(destinationCell),
                IsUnhydratable: false,
                RequiredRenderRadius: 0,
                IsRenderNeighborhoodReady: false,
                AreCompositeTexturesReady: false,
                IsCollisionReady: false),
            Materialized: false,
            Completed: false,
            Cancelled: false,
            WorldViewportObserved: false,
            WorldSimulationAvailable: false,
            InvariantFailureCount: _capture.InvariantFailureCount,
            WaitCueShown: false,
            PortalMaterializationCount: _capture.PortalMaterializationCount);
        Trace("begin", _capture);
        return gen;
    }

    // The shared gate before either materialization edge
    private bool MayMaterialize(long gen, uint destChamber)
    {
        if (!VetEngaged(gen, destChamber, "materialized"))
            return false;
        if (_capture.Materialized)
            return false;
        if (_capture.Readiness.IsReady)
            return true;

        FailInvariant("materialized-before-ready", null);
        return false;
    }

    private void Materialize(long gen, int gatewayMaterializationTally)
    {
        _capture = _capture with
        {
            Materialized = true,
            WorldSimulationAvailable = true,
            PortalMaterializationCount = gatewayMaterializationTally,
        };
        if (_mirrors.TryGetValue(gen, out Mirror? mirror))
            mirror.Owe(SimRealmHarborAckStage.SimulationReleaseProjected);
        Trace("materialized", _capture);
    }

    // Terminates the reveal as completed or cancelled; either way the world simulation is released
    private void Close(long gen, string signalLabel, bool finished)
    {
        bool simulationWasOnHand = _capture.WorldSimulationAvailable;
        _capture = finished
            ? _capture with { Completed = true, WorldSimulationAvailable = true }
            : _capture with { Cancelled = true, WorldSimulationAvailable = true };
        if (_mirrors.TryGetValue(gen, out Mirror? mirror))
            mirror.Cease(demandSimulationFree: !simulationWasOnHand);
        Trace(signalLabel, _capture);
    }

    // True for the current, non-terminal generation
    private bool IsOnline(long gen)
    {
        return gen is not 0
        && gen == _capture.Generation
        && !_capture.Cancelled
        && !_capture.Completed;
    }

    private bool VetEngaged(long gen, uint destChamber, string signalLabel)
    {
        if (!VetGen(gen, signalLabel, allowFinished: false))
            return false;
        if (destChamber is 0u || destChamber != _capture.DestinationCell)
        {
            FailInvariant(
                $"{signalLabel}-destination-mismatch",
                $"expected=0x{_capture.DestinationCell:X8} "
                + $"actual=0x{destChamber:X8}");
            return false;
        }

        return true;
    }

    private bool VetGen(long gen, string signalLabel, bool allowFinished)
    {
        if (gen is 0 || gen != _capture.Generation)
        {
            TraceRejected(
                $"{signalLabel}-generation-mismatch",
                $"expected={_capture.Generation} actual={gen}");
            return false;
        }
        if (_capture.Cancelled || (!allowFinished && _capture.Completed))
        {
            TraceRejected(
                $"{signalLabel}-after-terminal",
                $"completed={_capture.Completed} "
                + $"cancelled={_capture.Cancelled}");
            return false;
        }

        return true;
    }
}

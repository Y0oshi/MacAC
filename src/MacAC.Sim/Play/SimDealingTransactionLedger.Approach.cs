using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public sealed partial class SimDealingTransactionLedger
{
    // One armed slot plus its token counter (tokens never come out as zero)
    private struct SimSlot<T> where T : struct
    {
        public T? Loaded;
        private ulong _previousTicket;

        public ulong UpcomingTicket()
        {
            ulong ticket = ++_previousTicket;
            return ticket is 0u ? ++_previousTicket : ticket;
        }
    }

    private SimSlot<SimPendingGrab> _grab;
    private SimSlot<SimPendingUse> _use;

    public bool HasQueuedLift => _grab.Loaded is not null;
    public bool HasQueuedUse => _use.Loaded is not null;

    public bool TryArmPostArrivalLift(
        uint srvOid,
        uint ownActorIdent,
        uint destVesselIdent,
        int stance,
        ulong queuedStanceTicket,
        SimDealingApproachTicket approachTicket,
        out SimPendingGrab queued)
    {
        Live();
        queued = default;
        if (srvOid is 0u || ownActorIdent is 0u || destVesselIdent is 0u || queuedStanceTicket is 0u || !approachTicket.IsValid)
            return false;
        if (_grab.Loaded is not null)
            return false;

        queued = new SimPendingGrab(_grab.UpcomingTicket(), srvOid, ownActorIdent, destVesselIdent, stance, queuedStanceTicket, approachTicket);
        _grab.Loaded = queued;
        Touch();
        return true;
    }

    public bool TryLocateApproachWrapUp(SimDealingApproachTicket approachTicket, bool natural, out SimPendingGrab queued)
    {
        return Disarm(ref _grab, grab => grab.ApproachToken == approachTicket, out queued) && natural;
    }

    public bool TryFetchQueuedLift(out SimPendingGrab queued)
    {
        queued = _grab.Loaded ?? default;
        return _grab.Loaded is not null;
    }

    public bool TryCancelPendingPickup(out SimPendingGrab queued) => Disarm(ref _grab, static _ => true, out queued);

    public bool TryCancelPendingPickup(uint srvOid, uint? ownActorIdent, out SimPendingGrab queued)
    {
        return Disarm(ref _grab, grab => grab.ServerGuid == srvOid && (ownActorIdent is not uint precise || grab.LocalEntityId == precise), out queued);
    }

    public bool TryRelayLift(SimPendingGrab lift, ISimDealingTransport conveyance, out uint series)
    {
        Live();
        ArgumentNullException.ThrowIfNull(conveyance);
        series = 0u;
        if (!conveyance.IsInRealm)
            return false;

        uint sent = 0u;
        bool dispatched = _satchel.TryRelay(
            PackRequestKind.Pickup,
            lift.ServerGuid,
            () => conveyance.TryTransmitLift(lift.ServerGuid, lift.DestinationContainerId, lift.Placement, out sent),
            lift.PendingPlacementToken);
        series = sent;
        if (dispatched)
            Touch();
        return dispatched;
    }

    public bool TryArmPostArrivalUse(
        uint srvOid,
        bool possessedByAvatar,
        bool useable,
        ItemUseHold? reservation,
        SimDealingApproachTicket approachTicket,
        out SimPendingUse queued)
    {
        Live();
        queued = default;
        if (srvOid is 0u || !approachTicket.IsValid)
            return false;
        if (_use.Loaded is not null)
            return false;

        queued = new SimPendingUse(_use.UpcomingTicket(), srvOid, possessedByAvatar, useable, reservation, approachTicket);
        _use.Loaded = queued;
        Touch();
        return true;
    }

    public bool TryLocateUseApproachWrapUp(SimDealingApproachTicket approachTicket, bool natural, out SimPendingUse queued)
    {
        return Disarm(ref _use, u => u.ApproachToken == approachTicket, out queued) && natural;
    }

    public bool TryFetchQueuedUse(out SimPendingUse queued)
    {
        queued = _use.Loaded ?? default;
        return _use.Loaded is not null;
    }

    public bool TryCancelPendingUse(out SimPendingUse queued) => Disarm(ref _use, static _ => true, out queued);

    public bool TryCancelPendingUse(uint srvOid, out SimPendingUse queued) =>
        Disarm(ref _use, u => u.ServerGuid == srvOid, out queued);

    // Takes the armed value out of a slot when fits accepts it
    private bool Disarm<T>(ref SimSlot<T> socket, Func<T, bool> fits, out T queued) where T : struct
    {
        Live();
        if (socket.Loaded is not { } loaded || !fits(loaded))
        {
            queued = default;
            return false;
        }
        socket.Loaded = null;
        queued = loaded;
        Touch();
        return true;
    }
}

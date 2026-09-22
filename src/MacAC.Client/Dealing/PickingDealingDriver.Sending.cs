using MacAC.Client.Shell;

namespace MacAC.Client.Dealing;

internal sealed partial class PickingDealingDriver
{
    public void TransmitUse(uint srvOid)
        => ReqUse(srvOid, reservation: null);

    public void TransmitLift(uint gearOid, uint destVesselIdent, int stance)
    {
        AbortQueuedApproach();
        if (!_conveyance.IsInRealm)
        {
            _toast?.Invoke("Not in world");
            AbortLiftExhibit(gearOid);
            return;
        }

        ulong queuedStanceTicket = _gearList.TryFetchQueuedBackpackStance(
            gearOid,
            out QueuedBackpackStance queuedStance)
                ? queuedStance.Token
                : 0u;
        if (!IsLatestLiftExhibit(
                gearOid,
                destVesselIdent,
                stance,
                queuedStanceTicket))

            return;

        if (_gearList.IsInLatestTerrainObject(gearOid)
            || (_ask.IsWieldedLocusPhase(gearOid)
                && _gearList.IsPossessedByAvatar(gearOid)))
        {
            SimPendingGrab contained = new SimPendingGrab(
                Token: 0u,
                gearOid,
                LocalEntityId: 0u,
                destVesselIdent,
                stance,
                queuedStanceTicket,
                ApproachToken: default);
            if (_transactions.TryRelayLift(
                    contained,
                    _conveyance,
                    out uint containedSeries))
            {
                Console.WriteLine(
                    $"[interaction] contained pickup item=0x{gearOid:X8} container=0x{destVesselIdent:X8} placement={stance} seq={containedSeries}");
            }
            else
            {
                AbortLiftExhibit(gearOid, queuedStanceTicket);
            }
            return;
        }

        if (!VetLiftMark(gearOid, unhideToast: true)
            || !_ask.TryFetchApproach(gearOid, out DealingApproach approach))
        {
            AbortLiftExhibit(gearOid, queuedStanceTicket);
            return;
        }

        if (approach.IsCloseRange)
        {
            bool loaded = false;
            bool begun = _movement.OpenApproach(
                approach,
                ticket =>
                {
                    loaded = _transactions.TryArmPostArrivalLift(
                        gearOid,
                        approach.Target.LocalEntityId,
                        destVesselIdent,
                        stance,
                        queuedStanceTicket,
                        new SimDealingApproachTicket(
                            ticket.ControllerLifetime,
                            ticket.ApproachGeneration),
                        out _);
                });
            if (!begun || !loaded)
            {
                if (_transactions.TryCancelPendingPickup(
                        gearOid,
                        approach.Target.LocalEntityId,
                        out SimPendingGrab cancelled))
                {
                    AbortLiftExhibit(
                        cancelled.ServerGuid,
                        cancelled.PendingPlacementToken);
                }
                else
                {
                    AbortLiftExhibit(gearOid, queuedStanceTicket);
                }
            }
            return;
        }

        _movement.OpenApproach(approach);
        if (!IsLatestLiftExhibit(
                gearOid,
                destVesselIdent,
                stance,
                queuedStanceTicket))

            return;
        SimPendingGrab immediate = new SimPendingGrab(
            Token: 0u,
            gearOid,
            approach.Target.LocalEntityId,
            destVesselIdent,
            stance,
            queuedStanceTicket,
            ApproachToken: default);
        if (_transactions.TryRelayLift(
                immediate,
                _conveyance,
                out uint series))
        {
            Console.WriteLine(
                $"[interaction] pickup item=0x{gearOid:X8} container=0x{destVesselIdent:X8} placement={stance} seq={series}");
        }
        else
        {
            AbortLiftExhibit(gearOid, queuedStanceTicket);
        }
    }
}

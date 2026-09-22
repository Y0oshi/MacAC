using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Dealing;

internal sealed class RealmSessionPickingDealingTransport(
    Func<RealmSession?> session)
    : ISimDealingTransport
{
    private readonly Func<RealmSession?> _session = session
        ?? throw new ArgumentNullException(nameof(session));

    public bool IsInRealm =>
        _session()?.LatestPhase == RealmSession.State.InWorld;

    public bool TryTransmitUse(uint srvOid, out uint series)
    {
        var online = _session();
        if (online?.LatestPhase != RealmSession.State.InWorld)
        {
            series = 0u;
            return false;
        }

        series = online.UpcomingPlayActSeries();
        online.TransmitPlayAct(InteractAsks.AssembleUse(series, srvOid));
        return true;
    }

    public bool TryTransmitLift(
        uint gearOid,
        uint destVesselIdent,
        int stance,
        out uint series)
    {
        var online = _session();
        if (online?.LatestPhase != RealmSession.State.InWorld)
        {
            series = 0u;
            return false;
        }

        series = online.UpcomingPlayActSeries();
        online.TransmitPlayAct(InteractAsks.AssembleChooseUp(
            series,
            gearOid,
            destVesselIdent,
            stance));
        return true;
    }
}

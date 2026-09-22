using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Play;

public interface ILocomotionTruthDiagnosticSink
{
    void OnOutgoing(string sort, uint series, LocomotionResult outcome, Vector3 wireLocus, uint wireChamberIdent, byte linkByte);

    void OnSrvEcho(RealmSession.MoverPositionUpdate refresh, Vector3 srvRealmLocus);

    void ResetSess();
}

public sealed class AvatarOutboundDriver
{
    private readonly ILocomotionTruthDiagnosticSink _diagnostic;

    internal AvatarOutboundDriver(ILocomotionTruthDiagnosticSink diagnostic)
    {
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public AvatarOutboundDriver(Action<string, uint, LocomotionResult, Vector3, uint, byte> probe)
    {
        _diagnostic = new DelegateLocomotionTruthDiagnosticSink(probe);
    }

    // The position as it goes on the wire: cell, origin and orientation
    private readonly record struct WireSpot(Locus Source, uint CellId, Vector3 Origin, Quaternion Rotation)
    {
        public static bool TryFrom(AvatarLocomotionDriver driver, out WireSpot spot)
        {
            if (!driver.TryFetchOutgoingLocus(out Locus locus))
            {
                spot = default;
                return false;
            }
            spot = new WireSpot(locus, locus.ObjCellId, locus.Frame.Origin, locus.Frame.Orientation);
            return true;
        }
    }

    private static byte Contact(in LocomotionResult travel) => travel.IsOnGround ? (byte)1 : (byte)0;

    private static byte[] PlacePacket(RealmSession sess, uint series, in WireSpot spot, byte previousLink) =>
        SelfPosition.Build(
            playActSeries: series,
            chamberIdent: spot.CellId,
            locus: spot.Origin,
            spin: spot.Rotation,
            instSeries: sess.InstanceSequence,
            srvControlSeries: sess.ServerControlSequence,
            warpSeries: sess.TeleportSequence,
            forceLocusSeries: sess.ForcePositionSequence,
            previousLink: previousLink);

    public void TransmitPreNetworkActs(RealmSession? sess, AvatarLocomotionDriver driver, LocomotionResult travel, bool concealed)
    {
        if (sess is null || concealed || !WireSpot.TryFrom(driver, out WireSpot spot))
            return;

        if (travel.ShouldSendMovementEvent)
            TryTransmitTravel(sess, driver, travel);

        if (travel.JumpExtent is { } reach && travel.JumpVelocity is { } vel)
        {
            uint series = sess.UpcomingPlayActSeries();
            sess.TransmitPlayAct(JumpMove.Build(
                playActSeries: series,
                reach: reach,
                vel: vel,
                chamberIdent: spot.CellId,
                locus: spot.Origin,
                spin: spot.Rotation,
                instSeries: sess.InstanceSequence,
                srvControlSeries: sess.ServerControlSequence,
                warpSeries: sess.TeleportSequence,
                forceLocusSeries: sess.ForcePositionSequence));
        }
    }

    public void TransmitPostNetworkLocus(RealmSession? sess, AvatarLocomotionDriver driver, bool concealed)
    {
        if (sess is null || concealed || !WireSpot.TryFrom(driver, out WireSpot spot))
            return;
        if (!driver.ShouldTransmitLocusSignal(spot.Source, driver.ContactPlane, driver.SimMomentSecs) || !driver.CanTransmitLocusSignal)
            return;

        LocomotionResult travel = driver.GrabExhibitOutcome();
        byte link = Contact(travel);
        uint series = sess.UpcomingPlayActSeries();
        byte[] corpus = PlacePacket(sess, series, spot, link);
        _diagnostic.OnOutgoing("AP", series, travel, spot.Origin, spot.CellId, link);
        sess.TransmitPlayAct(corpus);
        driver.NoteLocusSent(spot.Source, driver.ContactPlane, driver.SimMomentSecs);
    }

    // An unconditional grounded position update, used when the server must hear where we are right now
    internal void TransmitImmediateLocus(RealmSession? sess, AvatarLocomotionDriver? driver)
    {
        if (sess is null || driver is null || !driver.CanTransmitLocusSignal || !WireSpot.TryFrom(driver, out WireSpot spot))
            return;

        sess.TransmitPlayAct(PlacePacket(sess, sess.UpcomingPlayActSeries(), spot, previousLink: 1));
        driver.NoteLocusSent(spot.Source, driver.ContactPlane, driver.SimMomentSecs);
    }

    public bool TryTransmitTravel(RealmSession? sess, AvatarLocomotionDriver? driver, LocomotionResult travel)
    {
        if (sess is null || driver is null || !WireSpot.TryFrom(driver, out WireSpot spot))
            return false;

        byte link = Contact(travel);
        uint series = sess.UpcomingPlayActSeries();
        byte[] corpus = MoveToFrame.Build(
            playActSeries: series,
            rawLocomotionPhase: AssembleRawLocomotionPhase(travel),
            chamberIdent: spot.CellId,
            locus: spot.Origin,
            spin: spot.Rotation,
            instSeries: sess.InstanceSequence,
            srvControlSeries: sess.ServerControlSequence,
            warpSeries: sess.TeleportSequence,
            forceLocusSeries: sess.ForcePositionSequence,
            link: link != 0,
            standingLongjump: false);
        _diagnostic.OnOutgoing("MTS", series, travel, spot.Origin, spot.CellId, link);
        sess.TransmitPlayAct(corpus);
        driver.NoteTravelSent(driver.SimMomentSecs, travel.IsMouseLookMovementEvent);
        return true;
    }

    public static CrudeLocomotionPhase AssembleRawLocomotionPhase(LocomotionResult travel)
    {
        if (travel.RawMotionStateOverride is { } overridden)
            return new CrudeLocomotionPhase(overridden);

        HeldKey exec = travel.IsRunning ? HeldKey.Run : HeldKey.None;
        CrudeLocomotionPhase defaults = CrudeLocomotionPhase.Default;
        static HeldKey Grip(bool present, bool usesExecGrip, HeldKey run) => !present ? HeldKey.Invalid : usesExecGrip ? HeldKey.Run : run;
        return new CrudeLocomotionPhase
        {
            CurrentHoldKey = exec,
            CurrentStyle = travel.CurrentStyle,
            ForwardCommand = travel.ForwardCommand ?? defaults.ForwardCommand,
            ForwardHoldKey = Grip(travel.ForwardCommand.HasValue, usesExecGrip: false, exec),
            ForwardSpeed = travel.ForwardSpeed ?? defaults.ForwardSpeed,
            SidestepDirective = travel.SidestepCommand ?? defaults.SidestepDirective,
            SidestepGripTag = Grip(travel.SidestepCommand.HasValue, travel.SidestepUsesRunHold, exec),
            SidestepPace = travel.SidestepSpeed ?? defaults.SidestepPace,
            TurnDirective = travel.TurnCommand ?? defaults.TurnDirective,
            PivotGripTag = Grip(travel.TurnCommand.HasValue, travel.TurnUsesRunHold, exec),
            TurnPace = travel.TurnSpeed ?? defaults.TurnPace,
        };
    }
}

// A diagnostic sink that only forwards outbound sends to a callback
internal sealed class DelegateLocomotionTruthDiagnosticSink(Action<string, uint, LocomotionResult, Vector3, uint, byte> outbound)
    : ILocomotionTruthDiagnosticSink
{
    private readonly Action<string, uint, LocomotionResult, Vector3, uint, byte> _outgoing = outbound ?? throw new ArgumentNullException(nameof(outbound));

    public void OnOutgoing(string sort, uint series, LocomotionResult outcome, Vector3 wireLocus, uint wireChamberIdent, byte linkByte) =>
        _outgoing(sort, series, outcome, wireLocus, wireChamberIdent, linkByte);

    public void OnSrvEcho(RealmSession.MoverPositionUpdate refresh, Vector3 srvRealmLocus)
    {
    }

    public void ResetSess()
    {
    }
}

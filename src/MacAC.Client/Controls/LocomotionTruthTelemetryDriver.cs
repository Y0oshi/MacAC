using System.Numerics;
using MacAC.Wire;

namespace MacAC.Client.Controls;

internal sealed class LocomotionTruthTelemetryDriver(
    bool turnedOn,
    ISimAvatarDriverSource player,
    IAvatarIdentitySource identity)
        : ILocomotionTruthDiagnosticSink
{
    private readonly bool _turnedOn = turnedOn;
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private LocomotionTruthOutbound? _previousOutgoing;

    private readonly record struct LocomotionTruthOutbound(
        string Kind,
        uint Sequence,
        DateTime TimeUtc,
        Vector3 LocalWorldPosition,
        Vector3 WirePosition,
        uint WireCellId,
        bool IsOnGround,
        byte ContactByte,
        Vector3 Velocity);

    public void OnOutgoing(
        string sort,
        uint series,
        LocomotionResult outcome,
        Vector3 wireLocus,
        uint wireChamberIdent,
        byte linkByte)
    {
        if (!_turnedOn)
            return;

        Vector3 vel = _avatar.Controller?.CorpusVel ?? Vector3.Zero;
        _previousOutgoing = new LocomotionTruthOutbound(
            sort,
            series,
            DateTime.UtcNow,
            outcome.Position,
            wireLocus,
            wireChamberIdent,
            outcome.IsOnGround,
            linkByte,
            vel);

        Console.WriteLine(FormattableString.Invariant(
            $"move-truth OUT kind={sort} seq={series} local={Fmt(outcome.Position)} localCell=0x{outcome.CellId:X8} wire={Fmt(wireLocus)} wireCell=0x{wireChamberIdent:X8} grounded={outcome.IsOnGround} contact={linkByte} vel={Fmt(vel)} f={FmtCmd(outcome.ForwardCommand)} s={FmtCmd(outcome.SidestepCommand)} t={FmtCmd(outcome.TurnCommand)}"));
    }

    public void OnSrvEcho(
        RealmSession.MoverPositionUpdate refresh,
        Vector3 srvRealmLocus)
    {
        if (!_turnedOn || refresh.Guid != _identity.SrvOid)
            return;

        DateTime instant = DateTime.UtcNow;
        var driver = _avatar.Controller;
        Vector3? ownLocus = driver?.Position;
        uint? ownChamberIdent = driver?.CellId;
        Vector3? diffOwn = ownLocus.HasValue
            ? srvRealmLocus - ownLocus.Value
            : null;

        string ownPhrase = ownLocus.HasValue ? Fmt(ownLocus.Value) : "-";
        string ownChamberPhrase = ownChamberIdent.HasValue
            ? FormattableString.Invariant($"0x{ownChamberIdent.Value:X8}")
            : "-";
        string diffOwnPhrase = diffOwn.HasValue ? Fmt(diffOwn.Value) : "-";
        string diffOwnLen = diffOwn.HasValue
            ? FormattableString.Invariant($"{diffOwn.Value.Length():F3}")
            : "-";

        string previousPhrase = "-";
        if (_previousOutgoing is { } previous)
        {
            previousPhrase = OnSrvEchoBranch(srvRealmLocus, previous, instant);
        }

        string phase = driver?.State.ToString() ?? "-";
        string velPhrase = refresh.Velocity.HasValue
            ? Fmt(refresh.Velocity.Value)
            : "-";

        Console.WriteLine(FormattableString.Invariant(
            $"move-truth ECHO guid=0x{refresh.Guid:X8} server={Fmt(srvRealmLocus)} serverCell=0x{refresh.Position.LandblockId:X8} local={ownPhrase} localCell={ownChamberPhrase} deltaLocal={diffOwnPhrase} distLocal={diffOwnLen} serverVel={velPhrase} state={phase} lastOut={previousPhrase}"));
    }

    private string OnSrvEchoBranch(Vector3 srvRealmLocus, LocomotionTruthOutbound previous, DateTime instant)
    {
        string previousPhrase;
        Vector3 diffOut = srvRealmLocus - previous.LocalWorldPosition;
        double ageMsec = (instant - previous.TimeUtc).TotalMilliseconds;
        previousPhrase = FormattableString.Invariant(
                        $"{previous.Kind}:{previous.Sequence} ageMs={ageMsec:F0} outGrounded={previous.IsOnGround} outContact={previous.ContactByte} outCell=0x{previous.WireCellId:X8} deltaOut={Fmt(diffOut)} distOut={diffOut.Length():F3}");
        return previousPhrase;
    }

    public void ResetSess() => _previousOutgoing = null;

    private static string Fmt(Vector3 val)
    {
        return FormattableString.Invariant(
            $"({val.X:F3},{val.Y:F3},{val.Z:F3})");
    }

    private static string FmtCmd(uint? directive)
    {
        return directive.HasValue
            ? FormattableString.Invariant($"0x{directive.Value:X8}")
            : "-";
    }
}

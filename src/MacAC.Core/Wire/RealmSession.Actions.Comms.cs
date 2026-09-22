using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Game actions: Chat, emotes, friends, squelches and channel membership.</summary>
public sealed partial class RealmSession
{
    public void TransmitTalk(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        Act(seq => CommsAsks.AssembleTalk(seq, phrase));
    }

    public void TransmitTell(string markLabel, string phrase)
    {
        ArgumentNullException.ThrowIfNull(markLabel);
        ArgumentNullException.ThrowIfNull(phrase);
        Act(seq => CommsAsks.AssembleTell(seq, markLabel, phrase));
    }

    public void TransmitLane(uint laneIdent, string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        Act(seq => CommsAsks.AssembleCommsLane(seq, laneIdent, phrase));
    }

    public void TransmitSetAfkManner(bool away) =>
        Act(seq => CommandAsks.AssembleSetAfkManner(seq, away));

    public void TransmitSetAfkMsg(string msg) =>
        Act(seq => CommandAsks.AssembleSetAfkMsg(seq, msg));

    public void TransmitEmote(string msg) =>
        Act(seq => CommandAsks.AssembleEmote(seq, msg));

    public void TransmitSoulEmote(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        Act(seq => CommandAsks.AssembleSoulEmote(seq, msg));
    }

    public void TransmitAppendFriend(string label) =>
        Act(seq => CommandAsks.AssembleAppendFriend(seq, label));

    public void TransmitDropFriend(uint friendIdent) =>
        Act(seq => CommandAsks.AssembleDropFriend(seq, friendIdent));

    public void TransmitWipeFriends() =>
        Act(seq => CommandAsks.AssembleWipeFriends(seq));

    public void TransmitLegacyFriendsRosterReq()
    {
        TransmitControlMsg(CommandAsks.AssembleLegacyFriendsDirective(0u, string.Empty));
    }

    public void TransmitModifyToonSquelch(bool append, uint toonIdent, string label, uint msgKind)
    {
        Act(seq => CommandAsks.AssembleModifyToonSquelch(seq, append, toonIdent, label, msgKind));
    }

    public void TransmitModifyAcctSquelch(bool append, string label) =>
        Act(seq => CommandAsks.AssembleModifyAcctSquelch(seq, append, label));

    public void TransmitModifyGlobalSquelch(bool append, uint msgKind) =>
        Act(seq => CommandAsks.AssembleModifyGlobalSquelch(seq, append, msgKind));

    public void TransmitOrdinalLanes() =>
        Act(seq => CommandAsks.AssembleOrdinalLanes(seq));

    public void TransmitRosterLane(uint laneIdent) =>
        Act(seq => CommandAsks.AssembleRosterLane(seq, laneIdent));

    public void TransmitOnLane(uint laneIdent) =>
        Act(seq => CommandAsks.AssembleOnLane(seq, laneIdent));

    public void TransmitOffLane(uint laneIdent) =>
        Act(seq => CommandAsks.AssembleOffLane(seq, laneIdent));

    public void TransmitWipeConsent() =>
        Act(seq => CommandAsks.AssembleWipeConsent(seq));

    public void TransmitReadoutConsent() =>
        Act(seq => CommandAsks.AssembleReadoutConsent(seq));

    public void TransmitDropConsent(string label) =>
        Act(seq => CommandAsks.AssembleDropConsent(seq, label));

    public void TransmitTurbineCommsTo(
        uint hallIdent,
        uint commsKind,
        uint relayKind,
        uint senderOid,
        string phrase,
        uint cookie)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        _ = relayKind;

        var cargo = new TurbineComms.Cargo.RoomSendAsk(
            ContextId: cookie,
            RoomId: hallIdent,
            Message: phrase,
            ExtraDataSize: 0x0Cu,
            SenderId: senderOid,
            HResult: 0,
            ChatType: commsKind);

        // The room-send form carries its cookie inside the payload; the outer one stays zero.
        TransmitPlayAct(TurbineComms.Build(
            blobKind: TurbineComms.BlobKind.RequestBinary,
            relayKind: TurbineComms.RelayKind.SendToRoomById,
            markKind: 1u,
            markIdent: 0u,
            conveyanceKind: 0u,
            conveyanceIdent: 0u,
            cookie: 0u,
            cargo: cargo));
    }
}

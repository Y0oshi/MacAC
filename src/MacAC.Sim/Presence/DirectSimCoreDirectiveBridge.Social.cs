using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Arcana;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

/// <summary>Chat, friends and squelch, fellowship and allegiance directives.</summary>
public sealed partial class DirectSimCoreDirectiveBridge
{
    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimBuddyDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var condition = SimDirectiveStatus.Accepted;
        switch (directive.Kind)
        {
            case SimBuddyDirectiveKind.Add
                when !string.IsNullOrWhiteSpace(directive.Name):
                sess!.TransmitAppendFriend(directive.Name);
                break;
            case SimBuddyDirectiveKind.Remove
                when directive.CharacterId is not 0u:
                sess!.TransmitDropFriend(directive.CharacterId);
                break;
            case SimBuddyDirectiveKind.Clear:
                sess!.TransmitWipeFriends();
                break;
            case SimBuddyDirectiveKind.RequestLegacyList:
                sess!.TransmitLegacyFriendsRosterReq();
                break;
            default:
                condition = SimDirectiveStatus.Rejected;
                break;
        }
        return Emit(
            SimDirectiveDomain.Social,
            (int)directive.Kind,
            condition,
            directive.CharacterId,
            directive.Name);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimMuteDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var condition = SimDirectiveStatus.Accepted;
        switch (directive.Scope)
        {
            case SimMuteScope.Character
                when directive.CharacterId is not 0u
                    && !string.IsNullOrWhiteSpace(directive.Name):
                sess!.TransmitModifyToonSquelch(
                    directive.Add,
                    directive.CharacterId,
                    directive.Name,
                    directive.MessageType);
                break;
            case SimMuteScope.Account
                when !string.IsNullOrWhiteSpace(directive.Name):
                sess!.TransmitModifyAcctSquelch(
                    directive.Add,
                    directive.Name);
                break;
            case SimMuteScope.Global:
                sess!.TransmitModifyGlobalSquelch(
                    directive.Add,
                    directive.MessageType);
                break;
            default:
                condition = SimDirectiveStatus.Rejected;
                break;
        }
        return Emit(
            SimDirectiveDomain.Social,
            4 + (int)directive.Scope,
            condition,
            directive.CharacterId,
            directive.Name);
    }

    public SimDirectiveResult Create(
        SimEpochTicket anticipatedGen,
        string fellowshipLabel,
        bool portionXp)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (string.IsNullOrWhiteSpace(fellowshipLabel))
        {
            return Unsupported(
                SimDirectiveDomain.Fellowship,
                op: 0,
                SimDirectiveStatus.Rejected);
        }
        sess!.TransmitFellowshipBuild(fellowshipLabel, portionXp);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 0,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Recruit(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Fellowship,
                op: 1,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        sess!.TransmitFellowshipRecruit(markOid);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 1,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Dismiss(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Fellowship,
                op: 2,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        sess!.TransmitFellowshipDismiss(markOid);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 2,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Quit(
        SimEpochTicket anticipatedGen,
        bool disband)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (_sim.FellowshipHolder.RequiresLeaderHandoffPriorQuit(
                _sim.AvatarIdentity.ServerGuid,
                disband,
                out uint newLeaderOid))

            sess!.TransmitFellowshipAssignNewLeader(newLeaderOid);
        sess!.TransmitFellowshipQuit(disband);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 3,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AssignLeader(
        SimEpochTicket anticipatedGen,
        uint newLeaderOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (newLeaderOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Fellowship,
                op: 4,
                SimDirectiveStatus.Rejected,
                newLeaderOid);
        }
        sess!.TransmitFellowshipAssignNewLeader(newLeaderOid);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 4,
            SimDirectiveStatus.Accepted,
            newLeaderOid);
    }

    public SimDirectiveResult SetOpen(
        SimEpochTicket anticipatedGen,
        bool isOpen)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        sess!.TransmitFellowshipEditOpenness(isOpen);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult ApplyBoardOpen(
        SimEpochTicket anticipatedGen,
        bool boardOpen)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        sess!.TransmitFellowshipRefreshReq(boardOpen);
        return Emit(
            SimDirectiveDomain.Fellowship,
            op: 6,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Swear(
        SimEpochTicket anticipatedGen,
        uint patronOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (patronOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Allegiance,
                op: 0,
                SimDirectiveStatus.Rejected,
                patronOid);
        }
        sess!.TransmitAllegianceSwear(patronOid);
        return Emit(
            SimDirectiveDomain.Allegiance,
            op: 0,
            SimDirectiveStatus.Accepted,
            patronOid);
    }

    public SimDirectiveResult Break(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Allegiance,
                op: 1,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        sess!.TransmitAllegianceBreak(markOid);
        return Emit(
            SimDirectiveDomain.Allegiance,
            op: 1,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Kick(
        SimEpochTicket anticipatedGen,
        uint vassalOid)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (vassalOid is 0u)
        {
            return Unsupported(
                SimDirectiveDomain.Allegiance,
                op: 2,
                SimDirectiveStatus.Rejected,
                vassalOid);
        }
        sess!.TransmitAllegianceKick(vassalOid);
        return Emit(
            SimDirectiveDomain.Allegiance,
            op: 2,
            SimDirectiveStatus.Accepted,
            vassalOid);
    }

    public SimDirectiveResult ReqDetails(
        SimEpochTicket anticipatedGen,
        string avatarLabel)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        sess!.TransmitAllegianceDetailsReq(avatarLabel ?? string.Empty);
        return Emit(
            SimDirectiveDomain.Allegiance,
            op: 3,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AssignRefreshSubscription(
        SimEpochTicket anticipatedGen,
        bool on)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        sess!.TransmitAllegianceRefreshReq(on);
        return Emit(
            SimDirectiveDomain.Allegiance,
            op: 4,
            SimDirectiveStatus.Accepted);
    }

    private bool TryTransmitOnLane(
        RealmSession sess,
        SimCommsChannel lane,
        string phrase)
    {
        if (lane == SimCommsChannel.Allegiance
            && !_sim.CommunicationHolder.TurbineChat.Enabled)
        {
            sess.TransmitLane(0x02000000u, phrase);
            return true;
        }

        if (TurbineSortOf(lane, out ChannelKindLite turbineSort))
        {
            var latch = TurbineCommsMembershipTurnstile.Evaluate(
                turbineSort,
                _sim.CommunicationHolder.TurbineChat,
                _sim.ToonHolder.Options,
                _sim.ToonHolder.IsOlthoiAvatar);

            // Shared refusal-text mapping - see
            // TurbineCommsMembershipTurnstile.ResolveRefusalText.
            if (latch.Status != TurbineCommsTurnstileStatus.Allowed)
            {
                if (TurbineCommsMembershipTurnstile.LocateRefusalPhrase(latch) is
                    (string refusalPhrase, CanonLogTextType refusalKind))

                    _sim.CommunicationHolder.AddText(refusalPhrase, refusalKind);
                return true;
            }

            sess.TransmitTurbineCommsTo(
                latch.RoomId,
                latch.ChatType,
                (uint)TurbineComms.RelayKind.SendToRoomById,
                _sim.AvatarIdentity.ServerGuid,
                phrase,
                _sim.CommunicationHolder.TurbineChat.UpcomingCtxIdent());
            return true;
        }

        uint? legacyLane = lane switch
        {
            SimCommsChannel.Fellowship => 0x00000800u,
            SimCommsChannel.AllegianceBroadcast => 0x02000000u,
            SimCommsChannel.Vassals => 0x00001000u,
            SimCommsChannel.Patron => 0x00002000u,
            SimCommsChannel.Monarch => 0x00004000u,
            SimCommsChannel.CoVassals => 0x01000000u,
            _ => null,
        };
        if (legacyLane is not { } laneIdent)
            return false;
        sess.TransmitLane(laneIdent, phrase);
        return true;
    }

    private static bool TurbineSortOf(
        SimCommsChannel lane,
        out ChannelKindLite sort)
    {
        sort = lane switch
        {
            SimCommsChannel.Allegiance => ChannelKindLite.Allegiance,
            SimCommsChannel.General => ChannelKindLite.General,
            SimCommsChannel.Trade => ChannelKindLite.Trade,
            SimCommsChannel.LookingForGroup => ChannelKindLite.Lfg,
            SimCommsChannel.Roleplay => ChannelKindLite.Roleplay,
            SimCommsChannel.Society => ChannelKindLite.Society,
            SimCommsChannel.Olthoi => ChannelKindLite.Olthoi,
            _ => default,
        };
        return lane is SimCommsChannel.Allegiance
            or SimCommsChannel.General
            or SimCommsChannel.Trade
            or SimCommsChannel.LookingForGroup
            or SimCommsChannel.Roleplay
            or SimCommsChannel.Society
            or SimCommsChannel.Olthoi;
    }
}

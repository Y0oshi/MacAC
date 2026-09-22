using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fellows;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    private sealed partial class Binder
    {
        public void AttachComms()
        {
            On(GameEventKind.ChannelBroadcast, PlaySignals.DecodeLaneAir, broadcast => Chat.OnLaneAir(broadcast.ChannelId, broadcast.SenderName, broadcast.Message));
            On(GameEventKind.Tell, PlaySignals.DecodeTell, broadcast => Chat.OnTellReceived(broadcast.SenderName, broadcast.Message, broadcast.SenderGuid, broadcast.ChatType));
            OnRef(GameEventKind.CommunicationTransientString, PlaySignals.DecodeTransient, s => Say(s, CanonLogTextType.ClientLocal));
            OnRef(GameEventKind.PopupString, PlaySignals.DecodePopupString, Chat.OnPopup);
            On(GameEventKind.QueryAgeResponse, PlaySignals.DecodeAskAgeResponse, broadcast => SayPlain(
                string.IsNullOrEmpty(broadcast.Name) ? $"You have played for {broadcast.Age}." : $"{broadcast.Name} has played for {broadcast.Age}."));

            OnRef(GameEventKind.ChannelIndex, CommandReplies.DecodeLaneOrdinal, lanes => Strokes(CommandReplies.ComposeLaneOrdinalStrokes(lanes)));
            OnRef(GameEventKind.ChannelList, CommandReplies.DecodeLaneRoster, labels => Strokes(CommandReplies.ComposeLaneRosterStrokes(labels)));
            On(GameEventKind.AvailableHouses, CommandReplies.DecodeOnHandHouses, houses => Strokes(CommandReplies.ComposeOnHandHousesStrokes(houses)));
            On(GameEventKind.AllegianceInfoResponse, CommandReplies.DecodeAllegianceDetailsResponse, details => Strokes(CommandReplies.ComposeAllegianceDetailsStrokes(details)));
        }

        public void AttachSocial()
        {
            if (OnFellowshipWholeRefresh is { } wholeRefresh)
                On(GameEventKind.FellowshipFullUpdate, PlaySignals.DecodeFellowshipWholeRefresh, wholeRefresh);
            if (OnFellowshipRefreshFellow is { } fellowRefresh)
                On(GameEventKind.FellowshipUpdateFellow, PlaySignals.DecodeFellowshipRefreshFellow, fellowRefresh);
            if (OnFellowshipQuit is { } quit)
                On(GameEventKind.FellowshipQuit, PlaySignals.DecodeFellowshipQuit, p => quit(p.QuitterGuid));
            if (OnFellowshipDismiss is { } dismiss)
                On(GameEventKind.FellowshipDismiss, PlaySignals.DecodeFellowshipDismiss, dismissed => dismiss(dismissed.DismissedGuid));
            if (OnFellowshipDisband is { } disband)
                OnBit(GameEventKind.FellowshipDisband, PlaySignals.DecodeFellowshipDisband, disband);

            if (OnAllegianceRefresh is { } allegiance)
                On(GameEventKind.AllegianceUpdate, CommandReplies.DecodeAllegianceRefresh, allegiance);
            if (OnAllegianceRefreshDone is { } done)
                On(GameEventKind.AllegianceUpdateDone, PlaySignals.DecodeAllegianceRefreshDone, done);
            if (OnAllegianceRefreshAborted is { } aborted)
                On(GameEventKind.AllegianceUpdateAborted, PlaySignals.DecodeAllegianceRefreshAborted, aborted);
            if (OnAllegianceSigninNotification is { } signin)
                On(GameEventKind.AllegianceLoginNotification, PlaySignals.DecodeAllegianceSigninNotification, signin);
        }

        // Secure trade, 0x01FD-0x0208
        public void AttachBarter()
        {
            if (OnBarterEnroll is { } enroll)
                On(GameEventKind.RegisterTrade, PlaySignals.DecodeEnrollBarter, enroll);
            if (OnBarterShut is { } shut)
                On(GameEventKind.CloseTrade, PlaySignals.DecodeShutBarter, shut);
            if (OnBarterAppend is { } append)
                On(GameEventKind.AddToTrade, PlaySignals.DecodeAppendToBarter, append);
            if (OnBarterDrop is { } drop)
                On(GameEventKind.RemoveFromTrade, PlaySignals.DecodeDropFromBarter, drop);
            if (OnBarterAdmit is { } admit)
                On(GameEventKind.AcceptTrade, PlaySignals.DecodeAdmitBarter, admit);
            if (OnBarterDecline is { } decline)
                On(GameEventKind.DeclineTrade, PlaySignals.DecodeDeclineBarter, decline);
            if (OnBarterRestart is { } restart)
                On(GameEventKind.ResetTrade, PlaySignals.DecodeRestartBarter, restart);
            if (OnBarterMiss is { } miss)
                On(GameEventKind.TradeFailure, PlaySignals.DecodeBarterMiss, miss);
            if (OnBarterWipeAcceptance is { } wipe)
                Raw(GameEventKind.ClearTradeAcceptance, _ => wipe());
        }

        public void AttachHousing()
        {
            if (OnHouseBlob is { } blob)
                On(GameEventKind.HouseData, PlaySignals.DecodeHouseBlob, blob);
            if (OnHouseCondition is { } condition)
                On(GameEventKind.HouseStatus, PlaySignals.DecodeHouseCondition, condition);
            if (OnHouseRefreshRentMoment is { } rentMoment)
                On(GameEventKind.UpdateRentTime, PlaySignals.DecodeRefreshRentMoment, rentMoment);
            if (OnHouseRefreshRentPayment is { } rentPayment)
                OnRef(GameEventKind.UpdateRentPayment, PlaySignals.DecodeRefreshRentPayment, rentPayment);
        }

        // Contracts, titles, confirmations, friends, squelches and Turbine chat rooms
        public void AttachToon()
        {
            if (OnContractChart is { } chart)
                OnRef(GameEventKind.SendClientContractTrackerTable, cargo => QuestTrackerNotices.DecodeChart(cargo, DateTime.UtcNow), chart);
            if (OnContractRefresh is { } refresh)
                On(GameEventKind.SendClientContractTracker, cargo => QuestTrackerNotices.DecodeRefresh(cargo, DateTime.UtcNow), refresh);

            if (OnToonBannerChart is { } banners)
                On(GameEventKind.CharacterTitle, PlaySignals.DecodeToonBannerChart, p => banners(p.DisplayTitleId, p.TitleIds));
            if (OnRefreshBanner is { } banner)
                On(GameEventKind.UpdateTitle, PlaySignals.DecodeRefreshBanner, p => banner(p.TitleId, p.SetAsDisplay));

            if (OnConfirmationRequest is { } req)
                On(GameEventKind.CharacterConfirmationRequest, PlaySignals.DecodeToonAckReq, req);
            if (OnAckDone is { } done)
                On(GameEventKind.CharacterConfirmationDone, PlaySignals.DecodeToonAckDone, done);

            if (Friends is { } friends)
                OnRef(GameEventKind.FriendsListUpdate, SocialStateNotices.DecodeFriendsRefresh, friends.Apply);
            if (Squelch is { } squelch)
                OnRef(GameEventKind.SetSquelchDB, SocialStateNotices.DecodeSquelchDatabase, squelch.Replace);

            if (TurbineChat is { } turbine)
            {
                On(GameEventKind.SetTurbineChatChannels, GroupTurbineCommsLanes.TryParse, parsed =>
                {
                    turbine.OnLanesReceived(
                        allegianceHall: parsed.AllegianceRoom,
                        generalHall: parsed.GeneralRoom,
                        barterHall: parsed.TradeRoom,
                        lfgHall: parsed.LfgRoom,
                        roleplayHall: parsed.RoleplayRoom,
                        olthoiHall: parsed.OlthoiRoom,
                        societyHall: parsed.SocietyRoom,
                        societyCelestialHandHall: parsed.SocietyCelestialHandRoom,
                        societyEldrytchWebHall: parsed.SocietyEldrytchWebRoom,
                        societyRadiantBloodHall: parsed.SocietyRadiantBloodRoom);

                    Console.WriteLine(
                        $"chat: GroupTurbineCommsLanes parsed enabled={turbine.Enabled} " +
                        $"general=0x{parsed.GeneralRoom:X8} trade=0x{parsed.TradeRoom:X8} " +
                        $"lfg=0x{parsed.LfgRoom:X8} roleplay=0x{parsed.RoleplayRoom:X8} " +
                        $"society=0x{parsed.SocietyRoom:X8} olthoi=0x{parsed.OlthoiRoom:X8} " +
                        $"allegiance=0x{parsed.AllegianceRoom:X8}");
                });
            }
        }

        // WeenieError replies become interface text unless retail keeps them silent or we have no wording
        // for them
        public void AttachProblems()
        {
            On(GameEventKind.WeenieError, PlaySignals.DecodeWeenieProblem, code => AnnounceWeenieProblem(code, null));
            On(GameEventKind.WeenieErrorWithString, PlaySignals.DecodeWeenieProblemWithString, p => AnnounceWeenieProblem(p.ErrorCode, p.Interpolation));
        }

        private void Strokes(IEnumerable<string> strokes)
        {
            foreach (string stroke in strokes)
                SayPlain(stroke);
        }

        private void AnnounceWeenieProblem(uint code, string? interpolation)
        {
            if (WeenieErrorText.IsSilentClientControlCondition(code))
                return;
            (string? phrase, CanonLogTextType kind) = WeenieErrorText.Resolve(code, interpolation);
            if (phrase is null)
            {
                Console.WriteLine(interpolation is null
                    ? $"[weenie-error] unmapped code=0x{code:X4}"
                    : $"[weenie-error] unmapped code=0x{code:X4} param={interpolation}");
                return;
            }
            Say(phrase, kind);
        }
    }
}

using MacAC.Cockpit.Input;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Dealing;

internal sealed partial class PickingDealingDriver
{
    public bool ServiceFeedAct(FeedAct act)
    {
        switch (act)
        {
            case FeedAct.SelectionSelf:
                PickSelf();
                return true;
            case FeedAct.SelectionPlaceInInventory:
                PutPickInBackpack(primaryBundle: false);
                return true;
            case FeedAct.SelectionPlaceInMainPack:
                PutPickInBackpack(primaryBundle: true);
                return true;
            case FeedAct.SelectionSplitStack:
                if (_pick.ChosenObjectTag is { } pile)
                    _dividePile?.Invoke(pile);
                return true;
            case FeedAct.SelectionClosestCompassItem:
                PickCanonMark(CanonPickingKind.CompassItem, CanonPickingDirection.Closest);
                return true;
            case FeedAct.SelectionPreviousCompassItem:
                PickCanonMark(CanonPickingKind.CompassItem, CanonPickingDirection.Previous);
                return true;
            case FeedAct.SelectionNextCompassItem:
                PickCanonMark(CanonPickingKind.CompassItem, CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionClosestItem:
                PickCanonMark(
                    CanonPickingKind.Item,
                    CanonPickingDirection.Closest,
                    excludePossessedByAvatar: true);
                return true;
            case FeedAct.SelectionPreviousItem:
                PickCanonMark(CanonPickingKind.Item, CanonPickingDirection.Previous);
                return true;
            case FeedAct.SelectionNextItem:
                PickCanonMark(CanonPickingKind.Item, CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionClosestMonster:
                PickCanonMark(
                    CanonPickingKind.Monster,
                    CanonPickingDirection.Closest,
                    unhideToast: true);
                return true;
            case FeedAct.SelectionPreviousMonster:
                PickCanonMark(CanonPickingKind.Monster, CanonPickingDirection.Previous);
                return true;
            case FeedAct.SelectionNextMonster:
                PickCanonMark(CanonPickingKind.Monster, CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionLastAttacker:
                if (_ask.SeekPreviousAttacker() is { } attacker)
                    _pick.Select(attacker, PickChangeSource.Keyboard);
                return true;
            case FeedAct.SelectionClosestPlayer:
                PickCanonMark(CanonPickingKind.Player, CanonPickingDirection.Closest);
                return true;
            case FeedAct.SelectionPreviousPlayer:
                PickCanonMark(CanonPickingKind.Player, CanonPickingDirection.Previous);
                return true;
            case FeedAct.SelectionNextPlayer:
                PickCanonMark(CanonPickingKind.Player, CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionPreviousFellow:
                PickFellow(earlier: true);
                return true;
            case FeedAct.SelectionNextFellow:
                PickFellow(earlier: false);
                return true;
            case FeedAct.SelectionClosestUnopenedCorpse:
                PickCanonMark(CanonPickingKind.UnopenedCorpse, CanonPickingDirection.Closest);
                return true;
            case FeedAct.SelectionNextUnopenedCorpse:
                PickCanonMark(CanonPickingKind.UnopenedCorpse, CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionUseClosestUnopenedCorpse:
                PickAndUseCorpse(CanonPickingDirection.Closest);
                return true;
            case FeedAct.SelectionUseNextUnopenedCorpse:
                PickAndUseCorpse(CanonPickingDirection.Next);
                return true;
            case FeedAct.SelectionGiveToTarget:
                HandPickToEarlierMark();
                return true;
            case FeedAct.SelectionDrop:
                DiscardPick();
                return true;
            case FeedAct.SelectionPreviousSelection:
                _pick.PickEarlier();
                return true;
            case FeedAct.SelectLeft:
                ChooseAndVaultPick(useImmediately: false);
                return true;
            case FeedAct.SelectRight:
                ChoosePickAndExamine();
                return true;
            case FeedAct.SelectDblLeft:
                ChooseAndVaultPick(useImmediately: true);
                return true;
            case FeedAct.SelectionExamine:
                _gearList.StudyChosenOrJoinManner(
                    _pick.ChosenObjectTag ?? 0u);
                return true;
            case FeedAct.UseSelected:
                EmployLatestPick();
                return true;
            case FeedAct.SelectionPickUp:
                if (_pick.ChosenObjectTag is uint liftMark)
                {
                    QueuePersonaTied(
                        SimQueuedDealingKind.Pickup,
                        liftMark,
                        demandOnlineActor: true);
                }
                else
                {
                    _toast?.Invoke("Nothing selected");
                }
                return true;
            case FeedAct.EscapeKey when _gearList.IsAnyObjectiveMannerEngaged:
                _gearList.AbortObjectiveManner();
                return true;
            case FeedAct.EscapeKey when _pick.ChosenObjectTag is not null:
                _pick.Clear(PickChangeSource.Keyboard);
                return true;
            default:
                return false;
        }
    }

    private void ProcessApproachWrapUp(
        SimDealingApproachTicket approachTicket,
        bool natural)
    {
        bool liftApproved = _transactions.TryLocateApproachWrapUp(
            approachTicket,
            natural,
            out SimPendingGrab queuedLift);
        if (queuedLift.Token is not 0u)
        {
            ProcessLiftApproachWrapUp(queuedLift, liftApproved);
            return;
        }

        bool useApproved = _transactions.TryLocateUseApproachWrapUp(
            approachTicket,
            natural,
            out SimPendingUse queuedUse);
        if (queuedUse.Token is not 0u)
            ProcessUseApproachWrapUp(queuedUse, useApproved);
    }

    private void ProcessLiftApproachWrapUp(
        SimPendingGrab queued,
        bool approved)
    {
        if (!approved)
        {
            AbortLiftExhibit(
                queued.ServerGuid,
                queued.PendingPlacementToken);
            return;
        }
        if (!_ask.IsCurrent(queued.ServerGuid, queued.LocalEntityId))
        {
            AbortLiftExhibit(
                queued.ServerGuid,
                queued.PendingPlacementToken);
            return;
        }

        if (!IsLatestLiftExhibit(
                queued.ServerGuid,
                queued.DestinationContainerId,
                queued.Placement,
                queued.PendingPlacementToken))

            return;
        if (!_transactions.TryRelayLift(
                queued,
                _conveyance,
                out _))
        {
            AbortLiftExhibit(
                queued.ServerGuid,
                queued.PendingPlacementToken);
        }
    }

    private void ProcessUseApproachWrapUp(
        SimPendingUse queued,
        bool approved)
    {
        if (!approved)
        {
            queued.Reservation?.AbortPriorRelay();
            return;
        }

        var outcome =
            _transactions.TryRelayUse(
                queued.ServerGuid,
                queued.OwnedByPlayer,
                queued.Useable,
                queued.Reservation,
                _conveyance,
                out uint series);
        if (outcome == SimDealingDispatchResult.NotInWorld)
            _toast?.Invoke("Not in world");
        if (outcome == SimDealingDispatchResult.Dispatched)
        {
            Console.WriteLine(
                $"[interaction] use guid=0x{queued.ServerGuid:X8} seq={series} (arrival-gated)");
        }
    }
}

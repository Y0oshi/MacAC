using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed partial class StashDriver
{
    public void ProcessDiscardFree(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        if (cargo.SourceKind == GearDragSource.ShortcutBar)
            return;

        uint gear = cargo.ObjId;
        if (gear is 0) return;

        var legality = EvaluateDiscard(
            markRoster,
            markChamber,
            gear,
            out _,
            out uint legalityDest);
        uint fallthrough = 0u;
        if (IsCapRejection(legality))
        {
            fallthrough = LocateFallthroughVessel(
                gear, legalityDest, out PackPlacementRefusal noHall);
            if (fallthrough is 0u)
            {
                if (PackPlacementPolicy.ConstructClientOwn(
                        noHall,
                        _objects.Get(gear),
                        _objects.Get(_avatarOid()),
                        _avatarOid()) is { } wholeNotice)

                    _gearDealing?.AnnounceClientOwn(wholeNotice);
                return;
            }
            legality = PackPlacementPolicy.Evaluate(
                _objects, gear, fallthrough, _avatarOid());
            legalityDest = fallthrough;
        }
        if (legality != PackPlacementRefusal.None)
        {
            if (PackPlacementPolicy.ConstructClientOwn(
                    legality,
                    _objects.Get(gear),
                    _objects.Get(legalityDest),
                    _avatarOid()) is { } refusal)

                _gearDealing?.AnnounceClientOwn(refusal);
            return;
        }

        if (markRoster == _insidesGrid
            && _queuedRosterStance is { } ownQueued
            && ownQueued.ContainerId == NetOpen())
        {
            _gearDealing?.AnnounceQueuedBackpackStanceConflict();
            return;
        }
        if (_gearDealing is not null
            && !_gearDealing.SecureSatchelReqPrimed())

            return;

        if (markRoster == _insidesGrid
            && markChamber.GearIdent is not 0
            && TryCombinePiles(gear, markChamber.GearIdent))
            return;

        bool srcIsBag = _objects.Get(gear) is { } dragged && IsBag(dragged);
        uint vessel; int stance;
        if (markRoster == _insidesGrid)
        {
            vessel = NetOpen();
            stance = markChamber.GearIdent is not 0
                ? markChamber.SocketIdx
                : TallyLooseInsides(vessel);                                              // first empty = append after visible loose items
        }
        else if (markRoster == _vesselRoster || markRoster == _topVessel)
        {
            if (srcIsBag)
            {
                vessel = _avatarOid();
                if (vessel is 0u) return;
                stance = Math.Max(0, markChamber.SocketIdx);
            }
            else
            {
                if (markChamber.GearIdent is 0 || markChamber.GearIdent == gear) return;
                vessel = markChamber.GearIdent;                                                  // the bag / main pack
                if (IsVesselWhole(vessel)) return;                                         // red already shown
                stance = _objects.FetchInsides(vessel).Count;                              // append into it
            }
        }
        else return;

        if (fallthrough is not 0u)
        {
            // The named pack was full; append into the pack that has room
            // (a pack joins the player's pack list, an item its loose items).
            vessel = fallthrough;
            stance = vessel != _avatarOid()
                ? _objects.FetchInsides(vessel).Count
                : srcIsBag
                    ? TallyBags(vessel)
                    : TallyLooseInsides(vessel);
        }

        if (vessel == gear) return;                                                         // never into itself

        if (_objects.Get(gear) is { } src)
        {
            uint wholePile = (uint)Math.Max(src.StackSize, 1);
            uint divideDims = _pileDivideQty?.FetchObjectDivideDims(
                gear, _pick.ChosenObjectTag ?? 0u, wholePile) ?? wholePile;
            if (divideDims < wholePile)
            {
                if (_gearDealing is not null)
                {
                    _gearDealing.TryDivideToVessel(
                        gear,
                        vessel,
                        (uint)stance,
                        divideDims);
                }
                else
                {
                    RelaySatchelReq(
                        PackRequestKind.SplitToContainer,
                        gear,
                        () =>
                        {
                            if (_transmitStackableDivideToVessel is null)
                                return false;
                            _transmitStackableDivideToVessel(
                                gear,
                                vessel,
                                (uint)stance,
                                divideDims);
                            return true;
                        });
                }
                return;
            }
        }

        if (_gearDealing is not null)
        {
            PackRequestKind sort = cargo.SourceKind == GearDragSource.Ground
                ? PackRequestKind.Pickup
                : PackRequestKind.PutInContainer;
            if (!_gearDealing.TryRelayQueuedBackpackStance(
                    gear,
                    vessel,
                    stance,
                    sort,
                    () =>
                    {
                        if (_transmitPutGearInVessel is null)
                            return false;
                        _transmitPutGearInVessel(gear, vessel, stance);
                        return true;
                    }))

                return;
            return;
        }

        if (_queuedRosterStance is not null)
            return;
        _queuedRosterStance = new QueuedRosterStance(0u, gear, vessel, stance);
        Populate();
        _transmitPutGearInVessel?.Invoke(gear, vessel, stance);
    }
}

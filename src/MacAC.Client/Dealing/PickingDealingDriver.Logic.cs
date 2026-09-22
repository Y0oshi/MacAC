using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Shell;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Dealing;

internal sealed partial class PickingDealingDriver
{

    public uint? PickClosestFightingMark(bool unhideToast)
    {
        var closest = _ask.SeekClosestHostileMonster();
        uint? finestOid = closest?.ServerGuid;
        if (finestOid is { } chosen)
            _pick.Select(chosen, PickChangeSource.Keyboard);
        else
            _pick.Clear(PickChangeSource.Keyboard);

        if (finestOid is { } oid)
        {
            string caption = _ask.Depict(oid);
            float gap = MathF.Sqrt(closest!.Value.DistanceSquared);
            Console.WriteLine($"combat: selected target 0x{oid:X8} {caption} dist={gap:F1}");
            if (unhideToast)
                _toast?.Invoke($"Target {caption}");
        }
        else if (unhideToast)
        {
            _toast?.Invoke("No monster target");
            Console.WriteLine("combat: no creature target found");
        }
        return finestOid;
    }

    public void PutDraggedGear(GearDragPayload cargo, float pointerX, float pointerY)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        uint mark = _ask.ChooseAt(pointerX, pointerY, includeSelf: true) ?? 0u;
        if (mark is not 0u)
            _ask.CommenceIlluminationPulse(mark);
        _gearList.SetIn3D(cargo, mark);
    }

    public void EmployLatestPick()
    {
        if (_pick.ChosenObjectTag is not uint chosen)
        {
            _toast?.Invoke("Nothing selected");
            return;
        }
        QueuePersonaTied(
            SimQueuedDealingKind.Use,
            chosen,
            demandOnlineActor: false);
    }

    public void ReqUse(
        uint srvOid,
        ItemUseHold? reservation)
    {
        AbortQueuedApproach();

        if (_gearList.TryOpenSecureBarterWithAvatar(srvOid))
        {
            reservation?.AbortPriorRelay();
            return;
        }

        bool possessedByAvatar = _gearList.IsPossessedByAvatar(srvOid);
        bool useable = possessedByAvatar || _ask.IsUseable(srvOid);

        if (useable
            && _ask.TryFetchApproach(srvOid, out DealingApproach approach)
            && !approach.IsCloseRange)
        {
            bool loaded = false;
            bool begun = _movement.OpenApproach(
                approach,
                ticket =>
                {
                    loaded = _transactions.TryArmPostArrivalUse(
                        srvOid,
                        possessedByAvatar,
                        useable,
                        reservation,
                        new SimDealingApproachTicket(
                            ticket.ControllerLifetime,
                            ticket.ApproachGeneration),
                        out _);
                });
            if (!begun || !loaded)
            {
                if (_transactions.TryCancelPendingUse(
                        srvOid, out SimPendingUse cancelled))
                {
                    cancelled.Reservation?.AbortPriorRelay();
                }
                else
                {
                    reservation?.AbortPriorRelay();
                }
            }
            return;
        }

        var outcome =
            _transactions.TryRelayUse(
                srvOid,
                possessedByAvatar,
                useable,
                reservation,
                _conveyance,
                out uint series);
        if (outcome == SimDealingDispatchResult.NotInWorld)
            _toast?.Invoke("Not in world");
        if (outcome == SimDealingDispatchResult.Dispatched)
            Console.WriteLine($"[interaction] use guid=0x{srvOid:X8} seq={series}");
    }

    public uint? ChooseAtCur(bool includeSelf)
        => _ask.SelectAtCur(includeSelf);

    public void ChooseAndVaultPick(bool useImmediately)
    {
        uint? picked = _ask.SelectAtCur(includeSelf: true);
        if (picked is not uint oid)
        {
            if (!_gearList.IsAnyObjectiveMannerEngaged)
                _toast?.Invoke("Nothing to select");
            return;
        }

        _ask.CommenceIlluminationPulse(oid);
        if (_gearList.OfferPrimaryPress(oid) is not GearPrimaryClickResult.NotActive)
            return;

        _pick.Select(oid, PickChangeSource.World);
        string caption = _ask.Depict(oid);
        Console.WriteLine($"[interaction] pick guid=0x{oid:X8} name={caption}");
        _toast?.Invoke($"Selected: {caption}");
        if (useImmediately && !_ask.IsWieldedByAvatar(oid))
        {
            QueuePersonaTied(
                SimQueuedDealingKind.Activate,
                oid,
                demandOnlineActor: true);
        }
    }

    public void ChoosePickAndExamine()
    {
        uint? picked = _ask.SelectAtCur(includeSelf: true);
        if (picked is not uint oid)
            return;

        _ask.CommenceIlluminationPulse(oid);
        _pick.Select(oid, PickChangeSource.World);
        _gearList.StudyChosenOrJoinManner(oid);
    }

    public uint? FetchChosenOrClosestFightingMark(bool autoMark)
    {
        return _pick.ChosenObjectTag is { } chosen
            && _ask.IsAttackableMark(chosen)
            ? chosen
            : autoMark ? PickClosestFightingMark(unhideToast: false) : null;
    }

    public void OnNaturalRelocateToDone()
    {
        if (_transactions.TryFetchQueuedLift(out SimPendingGrab queuedLift))
        {
            ProcessApproachWrapUp(queuedLift.ApproachToken, natural: true);
            return;
        }
        if (_transactions.TryFetchQueuedUse(out SimPendingUse queuedUse))
            ProcessApproachWrapUp(queuedUse.ApproachToken, natural: true);
    }

    public void OnRelocateToCancelled(WeenieProblem _) => AbortQueuedApproach();

    public void OnActorConcealed(uint srvOid)
    {
        _transactions.AbortQueuedInteractions(srvOid);
        if (_transactions.TryCancelPendingPickup(
                srvOid,
                ownActorIdent: null,
                out SimPendingGrab cancelled))
        {
            AbortLiftExhibit(
                cancelled.ServerGuid,
                cancelled.PendingPlacementToken);
        }
        if (_transactions.TryCancelPendingUse(srvOid, out SimPendingUse cancelledUse))
            cancelledUse.Reservation?.AbortPriorRelay();
        if (_pick.ChosenObjectTag == srvOid)
        {
            _pick.Clear(
                PickChangeSource.System,
                PickChangeReason.Cleared);
        }
    }

    public void OnActorRemoved(OnlineActorRecord capture, bool substituteExists)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _transactions.AbortQueuedInteractions(
            capture.ServerOid,
            capture.OwnActorIdent);
        if (_transactions.TryCancelPendingPickup(
                capture.ServerOid,
                capture.OwnActorIdent,
                out SimPendingGrab cancelled))
        {
            AbortLiftExhibit(
                cancelled.ServerGuid,
                cancelled.PendingPlacementToken);
        }
        if (_transactions.TryCancelPendingUse(capture.ServerOid, out SimPendingUse cancelledUse))
            cancelledUse.Reservation?.AbortPriorRelay();
        if (!substituteExists && _pick.ChosenObjectTag == capture.ServerOid)
        {
            _pick.Clear(
                PickChangeSource.System,
                PickChangeReason.SelectedObjectRemoved);
        }
    }

    public void DrainOutbound()
    {
        while (_approachCompletions.TryGrab(out AvatarApproachCompletion wrapUp))
        {
            ProcessApproachWrapUp(
                new SimDealingApproachTicket(
                    wrapUp.Token.ControllerLifetime,
                    wrapUp.Token.ApproachGeneration),
                wrapUp.IsNatural);
        }
        _transactions.DrainOutbound(RelayQueuedDealing);
    }

    public void ResetSession()
    {
        List<Exception> misses = [];
        try { AbortQueuedApproach(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _gearList.ResetSession(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _pick.Reset(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _approachCompletions.Clear(); }
        catch (Exception problem) { misses.Add(problem); }

        if (misses.Count is not 0)
            throw new AggregateException(
                "One or more selection-interaction reset stages failed",
                misses);
    }

    internal void RestartGenExhibit()
    {
        List<Exception> misses = [];
        try { AbortQueuedApproach(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _gearList.RestartGenExhibit(); }
        catch (Exception problem) { misses.Add(problem); }
        try { _approachCompletions.Clear(); }
        catch (Exception problem) { misses.Add(problem); }

        if (misses.Count is not 0)
        {
            throw new AggregateException(
                "One or more selection-interaction presentation reset stages failed",
                misses);
        }
    }
    private void PickSelf()
    {
        uint avatarOid = _ask.AvatarOid;
        if (avatarOid is 0u)
            return;
        if (_gearList.OfferPrimaryPress(avatarOid) is not GearPrimaryClickResult.NotActive)
            return;
        _pick.Select(avatarOid, PickChangeSource.Keyboard);
    }

    private void PickCanonMark(
        CanonPickingKind sort,
        CanonPickingDirection dir,
        bool excludePossessedByAvatar = false,
        bool unhideToast = false)
    {
        uint? mooring = _pick.ChosenObjectTag ?? _pick.EarlierObjectIdent;
        uint? mark = _ask.SeekPickMark(
            sort,
            dir,
            mooring,
            excludePossessedByAvatar);
        if (mark is { } oid)
        {
            _pick.Select(oid, PickChangeSource.Keyboard);
            if (unhideToast)
                _toast?.Invoke(_ask.Depict(oid));
        }
    }

    private void PickAndUseCorpse(CanonPickingDirection dir)
    {
        PickCanonMark(CanonPickingKind.UnopenedCorpse, dir);
        if (_pick.ChosenObjectTag is { } corpse)
            QueuePersonaTied(
                SimQueuedDealingKind.Use,
                corpse,
                demandOnlineActor: false);
    }

    private void PickFellow(bool earlier)
    {
        uint[] fellows = [.. _fellowshipParticipants()
            .Where(static oid => oid != 0u)
            .Distinct()];
        if (fellows.Length is 0)
            return;

        int latest = _pick.ChosenObjectTag is { } chosen
            ? Array.IndexOf(fellows, chosen)
            : -1;
        int upcoming = earlier
            ? (latest > 0 ? latest - 1 : fellows.Length - 1)
            : (latest >= 0 && latest + 1 < fellows.Length ? latest + 1 : 0);
        _pick.Select(fellows[upcoming], PickChangeSource.Keyboard);
    }

    private void PutPickInBackpack(bool primaryBundle)
    {
        if (_pick.ChosenObjectTag is { } chosen)
            _gearList.PutRealmGearInBackpack(chosen, primaryBundle);
    }

    private void HandPickToEarlierMark()
    {
        if (_pick.ChosenObjectTag is not { } chosen
            || _pick.EarlierObjectIdent is not { } mark
            || chosen == mark
            || !_ask.IsBeast(mark))
        {
            _toast?.Invoke(
                "You must select a creature or a character to give that to.\n");
            return;
        }

        if (_gearList.PutChosenIn3D(chosen, mark))
            _pick.Select(mark, PickChangeSource.Keyboard);
    }

    private void DiscardPick()
    {
        if (_pick.ChosenObjectTag is not { } chosen)
            return;
        if (!_gearList.IsPossessedByAvatar(chosen))
        {
            _toast?.Invoke("You must pick that up first");
            return;
        }
        _gearList.PutChosenIn3D(chosen, markOid: 0u);
    }

    private void RelayQueuedDealing(
        SimQueuedDealing dealing)
    {
        var persona = dealing.Identity;
        if (!IsLatest(persona))
            return;

        switch (dealing.Kind)
        {
            case SimQueuedDealingKind.Activate:
                _gearList.EngageGear(persona.ServerGuid);
                break;
            case SimQueuedDealingKind.Use:
                _gearList.EmployChosenOrJoinManner(persona.ServerGuid);
                break;
            case SimQueuedDealingKind.Pickup:
                if (VetLiftMark(persona.ServerGuid, unhideToast: true))
                    _gearList.PutRealmGearInBackpack(persona.ServerGuid);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unrecognized queued interaction kind {dealing.Kind}.");
        }
    }

    private bool VetLiftMark(uint srvOid, bool unhideToast)
    {
        if (_ask.IsBeast(srvOid))
        {
            if (unhideToast)
                _toast?.Invoke(CanonMessages.CannotChooseUpBeasts);
            return false;
        }
        if (_ask.IsWieldedLocusPhase(srvOid)
            && !_gearList.IsPossessedByAvatar(srvOid))
        {
            if (unhideToast)
            {
                _toast?.Invoke(CanonMessages.BeingWieldedBySomeoneElse(
                    _ask.Depict(srvOid)));
            }
            return false;
        }
        if (_ask.IsPickupable(srvOid))
            return true;
        if (unhideToast)
            _toast?.Invoke(CanonMessages.CantBePickedUp(_ask.Depict(srvOid)));
        return false;
    }

    private bool QueuePersonaTied(
        SimQueuedDealingKind sort,
        uint srvOid,
        bool demandOnlineActor)
    {
        uint? ownActorIdent = _ask.TryGrabPersona(srvOid, out uint ownIdent)
            ? ownIdent
            : null;
        ClientThing? gear = _gearList.TryGrabObjectPersona(srvOid, out ClientThing grabbed)
            ? grabbed
            : null;
        if ((demandOnlineActor && ownActorIdent is null)
            || (ownActorIdent is null && gear is null))

            return false;

        SimDealingIdentity persona = new SimDealingIdentity(
            srvOid,
            ownActorIdent,
            gear);
        _transactions.Enqueue(new SimQueuedDealing(sort, persona));
        return true;
    }

    private bool IsLatest(SimDealingIdentity persona)
    {
        return (persona.LocalEntityId is not uint ownIdent
                    || _ask.IsCurrent(persona.ServerGuid, ownIdent))
                && (persona.ClientObject is not { } gear
                    || _gearList.IsLatestObjectPersona(persona.ServerGuid, gear));
    }

    private bool IsLatestLiftExhibit(
        uint gearOid,
        uint destVesselIdent,
        int stance,
        ulong ticket)
    {
        return ticket is not 0u
                && _gearList.TryFetchQueuedBackpackStance(
                    gearOid,
                    out QueuedBackpackStance queued)
                && queued.Token == ticket
                && queued.ContainerId == destVesselIdent
                && queued.Placement == stance;
    }

    private void AbortQueuedApproach()
    {
        if (_transactions.TryCancelPendingPickup(
                out SimPendingGrab queued))
        {
            AbortLiftExhibit(
                queued.ServerGuid,
                queued.PendingPlacementToken);
        }
        if (_transactions.TryCancelPendingUse(out SimPendingUse queuedUse))
            queuedUse.Reservation?.AbortPriorRelay();
    }

    private void AbortLiftExhibit(uint gearOid, ulong ticket = 0u)
        => _gearList.AbortQueuedBackpackStance(gearOid, ticket);
}

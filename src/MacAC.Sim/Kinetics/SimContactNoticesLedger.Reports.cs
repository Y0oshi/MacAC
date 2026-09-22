using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// Per-frame contact reports: opening object and environment contacts, and closing the ones that
// stopped touching
internal sealed partial class SimContactNoticesLedger
{
    internal bool ProcessDossiers(
        SimActorRecord holder,
        KineticBody holderCorpus,
        double kineticsMoment,
        bool earlierLink,
        bool earlierOnPassable,
        bool collidedWithSurroundings,
        ImmutableArray<uint> collidedObjectIdents)
    {
        Live();
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(holderCorpus);
        if (!double.IsFinite(kineticsMoment)
            || !TryOnlineParty(holder, holderCorpus, out SimActorKey tag))

            return false;
        _rev = checked(_rev + 1UL);

        bool reported = false;
        if (collidedObjectIdents.IsDefault)
            collidedObjectIdents = ImmutableArray<uint>.Empty;
        for (int ordinal = 0; ordinal < collidedObjectIdents.Length; ++ordinal)
        {
            if (!PartyIsOnline(holder, holderCorpus, tag))
                return reported;

            uint collidedIdent = collidedObjectIdents[ordinal];
            if (collidedIdent is 0u || collidedIdent == tag.LocalEntityId)
                continue;
            if (!_proxies.TryFetchImpactHolder(
                    collidedIdent,
                    out _,
                    out bool registeredStatic))

                continue;

            if (registeredStatic)
            {
                reported |= NoteSurroundingsLink(
                    holder,
                    holderCorpus,
                    tag,
                    earlierLink);
                continue;
            }

            if (!TryOnlineParty(
                    collidedIdent,
                    out SimActorRecord mark,
                    out KineticBody markCorpus,
                    out SimActorKey markTag))

                continue;

            var markPhase = markCorpus.State;
            if ((markPhase & KineticStateFlags.Static) != 0)
            {
                reported |= NoteSurroundingsLink(
                    holder,
                    holderCorpus,
                    tag,
                    earlierLink);
                continue;
            }

            var holderPhase = HolderFor(tag);
            bool isNew = !holderPhase.Records.ContainsKey(markTag);
            holderPhase.Records[markTag] = new ContactRecord(
                kineticsMoment,
                (markPhase & KineticStateFlags.Ethereal) != 0,
                mark.ServerGuid);
            if (!isNew)
                continue;

            holderPhase.Order.Add(markTag);
            ConnectTouching(markTag, tag);
            reported |= NoteObjectLink(
                holder,
                holderCorpus,
                tag,
                mark,
                markCorpus,
                markTag,
                markPhase,
                earlierLink);
        }

        if (!PartyIsOnline(holder, holderCorpus, tag))
            return reported;

        ShutExpiredLinks(
            holder,
            holderCorpus,
            tag,
            kineticsMoment,
            force: false);
        if (!PartyIsOnline(holder, holderCorpus, tag))
            return reported;

        var kept = TryHolder(tag);
        if (kept?.CollidingWithEnvironment == true)
        {
            kept.CollidingWithEnvironment = collidedWithSurroundings;
        }
        else if (collidedWithSurroundings
                 || (!earlierOnPassable && holderCorpus.OnWalkable))
        {
            reported |= NoteSurroundingsLink(
                holder,
                holderCorpus,
                tag,
                earlierLink);
        }

        PruneHolder(tag);
        return reported;
    }

    private bool NoteObjectLink(
        SimActorRecord holder,
        KineticBody holderCorpus,
        SimActorKey holderTag,
        SimActorRecord mark,
        KineticBody markCorpus,
        SimActorKey markTag,
        KineticStateFlags markPhase,
        bool earlierLink,
        bool preciseDormantHolder = false,
        ulong anticipatedHolderLocusArbiterVer = 0UL,
        ulong setLocusLotIdent = 0UL)
    {
        if ((markPhase & KineticStateFlags.ReportAsEnvironment) != 0)
        {
            return NoteSurroundingsLink(
                holder,
                holderCorpus,
                holderTag,
                earlierLink,
                setLocusLotIdent);
        }

        var holderPhase = holderCorpus.State;
        bool holderWasMissile =
            (holderPhase & KineticStateFlags.Missile) != 0;
        bool holderReported = (markPhase
                & KineticStateFlags.IgnoreCollisions) == 0
            && (holderPhase & KineticStateFlags.ReportCollisions) != 0;
        if (holderReported)
        {
            Publish(new SimContactNotice(
                UpcomingSeq(),
                SimContactNoticeKind.ObjectCollision,
                holderTag,
                holder.ServerGuid,
                markTag,
                mark.ServerGuid,
                earlierLink,
                markCorpus.InContact));
        }

        if (holderWasMissile
            && (markPhase & KineticStateFlags.IgnoreCollisions) == 0)
        {
            HaltMissile(
                holder,
                holderCorpus,
                holderTag,
                demandLatestMissile: false);
        }

        bool markReported = PartyIsOnline(mark, markCorpus, markTag)
            && (markCorpus.State & KineticStateFlags.ReportCollisions) != 0
            && (PartyIsOnline(holder, holderCorpus, holderTag)
                || preciseDormantHolder
                    && PreciseDormantParty(
                        holder,
                        holderCorpus,
                        holderTag,
                        anticipatedHolderLocusArbiterVer))
            && (holderCorpus.State & KineticStateFlags.IgnoreCollisions) == 0;
        if (markReported)
        {
            Publish(new SimContactNotice(
                UpcomingSeq(),
                SimContactNoticeKind.ObjectCollision,
                markTag,
                mark.ServerGuid,
                holderTag,
                holder.ServerGuid,
                markCorpus.InContact,
                earlierLink));
        }
        return holderReported || markReported;
    }

    private bool PreciseDormantParty(
        SimActorRecord capture,
        KineticBody corpus,
        SimActorKey tag,
        ulong anticipatedLocusArbiterVer)
    {
        return !corpus.InWorld
        && (corpus.TransientState & TransientPhaseFlagSet.Active) == 0
        && (corpus.State & KineticStateFlags.Hidden) == 0
        && _actors.IsCurrent(capture)
        && !_leaving.Contains(tag)
        && !_intakeBlocked.Contains(tag)
        && anticipatedLocusArbiterVer is not 0UL
        && capture.PositionAuthorityVersion
            == anticipatedLocusArbiterVer
        && PartyIsRecognized(capture, corpus, tag);
    }

    private bool NoteSurroundingsLink(
        SimActorRecord holder,
        KineticBody holderCorpus,
        SimActorKey holderTag,
        bool earlierLink,
        ulong setLocusLotIdent = 0UL)
    {
        var phase = HolderFor(
            holderTag, setLocusLotIdent);
        if (phase.CollidingWithEnvironment)
            return false;

        bool reported = (holderCorpus.State
            & KineticStateFlags.ReportCollisions) != 0;
        phase.CollidingWithEnvironment = true;
        if (reported)
        {
            Publish(new SimContactNotice(
                UpcomingSeq(),
                SimContactNoticeKind.EnvironmentCollision,
                holderTag,
                holder.ServerGuid,
                Other: null,
                OtherServerGuid: null,
                earlierLink,
                OtherWasInContact: false));
        }
        HaltMissile(holder, holderCorpus, holderTag);
        return reported;
    }

    private void ShutExpiredLinks(
        SimActorRecord holder,
        KineticBody? holderCorpus,
        SimActorKey holderTag,
        double kineticsMoment,
        bool force,
        ulong setLocusLotIdent = 0UL,
        ulong anticipatedLocusArbiterVer = 0UL)
    {
        if (!_holders.TryGetValue(holderTag, out HolderLedger? phase)
            || phase.Records.Count is 0)

            return;

        List<Closed>? ended = null;
        for (int ordinal = 0; ordinal < phase.Order.Count; ++ordinal)
        {
            SimActorKey markTag = phase.Order[ordinal];
            if (!phase.Records.TryGetValue(
                    markTag,
                    out ContactRecord impact))

                continue;
            double age = kineticsMoment - impact.TouchedTime;
            if (!force
                && !(age > 1d)
                && !(impact.Ethereal && age > 0d))

                continue;
            (ended ??= []).Add(new Closed(
                markTag,
                impact.ServerGuid));
        }

        if (ended is null)
            return;

        ulong dossierEpoch = _deliveryEpoch;
        for (int ordinal = 0; ordinal < ended.Count; ++ordinal)
        {
            Closed impact = ended[ordinal];
            phase.Records.Remove(impact.Key);
            phase.Order.Remove(impact.Key);
            UnlinkTouching(impact.Key, holderTag);
        }

        ulong srcSessVer = _actors.SessionLifetimeVersion;
        ulong srcLifespanAlteration =
            _actors.LatestLifespanAlteration(holder.ServerGuid);
        for (int ordinal = 0; ordinal < ended.Count; ++ordinal)
        {
            if (_destroyed
                || dossierEpoch != _deliveryEpoch
                || _actors.SessionLifetimeVersion != srcSessVer
                || _actors.LatestLifespanAlteration(holder.ServerGuid)
                    != srcLifespanAlteration
                || setLocusLotIdent is not 0UL
                    && (holder.PositionAuthorityVersion
                            != anticipatedLocusArbiterVer
                        || !_holders.TryGetValue(
                            holderTag, out HolderLedger? latestHolder)
                        || !ReferenceEquals(latestHolder, phase)
                        || latestHolder.SetLocusLotIdent
                            != setLocusLotIdent))

                break;
            Closed impact = ended[ordinal];
            if (TryParty(
                    impact.Key,
                    out SimActorRecord mark,
                    out KineticBody markCorpus))
            {
                if ((markCorpus.State
                        & KineticStateFlags.ReportAsEnvironment) != 0)

                    continue;
                BroadcastFinishForOnline(
                    holder,
                    holderCorpus,
                    holderTag,
                    mark,
                    markCorpus,
                    impact.Key);
            }
            else
            {
                BroadcastFinishForGone(
                    holder,
                    holderCorpus,
                    holderTag,
                    impact.Key,
                    impact.ServerGuid);
            }
        }
        PruneHolder(holderTag);
    }

    private void BroadcastFinishForOnline(
        SimActorRecord holder,
        KineticBody? holderCorpus,
        SimActorKey holderTag,
        SimActorRecord mark,
        KineticBody markCorpus,
        SimActorKey markTag)
    {
        ulong srcSessVer = _actors.SessionLifetimeVersion;
        ulong srcLifespanAlteration =
            _actors.LatestLifespanAlteration(holder.ServerGuid);
        ulong dossierEpoch = _deliveryEpoch;
        if (holderCorpus is not null
            && (holderCorpus.State & KineticStateFlags.ReportCollisions) != 0
            && PartyIsRecognized(holder, holderCorpus, holderTag))
        {
            Publish(new SimContactNotice(
                UpcomingSeq(),
                SimContactNoticeKind.ObjectCollisionEnd,
                holderTag,
                holder.ServerGuid,
                markTag,
                mark.ServerGuid,
                holderCorpus.InContact,
                markCorpus.InContact));
        }

        if ((markCorpus.State & KineticStateFlags.ReportCollisions) != 0
            && dossierEpoch == _deliveryEpoch
            && !_destroyed
            && _actors.SessionLifetimeVersion == srcSessVer
            && _actors.LatestLifespanAlteration(holder.ServerGuid)
                == srcLifespanAlteration
            && holderCorpus is not null
            && PartyIsRecognized(holder, holderCorpus, holderTag)
            && PartyIsRecognized(mark, markCorpus, markTag))
        {
            Publish(new SimContactNotice(
                UpcomingSeq(),
                SimContactNoticeKind.ObjectCollisionEnd,
                markTag,
                mark.ServerGuid,
                holderTag,
                holder.ServerGuid,
                markCorpus.InContact,
                holderCorpus?.InContact ?? false));
        }
    }

    private void BroadcastFinishForGone(
        SimActorRecord holder,
        KineticBody? holderCorpus,
        SimActorKey holderTag,
        SimActorKey markTag,
        uint markSrvOid)
    {
        if (holderCorpus is null
            || (holderCorpus.State & KineticStateFlags.ReportCollisions) == 0
            || !PartyIsRecognized(holder, holderCorpus, holderTag))

            return;
        Publish(new SimContactNotice(
            UpcomingSeq(),
            SimContactNoticeKind.ObjectCollisionEnd,
            holderTag,
            holder.ServerGuid,
            markTag,
            markSrvOid,
            holderCorpus.InContact,
            OtherWasInContact: false));
    }

    private void HaltMissile(
        SimActorRecord holder,
        KineticBody holderCorpus,
        SimActorKey holderTag,
        bool demandLatestMissile = true)
    {
        if (!PartyIsOnline(holder, holderCorpus, holderTag)
            || !_actors.HaltMissileFollowingImpact(
                holder,
                demandLatestMissile))

            return;
        _proxies.RefreshKineticsPhase(
            holderTag.LocalEntityId,
            (uint)holder.FinalKineticsCondition);
    }
}

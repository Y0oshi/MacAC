using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// Set-position collision batches: staged during placement, installed on commit, delivered after
// the host acknowledges
internal sealed partial class SimContactNoticesLedger
{
    private ulong _lotIdents;

    private ulong _installedLotIdent;

    private readonly HashSet<ulong> _lotsInFlight = [];

    internal enum StagedNoticeEligibility : byte
    {
        Environment,
        Object,
    }

    private sealed record PinnedParty(
        uint LocalEntityId,
        bool IsStatic,
        SimActorRecord? Record,
        KineticBody? Body,
        SimActorKey Key);

    internal sealed record StagedNoticeAction(
        StagedNoticeEligibility Eligibility,
        SimActorRecord Recipient,
        KineticBody RecipientBody,
        SimActorKey RecipientKey,
        SimActorRecord? Other,
        KineticBody? OtherBody,
        SimActorKey? OtherKey,
        bool RecipientContact,
        bool ExactDormantRecipient,
        ulong RecipientPositionAuthorityVersion);

    internal sealed class StagedSetPositionContactBatch
    {
        internal required ulong LotIdent { get; init; }
        internal required ulong AnticipatedAlterationRev { get; init; }
        internal required ulong InstalledAlterationRev { get; init; }
        internal required ulong SessionLifetimeVersion { get; init; }
        internal required SimActorRecord Owner { get; init; }
        internal required KineticBody HolderCorpus { get; init; }
        internal required SimActorKey HolderTag { get; init; }
        internal required ulong HolderLocusArbiterVer { get; init; }
        internal required HolderLedger? HolderRegister { get; init; }
        internal required bool PreviousContact { get; init; }
        internal required bool FinalCollidedWithSurroundings { get; init; }
        internal required bool FinalTerrainRim { get; init; }
        internal required double KineticsMoment { get; init; }
        internal required StagedNoticeAction[] Actions { get; init; }
    }

    internal readonly record struct SetPositionContactBatchStub(
        ulong BatchId,
        SimActorRecord Owner,
        KineticBody OwnerBody,
        SimActorKey OwnerKey,
        ulong OwnerPositionAuthorityVersion,
        bool PreviousContact,
        bool FinalCollidedWithEnvironment,
        bool FinalGroundEdge,
        double PhysicsTime,
        StagedNoticeAction[] Actions)
    {
        internal bool IsValid => BatchId is not 0UL;
    }

    internal bool TryReadySetLocusLot(
        SimActorRecord holder,
        KineticBody holderCorpus,
        double kineticsMoment,
        bool earlierLink,
        bool earlierOnPassable,
        bool finalOnPassable,
        bool collidedWithSurroundings,
        ImmutableArray<uint> collidedObjectIdents,
        out StagedSetPositionContactBatch? readied)
    {
        Live();
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(holderCorpus);
        readied = null;
        SimActorKey holderTag = holder.Key ?? default;
        if (!double.IsFinite(kineticsMoment)
            || holderTag == default
            || !PartyIsRecognized(holder, holderCorpus, holderTag)
            || _leaving.Contains(holderTag)
            || _intakeBlocked.Contains(holderTag))

            return false;

        ulong anticipatedAlteration = _rev;
        ulong installedAlteration = checked(anticipatedAlteration + 1UL);
        ulong sessLifespan = _actors.SessionLifetimeVersion;
        if (collidedObjectIdents.IsDefault)
            collidedObjectIdents = ImmutableArray<uint>.Empty;
        List<PinnedParty> subjects = new List<PinnedParty>(
            collidedObjectIdents.Length);
        for (int ordinal = 0; ordinal < collidedObjectIdents.Length; ++ordinal)
        {
            uint ownIdent = collidedObjectIdents[ordinal];
            if (ownIdent is 0u || ownIdent == holderTag.LocalEntityId)
                continue;
            if (!_proxies.TryFetchImpactHolder(
                    ownIdent,
                    out uint shadePhase,
                    out bool isStatic))

                continue;
            if (isStatic)
            {
                subjects.Add(new PinnedParty(
                    ownIdent,
                    IsStatic: true,
                    Record: null,
                    Body: null,
                    Key: default));
                continue;
            }
            if (!_actors.TryFetchByOwnTag(ownIdent, out SimActorRecord mark)
                || mark.KineticBody is not { } markCorpus
                || !_actors.IsCurrent(mark)
                || mark.Key is not { } markTag)

                continue;
            subjects.Add(new PinnedParty(
                ownIdent,
                IsStatic: false,
                mark,
                markCorpus,
                markTag));
            _ = shadePhase;
        }

        var lined = ReplicateHolder(TryHolder(holderTag));
        var acts = new List<StagedNoticeAction>(subjects.Count);
        void JunctureSurroundings()
        {
            acts.Add(new StagedNoticeAction(
                StagedNoticeEligibility.Environment,
                holder,
                holderCorpus,
                holderTag,
                Other: null,
                OtherBody: null,
                OtherKey: null,
                RecipientContact: earlierLink,
                ExactDormantRecipient: !holderCorpus.InWorld,
                RecipientPositionAuthorityVersion:
                    holder.PositionAuthorityVersion));
        }
        for (int ordinal = 0; ordinal < subjects.Count; ++ordinal)
        {
            PinnedParty subject = subjects[ordinal];
            if (subject.IsStatic)
            {
                JunctureSurroundings();
                continue;
            }
            var mark = subject.Record!;
            KineticBody markCorpus = subject.Body!;
            SimActorKey markTag = subject.Key;
            acts.Add(new StagedNoticeAction(
                StagedNoticeEligibility.Object,
                holder,
                holderCorpus,
                holderTag,
                mark,
                markCorpus,
                markTag,
                RecipientContact: earlierLink,
                ExactDormantRecipient: !holderCorpus.InWorld,
                RecipientPositionAuthorityVersion:
                    holder.PositionAuthorityVersion));
        }

        _holders.EnsureCapacity(_holders.Count + 1);
        _lotsInFlight.EnsureCapacity(
            _lotsInFlight.Count + 1);
        readied = new StagedSetPositionContactBatch
        {
            LotIdent = checked(++_lotIdents),
            AnticipatedAlterationRev = anticipatedAlteration,
            InstalledAlterationRev = installedAlteration,
            SessionLifetimeVersion = sessLifespan,
            Owner = holder,
            HolderCorpus = holderCorpus,
            HolderTag = holderTag,
            HolderLocusArbiterVer = holder.PositionAuthorityVersion,
            HolderRegister = lined.Records.Count is 0
                && !lined.CollidingWithEnvironment
                    ? null
                    : lined,
            PreviousContact = earlierLink,
            FinalCollidedWithSurroundings = collidedWithSurroundings,
            FinalTerrainRim = !earlierOnPassable && finalOnPassable,
            KineticsMoment = kineticsMoment,
            Actions = acts.ToArray(),
        };
        _ = earlierLink;
        return _rev == anticipatedAlteration
            && _actors.SessionLifetimeVersion == sessLifespan
            && PartyIsRecognized(holder, holderCorpus, holderTag);
    }

    internal bool TryInstallSetLocusLot(
        StagedSetPositionContactBatch readied,
        out SetPositionContactBatchStub receipt)
    {
        Live();
        ArgumentNullException.ThrowIfNull(readied);
        receipt = default;
        if (readied.LotIdent <= _installedLotIdent
            || _rev != readied.AnticipatedAlterationRev
            || _actors.SessionLifetimeVersion
                != readied.SessionLifetimeVersion
            || !PartyIsRecognized(
                readied.Owner,
                readied.HolderCorpus,
                readied.HolderTag))

            return false;
        if (!PartyIsRecognized(
                readied.Owner,
                readied.HolderCorpus,
                readied.HolderTag))
            return false;

        if (readied.HolderRegister is null)
            _holders.Remove(readied.HolderTag);
        else
        {
            readied.HolderRegister.SetLocusLotIdent = readied.LotIdent;
            _holders[readied.HolderTag] = readied.HolderRegister;
        }
        _rev = readied.InstalledAlterationRev;
        _installedLotIdent = readied.LotIdent;
        _lotsInFlight.Add(readied.LotIdent);
        receipt = new SetPositionContactBatchStub(
            readied.LotIdent,
            readied.Owner,
            readied.HolderCorpus,
            readied.HolderTag,
            readied.HolderLocusArbiterVer,
            readied.PreviousContact,
            readied.FinalCollidedWithSurroundings,
            readied.FinalTerrainRim,
            readied.KineticsMoment,
            readied.Actions);
        return true;
    }

    internal bool IsReadiedSetLocusLotLatest(
        StagedSetPositionContactBatch readied)
    {
        ArgumentNullException.ThrowIfNull(readied);
        return !_destroyed
            && readied.LotIdent > _installedLotIdent
            && _rev == readied.AnticipatedAlterationRev
            && _actors.SessionLifetimeVersion
                == readied.SessionLifetimeVersion
            && PartyIsRecognized(
                readied.Owner,
                readied.HolderCorpus,
                readied.HolderTag);
    }

    internal bool RelaySetLocusLot(
        in SetPositionContactBatchStub receipt) =>
        RelaySetLocusLotOutcome(receipt).Reported;

    internal SetPositionContactBatchDispatchResult
        RelaySetLocusLotOutcome(
        in SetPositionContactBatchStub receipt)
    {
        if (!receipt.IsValid
            || _destroyed
            || receipt.BatchId > _installedLotIdent
            || !_lotsInFlight.Remove(receipt.BatchId))
        {
            return new(
                SetPositionContactBatchDispatchStatus.RejectedReceipt,
                Reported: false);
        }
        bool reported = false;
        for (int ordinal = 0; ordinal < receipt.Actions.Length; ++ordinal)
        {
            if (LotHolderConcealed(receipt))
            {
                return new(
                    SetPositionContactBatchDispatchStatus.Completed,
                    reported);
            }
            if (receipt.Owner.PositionAuthorityVersion
                    != receipt.OwnerPositionAuthorityVersion
                || _holders.TryGetValue(
                    receipt.OwnerKey, out HolderLedger? latestHolder)
                    && latestHolder.SetLocusLotIdent != receipt.BatchId)
            {
                return new(
                    SetPositionContactBatchDispatchStatus.Displaced,
                    reported);
            }
            var act = receipt.Actions[ordinal];
            if (act.Eligibility is StagedNoticeEligibility.Environment)
            {
                reported |= DeliverSurroundingsAct(
                    act.Recipient,
                    act.RecipientBody,
                    act.RecipientKey,
                    act.RecipientContact,
                    receipt.BatchId);
                continue;
            }
            reported |= DeliverTrackingAct(
                act, receipt.PhysicsTime, receipt.BatchId);
        }

        if (LotHolderConcealed(receipt))
        {
            return new(
                SetPositionContactBatchDispatchStatus.Completed,
                reported);
        }
        if (receipt.Owner.PositionAuthorityVersion
                != receipt.OwnerPositionAuthorityVersion
            || _holders.TryGetValue(
                receipt.OwnerKey, out HolderLedger? suffixHolder)
                && suffixHolder.SetLocusLotIdent != receipt.BatchId)
        {
            return new(
                SetPositionContactBatchDispatchStatus.Displaced,
                reported);
        }

        ShutExpiredLinks(
            receipt.Owner,
            receipt.OwnerBody,
            receipt.OwnerKey,
            receipt.PhysicsTime,
            force: false,
            receipt.BatchId,
            receipt.OwnerPositionAuthorityVersion);
        if (!LotHolderLatest(receipt))
        {
            return new(
                SetPositionContactBatchDispatchStatus.Displaced,
                reported);
        }
        reported |= DeliverSurroundingsSuffix(receipt);
        return new(
            SetPositionContactBatchDispatchStatus.Completed,
            reported);
    }

    internal bool TossSetLocusLot(
        in SetPositionContactBatchStub receipt)
    {
        return receipt.IsValid
        && _lotsInFlight.Remove(receipt.BatchId);
    }

    internal void RetireSetLocusLotHolder(
        in SetPositionContactBatchStub receipt)
    {
        if (!receipt.IsValid || _destroyed)
            return;
        _lotsInFlight.Remove(receipt.BatchId);
        if (!PartyIsRecognized(
                receipt.Owner,
                receipt.OwnerBody,
                receipt.OwnerKey)
            || !_holders.TryGetValue(
                receipt.OwnerKey, out HolderLedger? holder)
            || holder.SetLocusLotIdent != receipt.BatchId)

            return;
        _rev = checked(_rev + 1UL);
        if (!_intakeBlocked.Add(receipt.OwnerKey))
            return;
        try
        {
            ForceFinish(receipt.Owner, receipt.OwnerKey);
            if (_holders.TryGetValue(receipt.OwnerKey, out HolderLedger? latest)
                && ReferenceEquals(latest, holder)
                && latest.SetLocusLotIdent == receipt.BatchId)
            {
                latest.CollidingWithEnvironment = false;
                _holders.Remove(receipt.OwnerKey);
            }
        }
        finally
        {
            _intakeBlocked.Remove(receipt.OwnerKey);
        }
    }

    private bool LotHolderConcealed(
        in SetPositionContactBatchStub receipt)
    {
        return _actors.IsCurrent(receipt.Owner)
        && receipt.Owner.PositionAuthorityVersion
            == receipt.OwnerPositionAuthorityVersion
        && ReferenceEquals(receipt.Owner.KineticBody, receipt.OwnerBody)
        && (receipt.OwnerBody.State & KineticStateFlags.Hidden) != 0;
    }

    private bool DeliverTrackingAct(
        StagedNoticeAction act,
        double kineticsMoment,
        ulong lotIdent)
    {
        if (act.Other is null
            || act.OtherBody is null
            || act.OtherKey is not { } markTag
            || !PartyIsRecognized(
                act.Recipient,
                act.RecipientBody,
                act.RecipientKey)
            || !PartyIsRecognized(
                act.Other,
                act.OtherBody,
                markTag))

            return false;

        var markPhase = act.OtherBody.State;
        if ((markPhase & KineticStateFlags.Static) != 0)
        {
            return DeliverSurroundingsAct(
                act.Recipient,
                act.RecipientBody,
                act.RecipientKey,
                act.RecipientContact,
                lotIdent);
        }

        var phase = HolderFor(act.RecipientKey, lotIdent);
        bool isNew = !phase.Records.ContainsKey(markTag);
        phase.Records[markTag] = new ContactRecord(
            kineticsMoment,
            (markPhase & KineticStateFlags.Ethereal) != 0,
            act.Other.ServerGuid);
        if (!isNew)
        {
            _rev = checked(_rev + 1UL);
            return false;
        }

        phase.Order.Add(markTag);
        ConnectTouching(markTag, act.RecipientKey);
        _rev = checked(_rev + 1UL);
        return NoteObjectLink(
            act.Recipient,
            act.RecipientBody,
            act.RecipientKey,
            act.Other,
            act.OtherBody,
            markTag,
            markPhase,
            act.RecipientContact,
            act.ExactDormantRecipient,
            act.RecipientPositionAuthorityVersion,
            lotIdent);
    }

    private bool DeliverSurroundingsAct(
        SimActorRecord holder,
        KineticBody holderCorpus,
        SimActorKey holderTag,
        bool earlierLink,
        ulong lotIdent)
    {
        if (!PartyIsRecognized(holder, holderCorpus, holderTag))
            return false;
        var phase = HolderFor(holderTag, lotIdent);
        if (phase.CollidingWithEnvironment)
            return false;

        phase.CollidingWithEnvironment = true;
        _rev = checked(_rev + 1UL);
        bool reported = (holderCorpus.State
            & KineticStateFlags.ReportCollisions) != 0;
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

        HaltLinedMissile(
            holder,
            holderCorpus,
            holderTag,
            demandLatestMissile: true);
        return reported;
    }

    private bool DeliverSurroundingsSuffix(
        in SetPositionContactBatchStub receipt)
    {
        if (!LotHolderLatest(receipt)
            || !PartyIsRecognized(
                receipt.Owner,
                receipt.OwnerBody,
                receipt.OwnerKey))

            return false;

        var phase = TryHolder(receipt.OwnerKey);
        if (phase?.CollidingWithEnvironment == true)
        {
            if (phase.CollidingWithEnvironment
                != receipt.FinalCollidedWithEnvironment)
            {
                phase.CollidingWithEnvironment =
                    receipt.FinalCollidedWithEnvironment;
                _rev = checked(_rev + 1UL);
            }
        }
        else if (receipt.FinalCollidedWithEnvironment
                 || receipt.FinalGroundEdge)
        {
            bool reported = DeliverSurroundingsAct(
                receipt.Owner,
                receipt.OwnerBody,
                receipt.OwnerKey,
                receipt.PreviousContact,
                receipt.BatchId);
            PruneHolder(receipt.OwnerKey);
            return reported;
        }
        PruneHolder(receipt.OwnerKey);
        return false;
    }

    private void HaltLinedMissile(
        SimActorRecord holder,
        KineticBody holderCorpus,
        SimActorKey holderTag,
        bool demandLatestMissile)
    {
        if (!PartyIsRecognized(holder, holderCorpus, holderTag)
            || !_actors.HaltMissileFollowingImpact(
                holder,
                demandLatestMissile))

            return;
        _proxies.RefreshKineticsPhase(
            holderTag.LocalEntityId,
            (uint)holder.FinalKineticsCondition);
    }

    private static HolderLedger ReplicateHolder(HolderLedger? src)
    {
        HolderLedger replicate = new HolderLedger();
        if (src is null)
            return replicate;
        foreach ((SimActorKey tag, ContactRecord capture)
                 in src.Records)
            replicate.Records.Add(tag, capture);
        replicate.Order.AddRange(src.Order);
        replicate.CollidingWithEnvironment = src.CollidingWithEnvironment;
        return replicate;
    }

    private bool LotHolderLatest(
        in SetPositionContactBatchStub receipt)
    {
        return receipt.Owner.PositionAuthorityVersion
            == receipt.OwnerPositionAuthorityVersion
        && (!_holders.TryGetValue(receipt.OwnerKey, out HolderLedger? holder)
            || holder.SetLocusLotIdent == receipt.BatchId);
    }
}

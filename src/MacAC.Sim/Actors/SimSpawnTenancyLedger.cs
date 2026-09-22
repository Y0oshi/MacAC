using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal sealed partial class SimSpawnTenancyLedger
{
    private sealed class Tenancy
    {
        internal required SimActorRecord Record { get; init; }
        internal required SimSpawnTenancyLease Lease { get; set; }
    }

    private readonly SimActorIndex _actors;

    private readonly SimSetPositionLedger _stances;

    private readonly Dictionary<SimActorKey, Tenancy> _tenancies = [];

    private Func<SimEpochTicket>? _gen;

    private readonly List<Action<SimActorKey>> _sunsetTaps = [];

    private ulong _tenancyIdents;

    internal SimSpawnTenancyLedger(
        SimActorIndex entities,
        SimSetPositionLedger setPosition)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _stances = setPosition
            ?? throw new ArgumentNullException(nameof(setPosition));
    }

    internal void AttachGen(Func<SimEpochTicket> gen)
    {
        ArgumentNullException.ThrowIfNull(gen);
        if (_gen is not null)
        {
            throw new InvalidOperationException(
                "The initial Create residence generation source is by now bound");
        }
        _gen = gen;
    }

    internal void AttachSunsetNotification(Action<SimActorKey> alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        _sunsetTaps.Add(alert);
    }

    internal bool CanAdmitBuild(RealmSession.MoverSpawn incoming)
    {
        bool parented = (incoming.ParentGuid
                ?? incoming.Physics?.Parent?.Guid)
            is not null and not 0u;
        bool topTier = !parented
            && incoming.Position is { LandblockId: not 0u };
        return EpochInstant().Value is not 0UL
            && _tenancyIdents != ulong.MaxValue
            && (!topTier
                || _stances.CanCommenceAuthoredStanceSeries
                    && SimSovereignPositionRouteSorter
                        .IsValidBuildWireLocus(
                            incoming.Position!.Value));
    }

    internal SimSpawnTenancyLease Begin(
        SimActorRecord capture,
        in IncomingBuildOutcome approved,
        bool isOwnAvatar)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_actors.IsCurrent(capture)
            || capture.Key is not { } tag
            || capture.WholeChamberTag is not 0u
            || approved.Snapshot.Guid != capture.ServerGuid
            || approved.Snapshot.InstanceSequence != capture.Incarnation)

            return default;

        var gen = EpochInstant();
        SimPositionActorKind actorSort = isOwnAvatar
            ? SimPositionActorKind.LocalPlayer
            : (capture.FinalKineticsCondition & KineticStateFlags.Missile) != 0
                ? SimPositionActorKind.Projectile
                : SimPositionActorKind.Remote;
        var snapshot = approved.Snapshot;
        SimCreateTenancyKind residence =
            (snapshot.ParentGuid ?? snapshot.Physics?.Parent?.Guid)
                is not null and not 0u
                ? SimCreateTenancyKind.Parented
                : snapshot.Position is { LandblockId: not 0u }
                    ? SimCreateTenancyKind.TopLevel
                    : SimCreateTenancyKind.PickedUp;
        var arbiter = new SimSovereignPositionAuthority(
            gen,
            tag,
            capture.PositionAuthorityVersion,
            snapshot.PositionSequence,
            approved.Timestamps.Teleport,
            approved.Timestamps.Teleport,
            PoseStampVerdict.Apply);
        var course =
            SimSovereignPositionRouteSorter.ClassifyBuild(
                new SimCreatePositionRouteRequest(
                    arbiter,
                    actorSort,
                    residence,
                    snapshot.Position,
                    new SimPositionPlacementFacts(
                        capture.FinalKineticsCondition,
                        HasAuthoredMoverShape: snapshot.SetupTableId is not null)));
        return Adopt(capture, course);
    }

    internal SimSpawnTenancyLease QueueApproved(
        SimActorRecord capture,
        in SimSpawnTenancyLease preceding,
        SimSpawnFollowupKind sort,
        SimGrantedPositionSource locusSrc,
        ImmutableArray<SimSpawnTailAction> acts)
    {
        var possessed = ImmutableArray.CreateBuilder<
            SimSpawnTailAction>(acts.Length);
        foreach (SimSpawnTailAction act in acts)
        {
            possessed.Add(SimSpawnIntakeLocker.Freeze(act));
        }
        return Queue(
            capture,
            preceding,
            new SimSpawnTenancyFollowup(
                FollowupSeriesFollowing(preceding),
                sort,
                capture.Incarnation,
                locusSrc,
                possessed.MoveToImmutable()));
    }

    internal bool CanQueue(
        SimActorRecord capture,
        in SimSpawnTenancyLease preceding)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return false;
        if (_tenancies.TryGetValue(tag, out Tenancy? tenancy))
        {
            return ReferenceEquals(tenancy.Record, capture)
                && tenancy.Lease.Token == preceding.Token
                && TenancyHolds(tenancy)
                && tenancy.Lease.Continuations.Length < int.MaxValue;
        }
        return _completed.TryGetValue(tag, out Finished? finished)
            && ReferenceEquals(finished.Record, capture)
            && finished.Lease.Token == preceding.Token
            && FinishedHolds(finished)
            && finished.Lease.Continuations.Length < int.MaxValue
            && finished.Receipt.Adoption.Revision < ulong.MaxValue;
    }

    internal bool TryFetchTransaction(
        SimActorRecord capture,
        out SimSpawnTenancyLease tenancy)
    {
        if (TryFetchLatest(capture, out tenancy))
            return true;
        if (capture.Key is { } tag
            && _completed.TryGetValue(tag, out Finished? finished)
            && ReferenceEquals(finished.Record, capture))
        {
            if (FinishedHolds(finished))
            {
                tenancy = finished.Lease;
                return true;
            }
            Retire(finished);
        }
        tenancy = default;
        return false;
    }

    internal bool TryFetchLatest(
        SimActorRecord capture,
        out SimSpawnTenancyLease lease)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is { } tag
            && _tenancies.TryGetValue(tag, out Tenancy? tenancy)
            && ReferenceEquals(tenancy.Record, capture))
        {
            if (TenancyHolds(tenancy))
            {
                lease = tenancy.Lease;
                return true;
            }
            Retire(tenancy);
        }
        lease = default;
        return false;
    }

    internal bool AdvanceExecutorBaseline(
        SimActorRecord capture,
        in SimSpawnTenancyTicket ticket,
        SimRunnerBaselineFields fields)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!ticket.IsValid
            || !_completed.TryGetValue(ticket.Entity, out Finished? tenancy)
            || !ReferenceEquals(tenancy.Record, capture)
            || tenancy.Receipt.Token != ticket)

            return false;
        if ((fields & SimRunnerBaselineFields.PositionAuthorityVersion) != 0)
            tenancy.AnticipatedLocusArbiterVer = capture.PositionAuthorityVersion;
        if ((fields & SimRunnerBaselineFields.CreateIntegrationVersion) != 0)
            tenancy.AnticipatedBuildIntegrationVer = capture.BuildIntegrationVersion;
        if ((fields & SimRunnerBaselineFields.FullCellId) != 0)
            tenancy.AnticipatedWholeChamberIdent = capture.WholeChamberTag;
        if ((fields & SimRunnerBaselineFields.PlacementCommitVersion) != 0)
            tenancy.AnticipatedStanceSealVer = capture.PlacementCommitVersion;
        return true;
    }

    internal bool AcknowledgeAdoption(
        SimActorRecord capture,
        in SimSpawnTenancyAdoptionTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!ticket.IsValid)
            return false;
        if (!_completed.TryGetValue(ticket.Entity, out Finished? latest)
            || !ReferenceEquals(latest.Record, capture)
            || latest.Receipt.Adoption != ticket)

            return false;
        if (!FinishedHolds(latest))
        {
            Retire(latest);
            return false;
        }
        if (!latest.Lease.Continuations.IsEmpty)
            return false;
        if (latest.Lease.Route.PerformsSetLocus
            && !latest.StanceAdopted
            && !_stances.AbsorbAcknowledgedStance(
                latest.Lease.Placement,
                latest.Receipt.Projection))

            return false;
        return _completed.Remove(ticket.Entity);
    }

    internal bool TryTranslateToCellessCourse(SimActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag
            || !_tenancies.TryGetValue(tag, out Tenancy? tenancy)
            || !ReferenceEquals(tenancy.Record, capture))

            return false;
        if (!TenancyHolds(tenancy))
        {
            Retire(tenancy);
            return false;
        }
        var lease = tenancy.Lease;
        if (!lease.Route.PerformsSetLocus)
            return true;
        if (capture.WholeChamberTag is not 0u)
            return false;
        var abort =
            _stances.DropPreciseStance(lease.Placement);
        tenancy.Lease = lease with
        {
            Route = SimSovereignPositionRouteSorter
                .ToCellessBuildCourse(lease.Route),
            Placement = default,
        };
        _stances.BroadcastAbort(abort);
        ProclaimSunset(tag);
        return true;
    }

    internal bool Drop(
        SimActorRecord capture,
        out SimSpawnTenancyLease lease,
        out SimPlacementAbortStub abort)
    {
        ArgumentNullException.ThrowIfNull(capture);
        abort = default;
        if (capture.Key is { } tag
            && _tenancies.TryGetValue(tag, out Tenancy? tenancy)
            && ReferenceEquals(tenancy.Record, capture)
            && _tenancies.Remove(tag))
        {
            lease = tenancy.Lease;
            abort = _stances.DropPreciseStance(
                lease.Placement);
            ProclaimSunset(tag);
            return true;
        }
        if (capture.Key is { } finishedTag
            && _completed.TryGetValue(
                finishedTag,
                out Finished? finished)
            && ReferenceEquals(finished.Record, capture)
            && _completed.Remove(finishedTag))
        {
            lease = finished.Lease;
            abort = _stances.DropPreciseStance(
                lease.Placement);
            ProclaimSunset(finishedTag);
            return true;
        }
        lease = default;
        return false;
    }

    internal void Clear()
    {
        Tenancy[] engaged = _tenancies.Values.ToArray();
        Finished[] finished = _completed.Values.ToArray();
        SimPlacementAbortStub[] cancellations = new SimPlacementAbortStub[
            engaged.Length + finished.Length];
        _tenancies.Clear();
        _completed.Clear();
        int abortTally = 0;
        foreach (Tenancy tenancy in engaged)
        {
            var abort =
                _stances.DropPreciseStance(
                    tenancy.Lease.Placement);
            if (abort.IsValid)
                cancellations[abortTally++] = abort;
        }
        foreach (Finished tenancy in finished)
        {
            var abort =
                _stances.DropPreciseStance(
                    tenancy.Lease.Placement);
            if (abort.IsValid)
                cancellations[abortTally++] = abort;
        }
        for (int ordinal = 0; ordinal < abortTally; ++ordinal)
        {
            _stances.BroadcastAbort(cancellations[ordinal]);
        }
        foreach (Tenancy tenancy in engaged)
            ProclaimSunset(tenancy.Lease.Token.Entity);
        foreach (Finished tenancy in finished)
            ProclaimSunset(tenancy.Receipt.Token.Entity);
    }

    internal SimSpawnTenancyHoldingCapture GrabOwnership() =>
        new(_tenancies.Count, _completed.Count, _tenancyIdents);

    private void ProclaimSunset(SimActorKey tag)
    {
        foreach (Action<SimActorKey> alert in _sunsetTaps.ToArray())
            alert(tag);
    }

    private SimSpawnTenancyLease Queue(
        SimActorRecord capture,
        in SimSpawnTenancyLease preceding,
        in SimSpawnTenancyFollowup continuation)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!continuation.IsValid
            || continuation.InstanceSequence != capture.Incarnation
            || !CanQueue(capture, preceding))

            return default;

        SimActorKey tag = capture.Key!.Value;
        Tenancy? engaged = null;
        Finished? finished = null;
        SimSpawnTenancyLease latest;
        if (_tenancies.TryGetValue(tag, out engaged))
            latest = engaged.Lease;
        else if (_completed.TryGetValue(tag, out finished))
            latest = finished.Lease;
        else
            return default;

        if (continuation.Sequence != FollowupSeriesFollowing(latest))
            return default;
        var revised = latest with
        {
            Continuations = latest.Continuations.Add(continuation),
        };
        if (engaged is not null)
        {
            engaged.Lease = revised;
        }
        else
        {
            finished!.Lease = revised;
            var adoption =
                finished.Receipt.Adoption with
                {
                    Revision = finished.Receipt.Adoption.Revision + 1UL,
                };
            finished.Receipt = finished.Receipt with
            {
                Adoption = adoption,
                Continuations = revised.Continuations,
            };
        }
        return revised;
    }

    private static ulong FollowupSeriesFollowing(
        in SimSpawnTenancyLease tenancy) =>
        (ulong)tenancy.Continuations.Length + 1UL;

    private SimSpawnTenancyLease Adopt(
        SimActorRecord capture,
        in SimSovereignPositionRoute course)
    {
        SimActorKey tag = capture.Key!.Value;
        if (_tenancies.ContainsKey(tag)
            || _tenancyIdents == ulong.MaxValue)

            return default;
        ulong tenancyIdent = _tenancyIdents + 1UL;

        SimActorPlacementTicket stance = default;
        if (course.PerformsSetLocus)
        {
            stance = _stances.TryCommenceExclusiveAuthoredStance(
                capture,
                capture.PositionAuthorityVersion,
                course.OperationKind);
            if (!stance.IsValid)
                return default;
            if (!_stances.MonitorStanceWrapUp(stance))
            {
                _ = _stances.DropPreciseStance(stance);
                return default;
            }
        }
        else if (!course.Accepted)
        {
            return default;
        }

        SimSpawnTenancyTicket ticket = new SimSpawnTenancyTicket(
            tag,
            tenancyIdent,
            _actors.SessionLifetimeVersion,
            capture.PositionAuthorityVersion,
            capture.BuildIntegrationVersion,
            capture.PlacementCommitVersion);
        SimSpawnTenancyLease tenancy = new SimSpawnTenancyLease(
            ticket,
            course,
            stance,
            SimSpawnIntakeLocker.Freeze(capture.Snapshot),
            ImmutableArray<SimSpawnTenancyFollowup>.Empty);
        _tenancies.Add(tag, new Tenancy
        {
            Record = capture,
            Lease = tenancy,
        });
        _tenancyIdents = tenancyIdent;
        return tenancy;
    }

    private bool TenancyHolds(Tenancy tenancy)
    {
        var ticket = tenancy.Lease.Token;
        bool stanceLatest = !tenancy.Lease.Route.PerformsSetLocus
            || _stances.IsStanceWrapUpFollowed(
                tenancy.Lease.Placement);
        return stanceLatest
            && _actors.IsCurrent(tenancy.Record)
            && tenancy.Record.Key == ticket.Entity
            && _actors.SessionLifetimeVersion
                == ticket.SessionLifetimeVersion
            && tenancy.Record.PositionAuthorityVersion
                == ticket.PositionAuthorityVersion
            && tenancy.Record.BuildIntegrationVersion
                == ticket.CreateIntegrationVersion
            && tenancy.Lease.Route.Authority.Generation
                == EpochInstant();
    }

    private SimEpochTicket EpochInstant() => _gen?.Invoke() ?? default;

    private void Retire(Tenancy tenancy)
    {
        SimActorKey tag = tenancy.Lease.Token.Entity;
        _tenancies.Remove(tag);
        var abort =
            _stances.DropPreciseStance(tenancy.Lease.Placement);
        _stances.BroadcastAbort(abort);
        ProclaimSunset(tag);
    }

    private void Retire(Finished tenancy)
    {
        SimActorKey tag = tenancy.Receipt.Token.Entity;
        _completed.Remove(tag);
        var abort =
            _stances.DropPreciseStance(tenancy.Lease.Placement);
        _stances.BroadcastAbort(abort);
        ProclaimSunset(tag);
    }
}

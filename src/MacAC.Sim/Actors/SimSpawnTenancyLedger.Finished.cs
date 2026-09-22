using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

// Completion: a tenancy becomes a finished entry whose placement the host adopts before the runner
// is released
internal sealed partial class SimSpawnTenancyLedger
{
    private sealed class Finished
    {
        internal required SimActorRecord Record { get; init; }
        internal required SimSpawnTenancyLease Lease { get; set; }
        internal required SimSpawnTenancyStub Receipt { get; set; }

        internal bool StanceAdopted { get; set; }

        internal ulong AnticipatedLocusArbiterVer { get; set; }
        internal ulong AnticipatedBuildIntegrationVer { get; set; }
        internal uint AnticipatedWholeChamberIdent { get; set; }
        internal ulong AnticipatedStanceSealVer { get; set; }
    }

    private readonly Dictionary<SimActorKey, Finished> _completed = [];

    internal SimSpawnTenancyFinishStatus Complete(
        SimActorRecord capture,
        in SimSpawnTenancyTicket ticket,
        out SimSpawnTenancyStub receipt)
    {
        ArgumentNullException.ThrowIfNull(capture);
        receipt = default;
        if (ticket.IsValid
            && _completed.TryGetValue(ticket.Entity, out Finished? finished)
            && finished.Receipt.Token == ticket
            && ReferenceEquals(finished.Record, capture))
        {
            if (FinishedHolds(finished))
            {
                receipt = finished.Receipt;
                return SimSpawnTenancyFinishStatus.Completed;
            }
            Retire(finished);
            return SimSpawnTenancyFinishStatus
                .RejectedAuthority;
        }
        if (!ticket.IsValid
            || !_tenancies.TryGetValue(ticket.Entity, out Tenancy? tenancy)
            || tenancy.Lease.Token != ticket
            || !ReferenceEquals(tenancy.Record, capture))

            return SimSpawnTenancyFinishStatus.RejectedToken;
        if (!TenancyHolds(tenancy))
        {
            Retire(tenancy);
            return SimSpawnTenancyFinishStatus
                .RejectedAuthority;
        }

        var lease = tenancy.Lease;
        SimPlacementMirrorTicket proj = default;
        if (lease.Route.PerformsSetLocus)
        {
            if (_stances.IsStanceLatest(lease.Placement))
            {
                return SimSpawnTenancyFinishStatus
                    .PendingPlacement;
            }
            if (!_stances.TryGlimpseAcknowledgedStance(
                    lease.Placement,
                    out proj)
                || proj.Entity != ticket.Entity
                || proj.PositionAuthorityVersion
                    != ticket.PositionAuthorityVersion
                || proj.SessionLifetimeVersion
                    != ticket.SessionLifetimeVersion
                || proj.ExactCellId is 0u
                || proj.ExactCellId != capture.WholeChamberTag
                || proj.PlacementCommitVersion
                    <= ticket.SourcePlacementCommitVersion
                || proj.PlacementCommitVersion
                    != capture.PlacementCommitVersion)
            {
                Retire(tenancy);
                return SimSpawnTenancyFinishStatus
                    .RejectedAuthority;
            }
        }
        else if (capture.WholeChamberTag is not 0u)
        {
            Retire(tenancy);
            return SimSpawnTenancyFinishStatus
                .RejectedAuthority;
        }

        var adoption = new SimSpawnTenancyAdoptionTicket(
            ticket.Entity,
            ticket.LeaseId,
            ticket.LeaseId,
            ticket.SessionLifetimeVersion,
            Revision: 1UL);
        receipt = new SimSpawnTenancyStub(
            ticket,
            lease.Route.TeleportHookPhase,
            proj,
            capture.WholeChamberTag,
            capture.PlacementCommitVersion,
            adoption,
            lease.Continuations);
        _tenancies.Remove(ticket.Entity);
        _completed.Add(ticket.Entity, new Finished
        {
            Record = capture,
            Lease = lease,
            Receipt = receipt,
            AnticipatedLocusArbiterVer = ticket.PositionAuthorityVersion,
            AnticipatedBuildIntegrationVer = ticket.CreateIntegrationVersion,
            AnticipatedWholeChamberIdent = receipt.FullCellId,
            AnticipatedStanceSealVer = receipt.PlacementCommitVersion,
        });
        return SimSpawnTenancyFinishStatus.Completed;
    }

    internal bool AdoptFinishedStance(
        SimActorRecord capture,
        in SimSpawnTenancyTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!ticket.IsValid
            || !_completed.TryGetValue(ticket.Entity, out Finished? tenancy)
            || !ReferenceEquals(tenancy.Record, capture)
            || tenancy.Receipt.Token != ticket)

            return false;
        if (tenancy.StanceAdopted)
            return FinishedHolds(tenancy);
        if (!FinishedHolds(tenancy))
        {
            Retire(tenancy);
            return false;
        }
        if (!tenancy.Lease.Route.PerformsSetLocus)
        {
            tenancy.StanceAdopted = true;
            return true;
        }
        if (!_stances.AbsorbAcknowledgedStance(
                tenancy.Lease.Placement,
                tenancy.Receipt.Projection))

            return false;
        tenancy.StanceAdopted = true;
        return true;
    }

    internal SimSpawnTenancyRunnerReleaseStatus AbsorbExecuted(
        SimActorRecord capture,
        in SimSpawnTenancyAdoptionTicket ticket,
        ulong executedThroughSeries)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!ticket.IsValid
            || !_completed.TryGetValue(ticket.Entity, out Finished? tenancy)
            || !ReferenceEquals(tenancy.Record, capture)
            || tenancy.Receipt.Adoption.Entity != ticket.Entity
            || tenancy.Receipt.Adoption.LeaseId != ticket.LeaseId)
        {
            return SimSpawnTenancyRunnerReleaseStatus
                .RejectedToken;
        }
        if (!FinishedHolds(tenancy))
        {
            Retire(tenancy);
            return SimSpawnTenancyRunnerReleaseStatus
                .RejectedAuthority;
        }
        if (tenancy.Receipt.Adoption.Revision != ticket.Revision)

            return SimSpawnTenancyRunnerReleaseStatus.Revised;
        if (!tenancy.StanceAdopted && tenancy.Lease.Route.PerformsSetLocus)
        {
            throw new InvalidOperationException(
                "Executor release needs the initial placement to have been adopted first");
        }
        if ((ulong)tenancy.Lease.Continuations.Length != executedThroughSeries)
        {
            return SimSpawnTenancyRunnerReleaseStatus
                .RejectedAuthority;
        }
        return _completed.Remove(ticket.Entity)
            ? SimSpawnTenancyRunnerReleaseStatus.Released
            : SimSpawnTenancyRunnerReleaseStatus
                .RejectedAuthority;
    }

    private bool FinishedHolds(Finished tenancy)
    {
        var receipt = tenancy.Receipt;
        return _actors.IsCurrent(tenancy.Record)
            && tenancy.Record.Key == receipt.Token.Entity
            && _actors.SessionLifetimeVersion
                == receipt.Token.SessionLifetimeVersion
            && tenancy.Record.PositionAuthorityVersion
                == tenancy.AnticipatedLocusArbiterVer
            && tenancy.Record.BuildIntegrationVersion
                == tenancy.AnticipatedBuildIntegrationVer
            && tenancy.Record.WholeChamberTag == tenancy.AnticipatedWholeChamberIdent
            && tenancy.Record.PlacementCommitVersion
                == tenancy.AnticipatedStanceSealVer
            && tenancy.Lease.Route.Authority.Generation
                == EpochInstant()
            && receipt.Token.SessionLifetimeVersion
                == receipt.Adoption.SessionLifetimeVersion
            && receipt.Token.LeaseId == receipt.Adoption.LeaseId
            && (!tenancy.Lease.Route.PerformsSetLocus
                || tenancy.StanceAdopted
                || _stances.IsStanceWrapUpFollowed(
                    tenancy.Lease.Placement));
    }
}

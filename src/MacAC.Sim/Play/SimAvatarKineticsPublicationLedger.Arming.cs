using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Play;

internal sealed partial class SimAvatarKineticsPublicationLedger
{
    internal SimAvatarKineticsArmingStatus EvaluateActivation(
        in SimAvatarKineticsArmingTicket ticket,
        out SimAvatarKineticsArmingStub receipt)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        receipt = default;
        if (!ticket.IsValid
            || _arming is not { } arming
            || arming.Token != ticket)

            return SimAvatarKineticsArmingStatus.RejectedToken;
        if (!ArmingStillLatest(arming))
        {
            TossActivation();
            return SimAvatarKineticsArmingStatus.RejectedAuthority;
        }
        if (!_physics.SetPosition.TryEvaluateDormantOwnActivation(
                arming.Record,
                arming.Body,
                ticket.Placement,
                arming.StanceDirective,
                out SimIdleSetPositionEvaluation stance))
        {
            if (ReferenceEquals(_arming, arming)
                && ArmingStillLatest(arming)
                && _physics.SetPosition.IsDormantOwnActivationExpectingChamber(
                    arming.Record,
                    arming.Body,
                    ticket.Placement,
                    arming.StanceDirective))

                return SimAvatarKineticsArmingStatus.DeferredCell;
            if (ReferenceEquals(_arming, arming)
                && ArmingStillLatest(arming)
                && _physics.SetPosition.IsDormantOwnActivationTenancyLatest(
                    arming.Record,
                    arming.Body,
                    ticket.Placement,
                    arming.StanceDirective))

                return SimAvatarKineticsArmingStatus.DeferredCell;
            if (ReferenceEquals(_arming, arming)
                && !ArmingStillLatest(arming))

                TossActivation();
            return SimAvatarKineticsArmingStatus.RejectedAuthority;
        }

        receipt = new SimAvatarKineticsArmingStub(
            ticket,
            checked(++_evaluationIdents),
            stance);
        arming.Receipt = receipt;
        if (stance.Result.IsPostponed)
            return SimAvatarKineticsArmingStatus.DeferredCell;
        return stance.Result.IsSealed
            ? SimAvatarKineticsArmingStatus.Evaluated
            : SimAvatarKineticsArmingStatus.RejectedPlacement;
    }

    internal bool IsEvaluationLatest(
        in SimAvatarKineticsArmingStub receipt)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!receipt.IsValid
            || _arming is not { } arming
            || arming.Token != receipt.Token
            || arming.Receipt != receipt)

            return false;
        return ArmingStillLatest(arming)
            && _physics.SetPosition.IsDormantOwnEvaluationLatest(
                arming.Record,
                arming.Body,
                receipt.Placement);
    }

    internal SimIdleSetPositionCommitStatus SealActivation(
        in SimAvatarKineticsArmingStub receipt,
        out SimPlacementMirrorTicket proj)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        proj = default;
        if (!receipt.IsValid
            || _arming is not { } arming
            || arming.Token != receipt.Token
            || arming.Receipt != receipt)

            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        if (arming.QueuedFinalSeal.Status
            is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation)
        {
            return CloseArming(
                arming,
                arming.QueuedFinalSeal,
                out proj);
        }
        if (!ArmingStillLatest(arming)
            || !_physics.SetPosition.IsDormantOwnEvaluationLatest(
                arming.Record,
                arming.Body,
                receipt.Placement))
        {
            arming.Receipt = default;
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }

        bool provenShapeless = arming.ActivationPrep
            .ShadowDisposition
            is SimAvatarProxyVerdict.ProvenShapeless;
        if (!_physics.SetPosition.TryReadyDormantOwnActivationSeal(
                arming.Record,
                arming.Body,
                receipt.Placement,
                provenShapeless,
                out StagedIdleSetPositionCommit? readied)
            || readied is null
            || !ArmingStillLatest(arming))
        {
            arming.Receipt = default;
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }

        if (!_physics.SetPosition.TryEnactDormantOwnActivationSeal(
                arming.Record,
                arming.Body,
                arming.Driver,
                arming.PhysicsHost,
                readied,
                out SimIdleSetPositionCommitStub committed))

            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        if (committed.Status is SimIdleSetPositionCommitStatus.DeferredCell)
        {
            arming.Receipt = default;
            _physics.SetPosition.RelayDormantOwnActivationShade(committed);
            return committed.Status;
        }

        try
        {
            arming.Driver.CommenceDormantSetLocusTerrainStage();
            if (committed.HitGround)
                arming.Movement.HitGround();
            else if (committed.LeaveGround)
                arming.Locomotion.LeaveGround();
        }
        catch
        {
            ++_armingMisses;
        }
        finally
        {
            arming.Driver.FinishDormantSetLocusTerrainStage();
        }
        if (committed.Status is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation
            && (!PrephaseEnvelopeHolds(arming, committed)
                || !RereadIdleVector(arming, committed)
                || !RereadIdlePhase(arming)
                || !_physics.SetPosition.SealDormantOwnActivationPostTerrain(
                    arming.Record,
                    arming.Body,
                    committed)))
        {
            AbandonArming(
                arming, committed, impactAlreadyDispatched: false);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        try
        {
            var impactRelay =
                _physics.SetPosition.RelayDormantOwnActivationImpact(
                    committed);
            if (impactRelay.Status
                is not SetPositionContactBatchDispatchStatus.Completed)
            {
                AbandonArming(
                    arming, committed, impactAlreadyDispatched: true);
                return SimIdleSetPositionCommitStatus.RejectedAuthority;
            }
        }
        catch
        {
            ++_armingMisses;
            AbandonArming(
                arming, committed, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        if (!ResponseEnvelopeHolds(arming, committed))
        {
            AbandonArming(
                arming, committed, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        arming.Driver.RenewDormantCoreKineticsPhase(
            arming.Record.FinalKineticsCondition,
            recalculateAcceleration: false);
        _ = RereadIdleVector(arming, committed);
        if (committed.Status is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation
                && !PrephaseEnvelopeHolds(arming, committed)
            || !_physics.SetPosition.SealDormantOwnActivationPostImpact(
                arming.Record,
                arming.Body,
                committed))
        {
            AbandonArming(
                arming, committed, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        if (committed.Status is SimIdleSetPositionCommitStatus
                .RejectedPlacement)
        {
            arming.Receipt = default;
            arming.QueuedFinalSeal = committed;
            return committed.Status;
        }
        arming.QueuedFinalSeal = committed;
        return CloseArming(arming, committed, out proj);
    }

    internal bool TossActivation(
        in SimAvatarKineticsArmingTicket ticket)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!ticket.IsValid
            || _arming is not { } arming
            || arming.Token != ticket)

            return false;
        TossActivation();
        return true;
    }

    private SimIdleSetPositionCommitStatus CloseArming(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub prephase,
        out SimPlacementMirrorTicket proj)
    {
        proj = default;
        if (!PrephaseEnvelopeHolds(arming, prephase))
        {
            AbandonArming(
                arming, prephase, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        arming.Driver.RenewDormantCoreKineticsPhase(
            arming.Record.FinalKineticsCondition,
            recalculateAcceleration: false);
        bool provenShapeless = arming.ActivationPrep
            .ShadowDisposition is SimAvatarProxyVerdict.ProvenShapeless;
        if (!_physics.SetPosition.TryReadyDormantOwnActivationFinalSeal(
                arming.Record,
                arming.Body,
                prephase,
                provenShapeless,
                out StagedIdleArmingFinalCommit? readied)
            || readied is null)
        {
            if (PrephaseEnvelopeHolds(arming, prephase))
                return prephase.Status;
            AbandonArming(
                arming, prephase, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        if (!PrephaseEnvelopeHolds(arming, prephase))
        {
            AbandonArming(
                arming, prephase, impactAlreadyDispatched: true);
            return SimIdleSetPositionCommitStatus.RejectedAuthority;
        }
        if (!_physics.SetPosition.TryEnactDormantOwnActivationFinalSeal(
                arming.Record,
                arming.Body,
                arming.Driver,
                arming.PhysicsHost,
                prephase,
                readied,
                out SimIdleSetPositionCommitStub committed))

            return prephase.Status;
        arming.Receipt = default;
        arming.QueuedFinalSeal = default;
        _arming = null;
        _physics.SetPosition.RelayDormantOwnActivationShade(committed);
        if (!SealedSuffixHolds(arming, committed))
            return committed.Status;
        LeashLeadListing(arming);
        SettleLeadListingLink(arming);
        _physics.SetPosition.RelayDormantOwnActivationStance(committed);
        proj = committed.Projection.Token;
        return committed.Status;
    }

    private void LeashLeadListing(ArmingRun arming)
    {
        try
        {
            arming.Driver.ArmConstraintLeashAtSealedStance();
        }
        catch
        {
            ++_armingMisses;
        }
    }

    private void SettleLeadListingLink(ArmingRun arming)
    {
        try
        {
            _ = SpawnSettler.TrySettle(
                _physics.Engine,
                arming.Body,
                arming.Body.Position,
                arming.Body.CellPosition.ObjCellId,
                arming.ActivationPrep.Radius,
                arming.ActivationPrep.Height,
                MoverState.IsPlayer
                    | MoverState.EdgeSlide
                    | arming.Driver.OwnPvpFlagSet,
                arming.Driver.OwnEntityId,
                arming.Movement.HitGround,
                arming.Locomotion.LeaveGround);
        }
        catch
        {
            ++_armingMisses;
        }
    }

    private bool PrephaseEnvelopeHolds(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub receipt)
    {
        return OwnershipEnvelopeHolds(arming)
        && _physics.IsImpactEvaluationFatalArbiterLatest(
            receipt.CollisionAuthority)
        && _physics.SetPosition.IsDormantOwnActivationPrephaseLatest(
            arming.Record,
            arming.Body,
            receipt);
    }

    private bool ResponseEnvelopeHolds(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub receipt)
    {
        return OwnershipEnvelopeHolds(arming)
        && _physics.IsImpactEvaluationFatalArbiterLatest(
            receipt.CollisionAuthority)
        && _physics.SetPosition.IsDormantOwnActivationResponseLatest(
            arming.Record,
            arming.Body,
            receipt);
    }

    private bool OwnershipEnvelopeHolds(
        ArmingRun arming)
    {
        return arming.Driver.IsCorePossessedDormant
        && !arming.Driver.IsDormantSetLocusTerrainStageEngaged
        && arming.Driver.OwnsKineticsCorpus(arming.Body)
        && _actors.SessionLifetimeVersion
            == arming.Token.SessionGenerationAuthority
        && _actors.IsCurrent(arming.Record)
        && arming.Record.Key == arming.Token.Entity
        && !_identity.IsDisposed
        && _identity.ServerGuid == arming.Token.LocalPlayerServerGuid
        && _identity.ServerGuid == arming.Record.ServerGuid
        && _identity.Revision == arming.Token.LocalPlayerIdentityRevision
        && arming.Record.KineticsOwnershipEpoch
            == arming.Token.PhysicsOwnershipEpoch
        && arming.Record.ObjectTimerEpoch
            == arming.Token.ObjectClockEpoch
        && _movement.CanSealCorePossessedDriver(
            arming.Token.ControllerOwnershipEpoch,
            arming.Driver)
        && ReferenceEquals(arming.Record.KineticBody, arming.Body)
        && arming.Record.PhysicsHost is null
        && arming.Record.PeerMotion is null
        && arming.Record.Projectile is null
        && !arming.Record.KineticsCorpusAcquisitionInHeadway
        && !arming.Record.DistantLocomotionMappingInHeadway
        && !arming.Record.MissileMappingInHeadway
        && !arming.Record.RequiresDistantStanceCore
        && !arming.Record.EraseApprovedForTeardown;
    }

    private static bool RereadIdlePhase(ArmingRun arming)
    {
        arming.Driver.RenewDormantCoreKineticsPhase(
            arming.Record.FinalKineticsCondition,
            recalculateAcceleration: false);
        return true;
    }

    private static bool RereadIdleVector(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub receipt)
    {
        if (arming.Record.VectorArbiterVer
            == receipt.SourceVectorAuthorityVersion)
            return true;
        var kinetics = arming.Record.Snapshot.Physics;
        arming.Driver.RenewDormantCoreVector(
            kinetics?.Velocity,
            kinetics?.AngularVelocity);
        return true;
    }

    private void AbandonArming(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub receipt,
        bool impactAlreadyDispatched)
    {
        _physics.SetPosition.RetireDormantOwnActivation(
            receipt,
            impactAlreadyDispatched);
        arming.Receipt = default;
        arming.QueuedFinalSeal = default;
        if (ReferenceEquals(_arming, arming))
            TossActivation();
    }

    private void TossActivation()
    {
        ArmingRun? arming = _arming;
        _arming = null;
        if (arming is not null)
        {
            if (arming.QueuedFinalSeal.Status
                is not SimIdleSetPositionCommitStatus.None)
            {
                _physics.SetPosition.RetireDormantOwnActivation(
                    arming.QueuedFinalSeal,
                    impactAlreadyDispatched: true);
            }
            _physics.SetPosition.RetireDormantOwnActivationTicket(
                arming.Record,
                arming.Token.Placement);
        }
        if (arming is not null
            && _actors.IsCurrent(arming.Record)
            && ReferenceEquals(
                arming.Record.KineticBody,
                arming.Body))

            _actors.AssignKineticsCorpus(arming.Record, null);
        if (arming is not null
            && ReferenceEquals(_movement.Controller, arming.Driver))

            _movement.Controller = null;
    }

    private bool ArmingStillLatest(ArmingRun arming)
    {
        return arming.Driver.IsCorePossessedDormant
        && arming.Driver.OwnsKineticsCorpus(arming.Body)
        && _actors.SessionLifetimeVersion
            == arming.Token.SessionGenerationAuthority
        && _actors.IsCurrent(arming.Record)
        && arming.Record.Key == arming.Token.Entity
        && !_identity.IsDisposed
        && _identity.ServerGuid == arming.Token.LocalPlayerServerGuid
        && _identity.ServerGuid == arming.Record.ServerGuid
        && _identity.Revision == arming.Token.LocalPlayerIdentityRevision
        && arming.Record.KineticsOwnershipEpoch
            == arming.Token.PhysicsOwnershipEpoch
        && arming.Record.ObjectTimerEpoch
            == arming.Token.ObjectClockEpoch
        && _movement.CanSealCorePossessedDriver(
            arming.Token.ControllerOwnershipEpoch,
            arming.Driver)
        && ReferenceEquals(arming.Record.KineticBody, arming.Body)
        && arming.Record.PhysicsHost is null
        && arming.Record.PeerMotion is null
        && arming.Record.Projectile is null
        && !arming.Record.KineticsCorpusAcquisitionInHeadway
        && !arming.Record.DistantLocomotionMappingInHeadway
        && !arming.Record.MissileMappingInHeadway
        && !arming.Record.RequiresDistantStanceCore
        && !arming.Record.EraseApprovedForTeardown
        && _physics.SetPosition.IsDormantOwnActivationTenancyLatest(
            arming.Record,
            arming.Body,
            arming.Token.Placement,
            arming.StanceDirective);
    }

    private bool SealedSuffixHolds(
        ArmingRun arming,
        in SimIdleSetPositionCommitStub receipt)
    {
        return arming.Token.ObjectClockEpoch != ulong.MaxValue
            && _actors.SessionLifetimeVersion
                == arming.Token.SessionGenerationAuthority
            && _actors.IsCurrent(arming.Record)
            && arming.Record.Key == arming.Token.Entity
            && !_identity.IsDisposed
            && _identity.ServerGuid == arming.Token.LocalPlayerServerGuid
            && _identity.ServerGuid == arming.Record.ServerGuid
            && _identity.Revision
                == arming.Token.LocalPlayerIdentityRevision
            && arming.Record.KineticsOwnershipEpoch
                == arming.Token.PhysicsOwnershipEpoch
            && arming.Record.ObjectTimerEpoch
                == arming.Token.ObjectClockEpoch + 1UL
            && _movement.DriverOwnershipEpoch
                == arming.Token.ControllerOwnershipEpoch
            && ReferenceEquals(_movement.Controller, arming.Driver)
            && arming.Driver.IsRuntimePublished
            && arming.Driver.OwnsKineticsCorpus(arming.Body)
            && ReferenceEquals(
                arming.Record.KineticBody,
                arming.Body)
            && ReferenceEquals(
                arming.Record.PhysicsHost,
                arming.PhysicsHost)
            && arming.Record.PeerMotion is null
            && arming.Record.Projectile is null
            && !arming.Record.EraseApprovedForTeardown
            && _physics.SetPosition.IsDormantOwnActivationSealLatest(
                arming.Record,
                arming.Body,
                receipt);
    }
}

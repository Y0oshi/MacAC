using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Kinetics;

internal sealed partial class MissileDriver
{
    internal bool TryAttach(
        OnlineActorRecord capture,
        RigSpec rig,
        double latestMoment,
        int onlineMiddleX = 0,
        int onlineMiddleY = 0)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(rig);

        if (!_onlineActors.TryFetchRecord(capture.ServerOid, out var onlineCapture)
            || !ReferenceEquals(onlineCapture, capture)
            || (capture.FinalKineticsPhase & KineticStateFlags.Missile) == 0)
            return false;

        if (capture.ProjectileRuntime is SimMissile kept)
        {
            kept.Body.State = capture.FinalKineticsPhase;
            return true;
        }

        if (capture.WorldEntity is not { } actor
            || capture.Snapshot.Physics is not { } kinetics
            || capture.Snapshot.Position is not { } wireLocus)
            return false;

        float scaling = kinetics.Scale ?? capture.Snapshot.ObjScale ?? 1f;
        if (!TryFetchImpactOrb(rig, scaling, out MissileContactSphere orb))
        {
            DiagnosticSink?.Invoke(
                $"Missile 0x{capture.ServerOid:X8} Setup 0x{capture.Snapshot.SetupTableId ?? 0u:X8} " +
                $"does not have the supported retail one-sphere collision shape.");
            return false;
        }

        if (!double.IsFinite(latestMoment))
        {
            DiagnosticSink?.Invoke(
                $"Missile 0x{capture.ServerOid:X8} has an invalid physics clock.");
            return false;
        }
        _previousFinitePlayMoment = latestMoment;

        KineticBody corpus;
        if (capture.KineticBody is { } sharedCorpus)
        {
            corpus = sharedCorpus;
            uint latestChamberIdent = corpus.CellPosition.ObjCellId;
            Vector3 latestChamberOwn = corpus.CellPosition.Frame.Origin;
            if (!IsFinite(corpus.Position)
                || !IsFinite(corpus.Velocity)
                || !IsFinite(corpus.Omega)
                || !PoseValidation.IsValid(
                    latestChamberIdent,
                    latestChamberOwn,
                    corpus.Orientation))
            {
                DiagnosticSink?.Invoke(
                    $"Missile 0x{capture.ServerOid:X8} has an invalid current shared physics frame or vector.");
                return false;
            }
            corpus.State = capture.FinalKineticsPhase;
            corpus.Friction = StandardizeFriction(
                kinetics.Friction ?? capture.Snapshot.Friction);
            corpus.Elasticity = StandardizeElasticity(
                kinetics.Elasticity ?? capture.Snapshot.Elasticity ?? 0.05f);

            corpus.SnapToChamber(
                latestChamberIdent,
                corpus.Position,
                latestChamberOwn);
            corpus.PreviousRefreshMoment = latestMoment;
        }
        else
        {
            Vector3 vel = kinetics.Velocity ?? Vector3.Zero;
            Vector3 omega = kinetics.AngularVelocity ?? Vector3.Zero;
            if (!IsFinite(actor.Position)
                || !PoseValidation.IsValid(
                    wireLocus.LandblockId,
                    new Vector3(
                        wireLocus.PositionX,
                        wireLocus.PositionY,
                        wireLocus.PositionZ),
                    actor.Rotation)
                || !IsFinite(vel)
                || !IsFinite(omega))
            {
                DiagnosticSink?.Invoke(
                    $"Missile 0x{capture.ServerOid:X8} has an invalid initial physics frame or vector.");
                return false;
            }
            corpus = _onlineActors.GetOrCreatePhysicsBody(
                capture.ServerOid,
                _ =>
                {
                    KineticBody built = new KineticBody
                    {
                        Friction = StandardizeFriction(
                            kinetics.Friction ?? capture.Snapshot.Friction),
                        Elasticity = StandardizeElasticity(
                            kinetics.Elasticity ?? capture.Snapshot.Elasticity ?? 0.05f),
                        Orientation = actor.Rotation,
                        PreviousRefreshMoment = latestMoment,
                    };
                    built.SnapToChamber(
                        wireLocus.LandblockId,
                        actor.Position,
                        new Vector3(
                            wireLocus.PositionX,
                            wireLocus.PositionY,
                            wireLocus.PositionZ));
                    return built;
                });
        }

        uint canonChamberIdent = corpus.CellPosition.ObjCellId;
        actor.SetPosition(corpus.Position);
        actor.Rotation = corpus.Orientation;
        actor.ParentCellId = canonChamberIdent;
        bool alreadyProjectedInCanonChamber = capture.IsSpatiallyProjected
            && capture.WholeChamberIdent == canonChamberIdent;
        if ((!alreadyProjectedInCanonChamber
                && !_onlineActors.RebucketLiveEntity(
                    capture.ServerOid,
                    canonChamberIdent))
            || !_onlineActors.TryFetchRecord(capture.ServerOid, out var latestCapture)
            || !ReferenceEquals(latestCapture, capture)
            || !ReferenceEquals(latestCapture.WorldEntity, actor)
            || !ReferenceEquals(latestCapture.KineticBody, corpus)
            || (latestCapture.FinalKineticsPhase & KineticStateFlags.Missile) == 0)

            return false;

        if (latestCapture.ProjectileRuntime is SimMissile concurrentCore)
            return ReferenceEquals(concurrentCore.Body, corpus);

        canonChamberIdent = corpus.CellPosition.ObjCellId;

        SimMissile core = (SimMissile)
            _onlineActors.AttachMissileCore(
                capture.ServerOid,
                corpus,
                orb,
                () => _onlineActors.IsLatestCapture(capture)
                    && ReferenceEquals(capture.WorldEntity, actor)
                    && ReferenceEquals(capture.KineticBody, corpus));

        if (HasShownChamber(capture) && !IsConcealed(capture))
        {
            ProxyPositionSynchronizer.Sync(
                _shades,
                actor.Id,
                corpus.Position,
                corpus.Orientation,
                canonChamberIdent,
                onlineMiddleX,
                onlineMiddleY);
        }
        else if (HasShownChamber(capture))
        {
            corpus.InWorld = true;
            corpus.PreviousRefreshMoment = latestMoment;
            _shades.Suspend(actor.Id);
        }
        else
        {
            SuspendBeyondRealm(core, actor.Id);
        }
        _rootPoses?.RenewTrunk(actor);
        return true;
    }

    internal bool TryFetchCorpus(uint srvOid, out KineticBody corpus)
    {
        if (TryFetchLatest(srvOid, out _, out SimMissile core))
        {
            corpus = core.Body;
            return true;
        }
        corpus = null!;
        return false;
    }

    internal bool TryCommenceQuantum(
        OnlineActorRecord capture,
        float quantum,
        out QuantumHop hop)
    {
        ArgumentNullException.ThrowIfNull(capture);
        hop = default;
        if (!TryFetchLatest(
                capture.ServerOid,
                out OnlineActorRecord latest,
                out SimMissile core)
            || !ReferenceEquals(capture, latest)
            || latest.WorldEntity is not { } actor
            || IsConcealed(latest)
            || !HasShownChamber(latest))
            return false;

        bool ExternalHolderValid() =>
            _onlineActors.IsLatestCapture(latest)
            && ReferenceEquals(latest.WorldEntity, actor)
            && ReferenceEquals(latest.ProjectileRuntime, core);
        if (!_runtimeUpdater.TryCommence(
                latest.Canonical,
                quantum,
                latest.ObjectTimerEpoch,
                ExternalHolderValid,
                out SimMissileKineticsCommit coreSeal))

            return false;

        hop = new QuantumHop(
            latest,
            actor,
            coreSeal);
        return true;
    }

    private bool TryFetchLatest(
        uint srvOid,
        out OnlineActorRecord capture,
        out SimMissile core)
    {
        if (_onlineActors.TryFetchRecord(srvOid, out capture!)
            && capture.ProjectileRuntime is SimMissile located)
        {
            core = located;
            return true;
        }

        capture = null!;
        core = null!;
        return false;
    }

    private static bool TryFetchImpactOrb(
        RigSpec rig,
        float scaling,
        out MissileContactSphere orb)
    {
        if (rig.Orbs.Count is 1)
        {
            Orb src = rig.Orbs[0];
            orb = new MissileContactSphere(src.Center, src.Radius, scaling);
            if (orb.IsValid)
                return true;
        }

        orb = default;
        return false;
    }
}

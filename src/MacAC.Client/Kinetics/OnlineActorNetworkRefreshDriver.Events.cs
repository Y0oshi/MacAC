using MacAC.Dat;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Kinetics;

internal sealed partial class OnlineActorNetworkRefreshDriver
{

    public void OnLocomotion(MacAC.Wire.RealmSession.MoverMotionUpdate refresh)
    {
        bool retainCargo = refresh.Guid != _avatarSrvOid || !refresh.IsAutonomous;
        if (!_arbiterLatch.TryAdmitLocomotion(
                refresh,
                retainCargo,
                out AcceptedMotionWirePulse approved,
                out bool stampApproved))
        {
            if (!stampApproved
                && (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1"
                || Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1")
                )
            {
                Console.WriteLine(
                    $"[UM_STALE] guid={refresh.Guid:X8} inst={refresh.InstanceSequence} "
                    + $"mov={refresh.MovementSequence} sc={refresh.ServerControlSequence} dropped");
            }
            return;
        }

        var approvedLocomotionCapture = approved.Record;
        ulong approvedTravelArbiterVer =
            approved.MovementAuthorityVersion;
        ulong approvedTravelVelArbiterVer =
            approved.VelocityAuthorityVersion;

        if (!_onlineActors.TryFetchRealmActor(refresh.Guid, out var actor)) return;
        if (!_animatedEntities.TryGetValue(actor.Id, out var ledger))
        {
            DispatchRemoteInboundMotion(
                refresh,
                actor,
                ledger: null,
                approvedLocomotionCapture,
                approvedTravelArbiterVer,
                approvedTravelVelArbiterVer);
            return;
        }
        if (_datFiles is null) return;

        ushort stance = refresh.MotionState.Stance;
        ushort? directive = refresh.MotionState.ForwardCommand;

        if (System.Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1"
            && refresh.Guid != _avatarSrvOid)
        {
            string cmdTextRaw = directive.HasValue ? $"0x{directive.Value:X4}" : "null";
            string flankText = refresh.MotionState.SideStepCommand is { } s ? $"0x{s:X4}" : "null";
            string pivotText = refresh.MotionState.TurnCommand is { } t ? $"0x{t:X4}" : "null";
            string fwdSpdText = refresh.MotionState.ForwardSpeed is { } fs ? $"{fs:F2}" : "null";
            uint seqMot = ledger.Sequencer?.CurrentMotion ?? 0;
            System.Console.WriteLine(
                $"[UM_RAW] guid={refresh.Guid:X8} stance=0x{stance:X4} fwd={cmdTextRaw} fwdSpd={fwdSpdText} "
                + $"side={flankText} turn={pivotText} mt=0x{refresh.MotionState.MovementType:X2} "
                + $"isMoveTo={refresh.MotionState.IsSrvControlledRelocateTo} "
                + $"seq.CurrentMotion=0x{seqMot:X8}");
        }

        if (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1"
            && refresh.Guid != _avatarSrvOid)
        {
            string cmdText = directive.HasValue ? $"0x{directive.Value:X4}" : "null";
            float spd = refresh.MotionState.ForwardSpeed
                ?? ((refresh.MotionState.MoveToSpeed ?? 0f)
                    * (refresh.MotionState.MoveToRunRate ?? 0f));
            uint seqStyling = ledger.Sequencer?.LatestStyling ?? 0;
            uint seqLocomotion = ledger.Sequencer?.CurrentMotion ?? 0;
            Console.WriteLine(
                $"UM guid=0x{refresh.Guid:X8} mt=0x{refresh.MotionState.MovementType:X2} stance=0x{stance:X4} cmd={cmdText} spd={spd:F2} " +
                $"| seq now style=0x{seqStyling:X8} motion=0x{seqLocomotion:X8}");
        }

        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeBuildingEnabled
            && IsDoorLabel(_objects.Get(refresh.Guid)?.Name))
        {
            Console.WriteLine(System.FormattableString.Invariant(
                $"[door-cycle] guid=0x{refresh.Guid:X8} stance=0x{stance:X4} cmd=0x{(directive ?? 0u):X4}"));
        }

        if (ledger.Sequencer is not null)
        {
            uint wholeStyling = stance is not 0
                ? (0x80000000u | (uint)stance)
                : ledger.Sequencer.LatestStyling;

            float paceMod = refresh.MotionState.ForwardSpeed ?? 1f;
            uint wholeLocomotion;
            if (!directive.HasValue || directive.Value is 0)
            {
                wholeLocomotion = 0x41000003u;
            }
            else
            {
                uint settled = MacAC.Mechanics.Kinetics.MotionCommandLookup
                    .ReconstructWholeCommand(directive.Value);
                wholeLocomotion = settled is not 0
                    ? settled
                    : (ledger.Sequencer.CurrentMotion & 0xFF000000u) | (uint)directive.Value;
                if (wholeLocomotion == (uint)directive.Value)
                    wholeLocomotion = 0x40000000u | (uint)directive.Value;
            }

            if (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1"
                && refresh.Guid != _avatarSrvOid)
                Console.WriteLine(
                    $"UM    ↳ SetCycle(style=0x{wholeStyling:X8}, motion=0x{wholeLocomotion:X8}, speed={paceMod:F2})");

            // No-op if same; the sequencer's fast path guards against that.
            uint precedingLocomotion = ledger.Sequencer.CurrentMotion;

            if (refresh.Guid == _avatarSrvOid)
            {
                if (_playerController is not null)
                {
                    _playerController.AssignPreviousRelocateWasAutonomous(refresh.IsAutonomous);
                    bool IsLatestOwnLocomotion() =>
                        _onlineActors.IsLatestTravelArbiter(
                            approvedLocomotionCapture,
                            approvedTravelArbiterVer)
                        && _onlineActors.IsLatestVelArbiter(
                            approvedLocomotionCapture,
                            approvedTravelVelArbiterVer)
                        && ReferenceEquals(
                            approvedLocomotionCapture.WorldEntity,
                            actor);
                    if (!IsLatestOwnLocomotion())
                        return;

                    var ownRelay =
                        _distantIncomingLocomotion.Apply(
                            refresh,
                            _playerController.Movement,
                            _playerController.Locomotion.DefaultSink,
                            _avatarHub,
                            _playerController.CellId,
                            ledger.Sequencer.CurrentMotion & 0xFF000000u,
                            IsLatestOwnLocomotion);
                    if (ownRelay.Superseded
                        || !IsLatestOwnLocomotion())

                        return;
                    if (ownRelay.RoutedMoveTo)

                        return;
                    if (!ownRelay.AppliedInterpretedState)
                        return;
                    wholeLocomotion = ownRelay.CurrentForwardCommand;
                }
            }
            else
            {
                var relay =
                    DispatchRemoteInboundMotion(
                        refresh,
                        actor,
                        ledger,
                        approvedLocomotionCapture,
                        approvedTravelArbiterVer,
                        approvedTravelVelArbiterVer);
                if (relay.Superseded
                    || relay.RoutedMoveTo
                    || !relay.AppliedInterpretedState)
                    return;
                wholeLocomotion = relay.CurrentForwardCommand;
            }

            _fightingMarkDriver?.OnLocomotionImposed(
                refresh.Guid, ledger.Sequencer.CurrentMotion);
            if (!_onlineActors.IsLatestTravelArbiter(
                    approvedLocomotionCapture,
                    approvedTravelArbiterVer)
                || !_onlineActors.IsLatestVelArbiter(
                    approvedLocomotionCapture,
                    approvedTravelVelArbiterVer))

                return;

            uint newLo = wholeLocomotion & 0xFFu;
            bool enteringLocomotion = newLo is 0x05 or 0x06
                                   or 0x07
                                   or 0x0F or 0x10;
            uint formerLo = precedingLocomotion & 0xFFu;
            bool wasLocomotion = formerLo is 0x05 or 0x06
                              or 0x07
                              or 0x0F or 0x10;
            if (enteringLocomotion && !wasLocomotion && refresh.Guid != _avatarSrvOid)
            {
                // Reset both stop signals so stop-detection starts a fresh
                // window from this transition. Without this, the entity
                // starts its run animation and is instantly interrupted.
                DateTime refreshedMoment = System.DateTime.UtcNow;
                if (approvedLocomotionCapture.ProjTag is { } locomotionTag
                    && _distantTravelObservations.TryFetchVal(
                        locomotionTag,
                        out var earlier))
                {
                    _distantTravelObservations[locomotionTag] =
                        (earlier.Pos, refreshedMoment);
                }
                if (_onlineActors.TryFetchDistantLocomotionCore(
                        refresh.Guid,
                        out ISimPeerMotion? distantCore)
                    && distantCore is PeerMotion motion)
                    motion.PreviousSrvSpotMoment = (refreshedMoment - System.DateTime.UnixEpoch).TotalSeconds;
            }

            return;
        }

        var newCycle = MacAC.Mechanics.Geometry.GaitResolver.FetchIdleCycle(
            ledger.Setup, _datFiles, _animFetcher!,
            locomotionChartIdentOverride: null,
            stanceOverride: stance,
            directiveOverride: directive);
        bool newCycleIsGood = newCycle is not null
            && newCycle.Framerate != 0f
            && newCycle.HighFrame >= newCycle.LowFrame
            && newCycle.Animation.Frames.Count >= 1;
        if (!newCycleIsGood) return;

        ledger.Animation = newCycle!.Animation;
        ledger.LoCycle = Math.Max(0, newCycle.LowFrame);
        ledger.HighFrame = Math.Min(newCycle.HighFrame, newCycle.Animation.Frames.Count - 1);
        ledger.Framerate = newCycle.Framerate;
        ledger.CurrCycle = ledger.LoCycle;
    }

    public void OnVector(MacAC.Wire.Messages.VelocityUpdate.Parsed refresh)
    {
        bool cargoIsValid = _missileDriver?.CanAdmitVectorCargo(
                refresh.Guid,
                refresh.Velocity,
                refresh.Omega) != false;
        if (!_arbiterLatch.TryAdmitVector(
                refresh,
                cargoIsValid,
                out AcceptedVectorWirePulse approved))

            return;
        var approvedVectorCapture = approved.Record;
        ulong approvedVectorArbiterVer =
            approved.VectorAuthorityVersion;
        ulong approvedVectorVelArbiterVer =
            approved.VelocityAuthorityVersion;

        OnlineActorVectorRouter.Course(
            () => _missileDriver?.ImposeAuthoritativeVector(
                    approvedVectorCapture,
                    approvedVectorArbiterVer,
                    approvedVectorVelArbiterVer,
                    refresh.Velocity,
                    refresh.Omega,
                    _physicsScriptGameTime) == true,
            () =>
            {
                if (refresh.Guid == _avatarSrvOid
                    || approvedVectorCapture.RemoteMotionRuntime is not null
                    || approvedVectorCapture.KineticBody is not { } canonCorpus)

                    return false;
                _onlineActors.TryCommitAuthoritativeVector(
                    approvedVectorCapture,
                    canonCorpus,
                    refresh.Velocity,
                    refresh.Omega,
                    _physicsScriptGameTime);
                return true;
            },
            () => ImposePlainVector(
                refresh,
                approvedVectorCapture,
                approvedVectorArbiterVer,
                approvedVectorVelArbiterVer));
    }

    public void OnState(MacAC.Wire.Messages.GroupPhase.Parsed decoded)
    {
        if (!_arbiterLatch.TryAdmitPhase(
                decoded,
                out AcceptedStateWirePulse approved))
            return;
        var capture = approved.Record;
        ulong approvedPhaseArbiterVer = approved.StateAuthorityVersion;

        _onlineActorLamps?.OnPhaseAltered(decoded.Guid);
        _onlineActorExhibit?.OnPhaseApproved(decoded.Guid);

        if (!_onlineActors.IsLatestPhaseArbiter(
                capture,
                approvedPhaseArbiterVer))

            return;

        _missileDriver?.ImposeAuthoritativePhase(
            capture,
            approvedPhaseArbiterVer,
            capture.FinalKineticsPhase,
            _physicsScriptGameTime,
            _origin.CenterX,
            _origin.CenterY);
        if (!_onlineActors.IsLatestPhaseArbiter(
                capture,
                approvedPhaseArbiterVer))

            return;
        if (decoded.Guid == _avatarSrvOid)
        {
            _ = _playerController?.ApplyServerPhysicsState(
                capture.FinalKineticsPhase);
        }

        if (!_onlineActors.TryFetchRealmActor(decoded.Guid, out var actor)) return;

        uint registryTag = actor.Id;

        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeBuildingEnabled)
            Console.WriteLine(System.FormattableString.Invariant(
                $"[setstate] guid=0x{decoded.Guid:X8} entityId=0x{registryTag:X8} raw=0x{decoded.PhysicsState:X8} final=0x{(uint)capture.FinalKineticsPhase:X8} instSeq={decoded.InstanceSequence} stateSeq={decoded.StateSequence}"));
    }

    /// <summary>
    /// Applies a projectile's position. A projectile's server position is authoritative, so there
    /// is no render pose to blend towards and no landblock bucket to move it between: the update
    /// is spent here whichever way it goes.
    /// </summary>
    private void ApplyMissilePosition(
        SimSovereignPositionRoute? distantCourse,
        SimActorRecord approvedLocusCanon,
        OnlineActorRecord approvedLocusCapture,
        ulong approvedLocusArbiterVer)
    {
        if (distantCourse is not { } course)
            return;

        // A teleport has to be announced before the placement runs, and the arming hook needs to
        // know which capture it is speaking for.
        if (course.Disposition is SimSovereignPositionVerdict.SetPosition
            && approvedLocusCanon.PeerMotion is PeerMotion adoptedDistant)
        {
            _missileArmLocusCapture = approvedLocusCapture;
            _missileArmLocusArbiterVer = approvedLocusArbiterVer;
            RunRemoteTeleportHook(
                approvedLocusCanon,
                adoptedDistant,
                _distantArmHooks.IsLatestMissileLocusHolder);
        }

        var stanceCondition =
            _distantStanceSteer.ImposeApprovedMissileLocus(approvedLocusCanon, course);

        // Deferred and placement-rejected both mean the body has not moved yet, so there is
        // nothing for the visible projectile to be synchronised against.
        if (stanceCondition is not null
            and not SimPeerPlacementExecutionStatus.Deferred
            and not SimPeerPlacementExecutionStatus.RejectedByPlacement)
        {
            _missileDriver?.SynchronizeExhibitFromSettledCorpus(
                approvedLocusCapture,
                _physicsScriptGameTime);
        }
    }

    /// <summary>
    /// Remembers where a remote actor was last seen actually moving. A position that has barely
    /// changed leaves the stored time alone, so "when did this stop moving" stays answerable
    /// rather than being reset by every idle echo the server sends.
    /// </summary>
    private void NoteDistantTravel(
        SimActorKey locusTag,
        System.Numerics.Vector3 realmSpot,
        DateTime instant)
    {
        const float StillnessMetres = 0.05f;
        if (_distantTravelObservations.TryFetchVal(locusTag, out var earlier)
            && System.Numerics.Vector3.Distance(earlier.Pos, realmSpot) <= StillnessMetres)
        {
            return;
        }

        _distantTravelObservations[locusTag] = (realmSpot, instant);
    }

    public void OnPosition(MacAC.Wire.RealmSession.MoverPositionUpdate refresh)
    {
        if (_realmDiscardProj?.TryRecoverUnknownLocus(refresh) == true)

            return;

        bool cargoIsValid = _missileDriver?.CanAdmitLocusCargo(
                refresh.Guid,
                refresh.Position,
                refresh.Velocity) != false;
        if (!_arbiterLatch.TryAdmitLocus(
            refresh,
            _avatarSrvOid,
            refresh.Guid == _avatarSrvOid && _playerController is not null
                ? _playerController.CorpusFacing
                : null,
            refresh.Guid == _avatarSrvOid && _playerController is not null
                ? _playerController.CorpusVel
                : null,
            cargoIsValid,
            out AcceptedPositionWirePulse approved))

            return;
        var stampDisposition = approved.TimestampDisposition;
        RealmSession.MoverSpawn approvedSummon = approved.Spawn;
        var timestamps = approved.Timestamps;
        var approvedLocusCanon = approved.Canonical;
        ulong approvedLocusArbiterVer =
            approved.PositionAuthorityVersion;
        if (!_onlineActors.TryFetchProj(
                approvedLocusCanon,
                out OnlineActorRecord approvedLocusCapture)
            && !_onlineActorHydration.ReclaimCanonProj(
                approvedLocusCanon,
                approvedLocusArbiterVer,
                out approvedLocusCapture))

            return;

        bool IsLatestLocusHolder(
            MacAC.Mechanics.Realm.RealmActor? anticipatedActor = null) =>
            _onlineActors.IsLatestLocusArbiter(
                approvedLocusCapture,
                approvedLocusArbiterVer)
            && (anticipatedActor is null
                || ReferenceEquals(
                    approvedLocusCapture.WorldEntity,
                    anticipatedActor));
        if (!IsLatestLocusHolder())
            return;

        if (_onlineActorHydration?.SecureRealmOrigin(
                approvedLocusCapture,
                approvedLocusArbiterVer,
                approvedSummon) != true
            || !IsLatestLocusHolder())
            return;

        var position = refresh.Position;
        int lbX = (int)((position.LandblockId >> 24) & 0xFFu);
        int lbY = (int)((position.LandblockId >> 16) & 0xFFu);
        System.Numerics.Vector3 origin = new System.Numerics.Vector3(
            (lbX - _origin.CenterX) * 192f,
            (lbY - _origin.CenterY) * 192f,
            0f);
        System.Numerics.Vector3 realmSpot = new System.Numerics.Vector3(position.PositionX, position.PositionY, position.PositionZ) + origin;

        bool forceOwn = stampDisposition is MacAC.Mechanics.Kinetics.PoseStampVerdict.ForcePosition
            && refresh.Guid == _avatarSrvOid
            && _playerController is not null;
        if (forceOwn)
        {
            if (!IsLatestLocusHolder())
                return;

            var forceCondition =
                _approvedLocusSteer.TryExecuteAcceptedLocalPosition(
                    approvedLocusCanon,
                    refresh,
                    stampDisposition,
                    timestamps,
                    timestamps.PreviousTeleport);
            if (forceCondition is SimGrantedPositionExecutionStatus.Committed
                or SimGrantedPositionExecutionStatus.DeferredCell)
            {
                _actorFxList?.StampOnlineHolderPostureStale(refresh.Guid);
                _arbiterLatch.ObserveAcceptedLocalPosition(
                    refresh.Position.LandblockId);
                return;
            }
            if (forceCondition is not SimGrantedPositionExecutionStatus.NotApplicable)

                return;
        }

        if (RequiresSpatialProjRecovery(approvedLocusCapture))
        {
            if (!IsLatestLocusHolder())
                return;
            MacAC.Client.Graphics.DescendantUnparentDisposition unparented =
                _equippedDescendantPainter?.OnDescendantBecameUnparented(
                    refresh.Guid,
                    () =>
                    {
                        if (!IsLatestLocusHolder())
                            return;
                        _onlineActorHydration!.ReclaimProj(
                            approvedLocusCapture,
                            approvedLocusArbiterVer,
                            approvedSummon);
                    })
                ?? MacAC.Client.Graphics.DescendantUnparentDisposition.NotAttached;
            if (unparented is MacAC.Client.Graphics.DescendantUnparentDisposition.Superseded
                or MacAC.Client.Graphics.DescendantUnparentDisposition.Pending)
                return;
            if (!IsLatestLocusHolder())
                return;
            if (unparented is MacAC.Client.Graphics.DescendantUnparentDisposition.NotAttached)
            {
                _onlineActorHydration!.ReclaimProj(
                    approvedLocusCapture,
                    approvedLocusArbiterVer,
                    approvedSummon);
                if (!IsLatestLocusHolder())
                    return;
            }
        }

        if (!_onlineActors.TryFetchRealmActor(refresh.Guid, out var actor)) return;
        if (!IsLatestLocusHolder(actor))
            return;
        _actorFxList?.StampOnlineHolderPostureStale(refresh.Guid);
        if (!IsLatestLocusHolder(actor))
            return;

        if (refresh.Guid == _avatarSrvOid)
            _arbiterLatch.ObserveAcceptedLocalPosition(refresh.Position.LandblockId);

        var rot = stampDisposition is MacAC.Mechanics.Kinetics.PoseStampVerdict.ForcePosition
            ? actor.Rotation
            : new System.Numerics.Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW);
        _travelTruthTelemetry.OnSrvEcho(refresh, realmSpot);

        SimSovereignPositionRoute? earlyDistantCourse =
            refresh.Guid != _avatarSrvOid
                ? ClassifyDistantApprovedLocus(
                    refresh,
                    approvedLocusCanon,
                    stampDisposition,
                    timestamps,
                    realmSpot)
                : null;
        bool isMissilePacket = earlyDistantCourse is { } classifiedCourse
            ? classifiedCourse.OperationKind
                is SimSetPositionOperationKind.ProjectileAuthoritative
            : refresh.Guid != _avatarSrvOid
                && (approvedLocusCanon.FinalKineticsCondition
                    & MacAC.Mechanics.Kinetics.KineticStateFlags.Missile) != 0
                && approvedLocusCanon.Projectile is { } tiedMissile
                && ReferenceEquals(
                    approvedLocusCanon.KineticBody,
                    tiedMissile.Body);
        if (isMissilePacket)
        {
            ApplyMissilePosition(
                earlyDistantCourse,
                approvedLocusCanon,
                approvedLocusCapture,
                approvedLocusArbiterVer);
            return;
        }

        if (!_onlineActors.TryFetchRecord(
                refresh.Guid,
                out OnlineActorRecord locusCapture)
            || !ReferenceEquals(locusCapture, approvedLocusCapture)
            || !ReferenceEquals(locusCapture.WorldEntity, actor)
            || !_onlineActors.IsLatestLocusArbiter(
                locusCapture,
                approvedLocusArbiterVer))

            return;

        TryApplyGenericRemoteRenderPose(
            actor,
            earlyDistantCourse,
            realmSpot,
            position.LandblockId,
            rot);
        bool queuedInterpolationSole = WillSoleFifoInterpolation(
            refresh.Guid, earlyDistantCourse, realmSpot);
        if ((!queuedInterpolationSole
                && !_onlineActors!.RebucketLiveEntity(refresh.Guid, position.LandblockId))
            || !_onlineActors!.TryFetchRecord(
                refresh.Guid,
                out OnlineActorRecord followingRebucket)
            || !ReferenceEquals(followingRebucket, locusCapture)
            || !ReferenceEquals(followingRebucket.WorldEntity, actor)
            || !_onlineActors.IsLatestLocusArbiter(
                followingRebucket,
                approvedLocusArbiterVer))

            return;

        if (refresh.Guid != _avatarSrvOid)
        {
            DateTime instant = System.DateTime.UtcNow;
            SimActorKey locusTag = locusCapture.ProjTag
                ?? throw new InvalidOperationException(
                    $"Position owner 0x{refresh.Guid:X8}/" +
                    $"{locusCapture.Generation} has no exact projection key.");
            NoteDistantTravel(locusTag, realmSpot, instant);

            if (!_onlineActors.TryFetchDistantLocomotionCore(
                    refresh.Guid,
                    out ISimPeerMotion? distantCore)
                || distantCore is not PeerMotion rmPhase)
            {
                rmPhase =
                    _onlineActors.FetchOrBuildDistantLocomotionCore(
                        refresh.Guid);
                // Hard-snap orientation on first spawn so the per-tick
                // slerp doesn't visibly rotate from Identity to truth.
                rmPhase.Body.Orientation = rot;
                rmPhase.Body.Position = realmSpot;
                SeedRemoteSpawnPlacement(
                    rmPhase,
                    refresh.Guid,
                    actor,
                    realmSpot,
                    refresh.Position.LandblockId);
            }

            if (!_onlineActors.IsLatestLocusArbiter(
                    locusCapture,
                    approvedLocusArbiterVer))

                return;

            if (MacAC.Mechanics.Kinetics.KineticTelemetry.ShouldTraceDistantSlide(
                    refresh.Guid))
            {
                (int slideFifoZDepth, int slideFailTally) =
                    rmPhase.Lerp.ProbeInterpolationPhase;
                MacAC.Mechanics.Kinetics.KineticTelemetry.TraceDistantSlideUp(
                    oid: refresh.Guid,
                    wireGrounded: refresh.IsGrounded,
                    wireVelocity: refresh.Velocity,
                    disposition: earlyDistantCourse is { } slideCourse
                        ? slideCourse.Disposition.ToString()
                        : "unclassified",
                    avatarGap: _playerController is { } slideDriver
                        ? System.Numerics.Vector3.Distance(
                            realmSpot,
                            slideDriver.Position)
                        : null,
                    corpusToMark: System.Numerics.Vector3.Distance(
                        rmPhase.Body.Position,
                        realmSpot),
                    corpusSnapThreshold:
                        SimPeerSettledStatePosition.ProbeCorpusSnapThreshold,
                    willBeDrTicked: WillAdvanceRemoteMotion(refresh.Guid, rmPhase),
                    leadUp: rmPhase.PreviousSrvSpotMoment <= 0.0,
                    airborne: rmPhase.Airborne,
                    link: rmPhase.Body.InContact,
                    onPassable: rmPhase.Body.OnWalkable,
                    gravity: rmPhase.Body.HasGravity,
                    corpusVel: rmPhase.Body.Velocity,
                    linkPlaneValid: rmPhase.Body.ContactPlaneValid,
                    linkPlaneNormZ: rmPhase.Body.ContactPlane.Normal.Z,
                    wireLocus: realmSpot,
                    corpusLocus: rmPhase.Body.Position,
                    lerpFifoZDepth: slideFifoZDepth,
                    lerpFailTally: slideFailTally);
            }

            double instantSec = (instant - System.DateTime.UnixEpoch).TotalSeconds;

            if (IsAvatarOid(refresh.Guid))
            {
                if (System.Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1"
                    && rmPhase.PreviousSrvSpotMoment > 0.0)
                {
                    double dtSrv = instantSec - rmPhase.PreviousSrvSpotMoment;
                    if (dtSrv > 0.001)
                    {
                        System.Numerics.Vector3 srvDiff = realmSpot - rmPhase.PreviousSrvSpot;
                        float srvPace = (float)(srvDiff.Length() / dtSrv);
                        float trunkLocomotionPace = rmPhase.UpperTrunkLocomotionPaceSincePreviousUP;
                        if (srvPace > 0.1f || trunkLocomotionPace > 0.1f)
                        {
                            System.Console.WriteLine(
                                $"[VEL_DIAG] guid={refresh.Guid:X8} maxRootMotionSpeed={trunkLocomotionPace:F3} m/s "
                                + $"serverSpeed={srvPace:F3} m/s dtServer={dtSrv:F3}s "
                                + $"ratio={(srvPace > 1e-3f ? trunkLocomotionPace / srvPace : 0f):F3}");
                        }
                    }
                }
                rmPhase.UpperTrunkLocomotionPaceSincePreviousUP = 0f;
                rmPhase.EarlierSrvSpot = rmPhase.PreviousSrvSpot;
                rmPhase.EarlierSrvSpotMoment = rmPhase.PreviousSrvSpotMoment;
            }

            if (SimPeerSettledStatePosition.IsAirborneNoOp(
                    earlyDistantCourse))
            {
                ImposeWireAirborneLeftoverBookkeeping(
                    rmPhase, position.LandblockId, realmSpot, instantSec);
                return;
            }

            bool isWarpCourse = SimPeerWarpPosition
                .OwnsTeleportPlacement(earlyDistantCourse);

            if (!refresh.IsGrounded && !isWarpCourse)
            {
                ImposeWireAirborneLeftoverBookkeeping(
                    rmPhase, position.LandblockId, realmSpot, instantSec);
                return;
            }

            if (!isWarpCourse)
            {
                var srvVel = refresh.Velocity;
                if (srvVel is null && rmPhase.PreviousSrvSpotMoment > 0.0)
                {
                    double passed = instantSec - rmPhase.PreviousSrvSpotMoment;
                    if (passed > 0.001)
                        srvVel = (realmSpot - rmPhase.PreviousSrvSpot) / (float)passed;
                }
                if (srvVel is { } authoritativeVel)
                {
                    rmPhase.SrvVel = authoritativeVel;
                    rmPhase.HasSrvVel = true;
                }
                else
                {
                    rmPhase.SrvVel = System.Numerics.Vector3.Zero;
                    rmPhase.HasSrvVel = false;
                }
            }

            // A sticky lease (a creature closing on its melee target) does not
            // gate the position arm: the server correction routes like any
            // other, and the per-tick stick adjustment then overwrites the frame.
            var arm = RemoteContactArm.UnroutedCatchUp;
            var routing = ExecuteDistantArmRear(
                approvedLocusCanon,
                locusCapture,
                rmPhase,
                earlyDistantCourse,
                refresh.Guid,
                realmSpot,
                rot,
                approvedLocusArbiterVer,
                actor);
            if (routing is null)
                return;
            arm = routing.Value.Arm;

            if (arm is RemoteContactArm.AirborneSnap)
            {
                if (IsAvatarOid(refresh.Guid))

                    rmPhase.Lerp.Clear();

                if (_animatedEntities.TryGetValue(actor.Id, out var aeForLand)
                    && aeForLand.Sequencer is not null)
                {
                    _locomotionCore.EnsureRemoteMotionBindings(
                        rmPhase, aeForLand, refresh.Guid);
                }
            }

            SimPeerSettledStatePosition.TryArmConstraintFollowingOp(
                ToConstraintArm(arm), rmPhase);

            TryAdoptWireChamberFollowingRouting(rmPhase, routing.Value, position.LandblockId);

            rmPhase.PreviousSrvSpot = realmSpot;
            rmPhase.PreviousSrvSpotMoment = instantSec;

            if (!isWarpCourse
                && rmPhase.HasSrvVel
                && _animatedEntities.TryGetValue(actor.Id, out var aeForVel))
            {
                if (System.Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1")
                {
                    string velSrc = refresh.Velocity is null ? "synth" : "wire";
                    System.Console.WriteLine(
                        $"[UPCYCLE_SRC] guid={refresh.Guid:X8} src={velSrc}");
                }
                DistantSrvControlledVelCycle.Apply(
                    refresh.Guid,
                    aeForVel,
                    rmPhase,
                    rmPhase.SrvVel);
            }

            actor.SetPosition(rmPhase.Body.Position);
            actor.ParentCellId = rmPhase.CellId;
            actor.Rotation = rmPhase.Body.Orientation;
            MacAC.Client.Kinetics.OnlineActorShadeHerald.TryBroadcastDistant(
                _onlineActors,
                locusCapture,
                actor,
                rmPhase,
                approvedLocusArbiterVer,
                () => _distantKineticsUpdater.SyncRemoteShadowToBody(
                    actor.Id,
                    rmPhase,
                    _origin.CenterX,
                    _origin.CenterY));
        }

        if (stampDisposition is MacAC.Mechanics.Kinetics.PoseStampVerdict.Apply
            && refresh.Guid == _avatarSrvOid)
        {
            _ownAvatarWarp.OfferDest(
                EngineWarpDestinationBridge.FromApprovedLocus(
                    refresh),
                timestamps.TeleportAdvanced);
        }
    }
    void IOnlineActorSameEpochPulseSink.OnDescription(
        uint holderOid,
        KineticSpawnData blurb)
    {
        if (_onlineActors.TryFetchFxProfile(
                holderOid,
                out var fxProfile)
            && fxProfile is ActorEffectProfile onlineProfile)
        {
            onlineProfile.ImposeNetworkBlurb(blurb);
            _actorFxList.OnLiveActorDescriptionChanged(holderOid);
        }
    }

    void IOnlineActorSameEpochPulseSink.OnAppearance(
        MacAC.Wire.Messages.ObjDescNotice.Parsed looks) =>
        _onlineActorHydration.OnLooks(looks);

    void IOnlineActorSameEpochPulseSink.OnParent(CreateAnchorUpdate ancestor) =>
        _onlineActorHydration.OnBuildAncestorApproved(ancestor);

    void IOnlineActorSameEpochPulseSink.OnPosition(
        RealmSession.MoverPositionUpdate locus) => OnPosition(locus);

    void IOnlineActorSameEpochPulseSink.OnPickup(
        MacAC.Wire.Messages.PickupNotice.Parsed lift) =>
        _onlineActorHydration.OnLift(lift);

    void IOnlineActorSameEpochPulseSink.OnMovement(
        RealmSession.MoverMotionUpdate travel) => OnLocomotion(travel);

    void IOnlineActorSameEpochPulseSink.OnState(
        MacAC.Wire.Messages.GroupPhase.Parsed phase) => OnState(phase);

    void IOnlineActorSameEpochPulseSink.OnVector(
        MacAC.Wire.Messages.VelocityUpdate.Parsed vector) => OnVector(vector);
}

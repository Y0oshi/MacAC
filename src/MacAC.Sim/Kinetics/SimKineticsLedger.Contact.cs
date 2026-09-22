using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public sealed partial class SimKineticsLedger
{
    private readonly Dictionary<uint, ulong> _linkEpochs = new();

    private readonly Dictionary<uint, SimContactIntake> _linkIntakes = new();

    private readonly Dictionary<uint, StagedLandblockContactEpoch> _linedLinkEpochs = new();

    private readonly Dictionary<uint, SimContactPrefixEdit> _stemEdits = new();

    private int _linkThreadIdent;

    private long _loadingSeries;

    private ulong _linkRealmArbiter = 1UL;

    private readonly List<Action<SimContactEpochCommitted>> _epochSealTaps = new();

    public event Action<SimContactEpochCommitted>? CollisionGenerationCommitted
    {
        add
        {
            if (value is not null)
                _epochSealTaps.Add(value);
        }
        remove
        {
            if (value is not null)
                _epochSealTaps.Remove(value);
        }
    }

    public SimContactIntake BeginCollisionAdmission(
        uint landblockId)
    {
        Live();
        DemandLinkThread();
        if (landblockId is 0u)
            throw new ArgumentOutOfRangeException(nameof(landblockId));
        uint canon = LbOf(landblockId);
        if (_stemEdits.ContainsKey(canon))
        {
            throw new InvalidOperationException(
                $"Collision prefix 0x{canon:X8} is still completing its previous mutation transaction");
        }
        ulong latestGen = _linkEpochs.TryGetValue(
            canon,
            out ulong latest)
                ? latest
                : 0UL;
        ulong earlierGen = latestGen;
        BumpLinkRealmArbiter();
        if (_linkIntakes.Remove(
                canon,
                out SimContactIntake? superseded))
        {
            earlierGen = superseded.EarlierGen;
            SetPosition.AbortImpactGen(
                canon,
                superseded.Generation);
            if (_linedLinkEpochs.Remove(
                    canon,
                    out StagedLandblockContactEpoch? readied))

                readied.Dispose();
        }
        ulong gen = checked(latestGen + 1UL);
        _linkEpochs[canon] = gen;
        SimContactIntake admission = new SimContactIntake(
            this,
            canon,
            gen,
            earlierGen);
        _linkIntakes[canon] = admission;
        SetPosition.CommenceImpactGen(canon, gen);
        return admission;
    }

    public SimContactEditResult DemoteImpactToLand(
        uint lbIdent)
    {
        return PushSunset(
                lbIdent,
                SimContactPrefixEditKind.Demotion);
    }

    public SimContactEditResult WithdrawImpact(
        uint lbIdent)
    {
        return PushSunset(
                lbIdent,
                SimContactPrefixEditKind.Withdrawal);
    }

    internal SimContactPrefixLullTicket CommenceImpactStemStillness(
            uint landblockId,
            ulong impactGen,
            bool includeExteriorChambers)
    {
        Live();
        DemandLinkThread();
        if (landblockId is 0u)
            throw new ArgumentOutOfRangeException(nameof(landblockId));
        uint canon = LbOf(landblockId);
        return SetPosition.CommenceImpactStemStillness(
            canon,
            impactGen,
            includeExteriorChambers);
    }

    internal bool TryObtainImpactStemAlterationPermission(
        in SimContactPrefixLullTicket ticket,
        out SimContactPrefixEditLeave permission)
    {
        Live();
        DemandLinkThread();
        return SetPosition.TryObtainImpactStemAlterationPermission(
            ticket,
            out permission);
    }

    internal bool IsImpactStemAlterationPermissionLatest(
        in SimContactPrefixEditLeave permission)
    {
        Live();
        DemandLinkThread();
        return SetPosition.IsImpactStemAlterationPermissionLatest(
            permission);
    }

    internal bool AbortImpactStemStillness(
        in SimContactPrefixLullTicket ticket,
        ulong successorGen = 0UL,
        bool successorPrimed = false)
    {
        Live();
        DemandLinkThread();
        return SetPosition.AbortImpactStemStillness(
            ticket,
            successorGen,
            successorPrimed);
    }

    internal StagedLandblockContactEpoch ReadyImpactGen(
        SimContactIntake admission)
    {
        DemandIntake(admission);
        DemandLinkThread();
        if (_linedLinkEpochs.Count
                >= StagedLandblockContactEpoch
                    .UpperConcurrentImpactPreparations
            && !_linedLinkEpochs.ContainsKey(
                admission.LandblockId))
        {
            throw new InvalidOperationException(
                "Too many collision generations are being prepared concurrently");
        }
        var loadingBuilder =
            Engine.BuildImpactLoadingBuilder(admission.LandblockId);
        var readied = new StagedLandblockContactEpoch(
            this,
            admission,
            loadingBuilder,
            checked(++_loadingSeries));
        _linedLinkEpochs[admission.LandblockId] = readied;
        return readied;
    }

    internal SimContactStagingStep ProgressImpactGenPrep(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        var replicate = readied.ProgressLoadingReplicate();
        return replicate;
    }

    internal bool AbortImpactGen(
        SimContactIntake admission,
        StagedLandblockContactEpoch? prepared = null)
    {
        Live();
        DemandLinkThread();
        ArgumentNullException.ThrowIfNull(admission);
        if (!ReferenceEquals(admission.Owner, this))
        {
            throw new ArgumentException(
                "Collision admission belongs to another Runtime",
                nameof(admission));
        }
        if (prepared is not null && !prepared.Fits(this, admission))
        {
            throw new ArgumentException(
                "Prepared collision generation belongs to another admission",
                nameof(prepared));
        }

        if (_stemEdits.TryGetValue(
                admission.LandblockId,
                out SimContactPrefixEdit? alteration))
        {
            if (alteration.Kind is not SimContactPrefixEditKind.Activation
                || !ReferenceEquals(alteration.Admission, admission)
                || (prepared is not null
                    && !ReferenceEquals(alteration.Prepared, prepared)))

                return false;
            if (alteration.EngineAlterationSealed)
                return PushActivation(alteration).Committed;

            alteration.AbortAsked = true;
            bool earlierPrimed = alteration.EarlierGen is not 0UL
                && Engine.IsLbLandHoused(
                    alteration.LbIdent);
            bool released = alteration.EarlierGen is 0UL
                ? SetPosition.AbortImpactStemStillnessToUnavailable(
                    alteration.Stillness)
                : AbortImpactStemStillness(
                    alteration.Stillness,
                    alteration.EarlierGen,
                    earlierPrimed);
            if (!released)

                return false;
            _stemEdits.Remove(alteration.LbIdent);
        }

        prepared?.Dispose();
        if (prepared is not null
            && _linedLinkEpochs.TryGetValue(
                admission.LandblockId,
                out StagedLandblockContactEpoch? latestReadied)
            && ReferenceEquals(latestReadied, prepared))

            _linedLinkEpochs.Remove(admission.LandblockId);
        if (_linkIntakes.TryGetValue(
                admission.LandblockId,
                out SimContactIntake? latest)
            && ReferenceEquals(latest, admission))
        {
            _linkIntakes.Remove(admission.LandblockId);
            SetPosition.AbortImpactGen(
                admission.LandblockId,
                admission.Generation);
            _linkEpochs[admission.LandblockId] = checked(
                admission.Generation + 1UL);
            BumpLinkRealmArbiter();
        }
        return true;
    }

    internal void StageCollisionAssets(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied,
        SimLandblockContactAssets assets)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        ArgumentNullException.ThrowIfNull(assets);
        if (LbOf(assets.LandblockId)
            != admission.LandblockId)
        {
            throw new ArgumentException(
                "Collision assets do not match their admission landblock",
                nameof(assets));
        }
        StageCollisionAssetsRest(admission, readied, assets);
    }

    private void StageCollisionAssetsRest(SimContactIntake admission, StagedLandblockContactEpoch readied, SimLandblockContactAssets assets)
    {
        if (admission.Completed)
        {
            throw new InvalidOperationException(
                "A completed collision admission can't publish more assets");
        }
        if (admission.HoldingsReadied)
        {
            throw new InvalidOperationException(
                "Collision assets were by now prepared by this receipt");
        }
        readied.Engine.AddLandblock(
                    admission.LandblockId,
                    assets.Terrain,
                    assets.CellSurfaces,
                    assets.PortalPlanes,
                    assets.WorldOffsetX,
                    assets.WorldOffsetY);
        if ((assets.CurrentCellId & 0xFFFF0000u)
                    == (admission.LandblockId & 0xFFFF0000u))

            readied.Engine.RefreshAvatarCurrChamber(assets.CurrentCellId);
        admission.HoldingsReadied = true;
    }

    internal SimContactHolderCaptureStep ProgressImpactKeptHolderGrab(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        if (!readied.ProgressLoadingReplicate().Completed)
        {
            return new SimContactHolderCaptureStep(
                Completed: false,
                Restarted: false,
                HasOwner: false,
                OwnerId: 0u);
        }
        return readied.ProgressKeptHolderGrab();
    }

    internal void RenewImpactKeptHolder(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied,
        uint holderIdent)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        readied.RenewKeptHolder(holderIdent);
    }

    internal void RestartCollisionRetainedOwnerCapture(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        readied.RestartKeptHolderGrab();
    }

    internal SimContactSealStep ProgressImpactGenSeal(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        DemandIntake(admission);
        DemandLinkThread();
        DemandLined(admission, readied);
        if (!admission.HoldingsReadied)
        {
            throw new InvalidOperationException(
                "Collision generation can't seal prior to its assets are prepared");
        }
        return readied.ProgressSeal();
    }

    internal SimContactEpochCommit CommitCollisionGeneration(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        Live();
        DemandLinkThread();
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(readied);

        if (_stemEdits.TryGetValue(
                admission.LandblockId,
                out SimContactPrefixEdit? queued))
        {
            if (queued.Kind is not SimContactPrefixEditKind.Activation
                || !ReferenceEquals(queued.Admission, admission)
                || !ReferenceEquals(queued.Prepared, readied))
            {
                throw new InvalidOperationException(
                    "A different collision-prefix mutation owns this landblock");
            }
            if (queued.EngineAlterationSealed)
                return PushActivation(queued);
            if (queued.AbortAsked)
                return ExpectingActivation(queued);
        }

        DemandIntake(admission);
        DemandLined(admission, readied);
        if (!admission.HoldingsReadied)
        {
            throw new InvalidOperationException(
                "Collision generation can't commit prior to its assets are prepared");
        }
        if (admission.Completed)
        {
            throw new InvalidOperationException(
                "Collision generation has by now completed");
        }

        if (!readied.IsPrimedForActivation
            || OlderEpochLined(readied))
        {
            if (!readied.IsSealed)
            {
                throw new InvalidOperationException(
                    "Collision generation can't activate prior to sealing");
            }
            return new SimContactEpochCommit(
                new SimContactAck(
                    admission.LandblockId,
                    admission.Generation,
                    Engine.IsLbLandHoused(admission.LandblockId),
                    Ready: false),
                Array.Empty<uint>(),
                EngineCommitted: false,
                Completed: false);
        }

        SimContactPrefixEdit alteration;
        if (!_stemEdits.TryGetValue(
                admission.LandblockId,
                out alteration!))
        {
            var ticket =
                CommenceImpactStemStillness(
                    admission.LandblockId,
                    admission.Generation,
                    includeExteriorChambers: true);
            alteration = new SimContactPrefixEdit
            {
                Kind = SimContactPrefixEditKind.Activation,
                LbIdent = admission.LandblockId,
                EarlierGen = admission.EarlierGen,
                MarkGen = admission.Generation,
                Stillness = ticket,
                Admission = admission,
                Prepared = readied,
                WasHoused = Engine.IsLbLandHoused(
                    admission.LandblockId),
            };
            _stemEdits.Add(admission.LandblockId, alteration);

        }

        if (!TryObtainImpactStemAlterationPermission(
                alteration.Stillness,
                out SimContactPrefixEditLeave permission))

            return ExpectingActivation(alteration);
        alteration.Permission = permission;

        if (!IsImpactStemAlterationPermissionLatest(permission)
            || !readied.IsPrimedForActivation
            || OlderEpochLined(readied))

            return ExpectingActivation(alteration);

        var substitute =
            readied.GrabSealedSubstitute();
        Engine.SealLbSubstitute(substitute);
        BumpLinkRealmArbiter();
        _linedLinkEpochs.Remove(admission.LandblockId);
        readied.FlagSealed();
        alteration.EngineAlterationSealed = true;
        alteration.Ready = Engine.IsLbLandHoused(
            admission.LandblockId);
        alteration.WasHoused = alteration.Ready;
        SetPosition.CommitCollisionGeneration(
            alteration.LbIdent,
            alteration.MarkGen,
            alteration.Ready);
        var finished =
            PushActivation(alteration);
        return finished;
    }

    internal ulong AnticipatedImpactGen(uint preciseChamberIdent)
    {
        uint lbIdent = LbOf(preciseChamberIdent);
        if (lbIdent is 0u)
            return 0UL;
        if (_linkIntakes.TryGetValue(
                lbIdent,
                out SimContactIntake? admission))

            return admission.Generation;
        return _linkEpochs.TryGetValue(
                lbIdent,
                out ulong gen)
            ? checked(gen + 1UL)
            : 1UL;
    }

    internal bool IsImpactEvaluationStemAdmissible(uint preciseChamberIdent)
    {
        uint lbIdent = LbOf(preciseChamberIdent);
        return lbIdent is not 0u
            && !_linkIntakes.ContainsKey(lbIdent)
            && !SetPosition.IsImpactStemQuiescing(lbIdent);
    }

    internal ulong ImpactGenArbiter(uint preciseChamberIdent)
    {
        uint lbIdent = LbOf(preciseChamberIdent);
        return lbIdent is not 0u
            && _linkEpochs.TryGetValue(
                lbIdent,
                out ulong gen)
                ? gen
                : 0UL;
    }

    internal void ProgressImpactStillnessArbiter() =>
        BumpLinkRealmArbiter();

    internal bool TrySealImpactEvaluationArbiter(
        in PlaceOutcome outcome,
        ulong anticipatedImpactRealmArbiter,
        ulong anticipatedShadeRealmArbiter,
        ClientThingChart? anticipatedObjectChart,
        ulong anticipatedObjectChartMappingArbiter,
        ulong anticipatedObjectChartArbiter,
        out SimContactEvaluationAuthority arbiter)
    {
        arbiter = default;
        if (_linkRealmArbiter != anticipatedImpactRealmArbiter
            || ShadeRealmArbiter != anticipatedShadeRealmArbiter
            || !ReferenceEquals(ObjectChart, anticipatedObjectChart)
            || ObjectChartMappingArbiter
                != anticipatedObjectChartMappingArbiter
            || ObjectChartArbiter != anticipatedObjectChartArbiter)

            return false;

        HashSet<uint> stems = new HashSet<uint>();
        var authorities = ImmutableArray.CreateBuilder<
            SimContactEpochAuthority>();
        void Append(uint chamberIdent)
        {
            uint lb = LbOf(chamberIdent);
            if (lb is 0u || !stems.Add(lb))
                return;
            authorities.Add(new SimContactEpochAuthority(
                lb,
                ImpactGenArbiter(lb)));
        }

        Append(outcome.CellId);
        if (!outcome.QueriedCellIds.IsDefaultOrEmpty)
        {
            foreach (uint chamberTag in outcome.QueriedCellIds)
                Append(chamberTag);
        }
        foreach (uint stem in stems)
        {
            if (!IsImpactEvaluationStemAdmissible(stem))
                return false;
        }

        if (_linkRealmArbiter != anticipatedImpactRealmArbiter
            || ShadeRealmArbiter != anticipatedShadeRealmArbiter
            || !ReferenceEquals(ObjectChart, anticipatedObjectChart)
            || ObjectChartMappingArbiter
                != anticipatedObjectChartMappingArbiter
            || ObjectChartArbiter != anticipatedObjectChartArbiter)

            return false;
        arbiter = new SimContactEvaluationAuthority(
            anticipatedImpactRealmArbiter,
            anticipatedShadeRealmArbiter,
            anticipatedObjectChart,
            anticipatedObjectChartMappingArbiter,
            anticipatedObjectChartArbiter,
            authorities.ToImmutable());
        return true;
    }

    internal bool IsImpactEvaluationArbiterLatest(
        in SimContactEvaluationAuthority arbiter)
    {
        if (!arbiter.IsValid
            || _linkRealmArbiter != arbiter.CollisionWorldAuthority
            || ShadeRealmArbiter != arbiter.ShadowWorldAuthority
            || !ReferenceEquals(ObjectChart, arbiter.ObjectTable)
            || ObjectChartMappingArbiter
                != arbiter.ObjectTableBindingAuthority
            || ObjectChartArbiter != arbiter.ObjectTableAuthority)

            return false;
        foreach (SimContactEpochAuthority gen
                 in arbiter.Generations)
        {
            if (gen.LandblockId is 0u
                || _linkIntakes.ContainsKey(gen.LandblockId)
                || SetPosition.IsImpactStemQuiescing(
                    gen.LandblockId)
                || ImpactGenArbiter(gen.LandblockId)
                    != gen.Generation)

                return false;
        }
        return true;
    }

    internal bool IsImpactEvaluationFatalArbiterLatest(
        in SimContactEvaluationAuthority arbiter)
    {
        if (!arbiter.IsValid
            || _linkRealmArbiter != arbiter.CollisionWorldAuthority
            || !ReferenceEquals(ObjectChart, arbiter.ObjectTable)
            || ObjectChartMappingArbiter
                != arbiter.ObjectTableBindingAuthority
            || ObjectChartArbiter != arbiter.ObjectTableAuthority)

            return false;
        foreach (SimContactEpochAuthority gen
                 in arbiter.Generations)
        {
            if (gen.LandblockId is 0u
                || _linkIntakes.ContainsKey(gen.LandblockId)
                || SetPosition.IsImpactStemQuiescing(
                    gen.LandblockId)
                || ImpactGenArbiter(gen.LandblockId)
                    != gen.Generation)

                return false;
        }
        return true;
    }

    internal bool HandleSetPositionCollisions(
        SimActorRecord capture,
        ulong locusArbiterVer,
        ulong spatialArbiterVer,
        ulong velArbiterVer,
        double kineticsMoment,
        bool earlierLink,
        bool earlierOnPassable,
        in PlaceContactReport dossier)
    {
        if (!Entities.IsCurrent(capture)
            || capture.PositionAuthorityVersion != locusArbiterVer
            || capture.SpatialAuthorityVersion != spatialArbiterVer
            || capture.KineticBody is not { } corpus)

            return false;

        _ = earlierLink;
        _ = earlierOnPassable;
        bool latest = ProcessSetLocusImpactDossiers(
            capture,
            locusArbiterVer,
            spatialArbiterVer,
            kineticsMoment,
            earlierLink: false,
            earlierOnPassable: false,
            collidedWithSurroundings: dossier.CollidedWithEnvironment,
            collidedObjectIdents: dossier.CollidedObjectIds,
            impactHandlerOutcome: out bool impactHandlerOutcome);
        if (!latest)
            return impactHandlerOutcome;
        if (velArbiterVer is not 0UL
            && capture.VelArbiterVer != velArbiterVer)

            return impactHandlerOutcome;

        corpus.FramesStationaryFall = dossier.FramesStationaryFall;
        KineticObjUpdate.HandleAllCollisions(
            corpus,
            dossier.CollisionNormalValid,
            dossier.CollisionNormal,
            earlierLink: false,
            earlierOnPassable: false,
            instantOnPassable: corpus.OnWalkable);
        corpus.TransientState &= ~(TransientPhaseFlagSet.StationaryFall
            | TransientPhaseFlagSet.StationaryStop
            | TransientPhaseFlagSet.StationaryStuck);
        corpus.TransientState |= dossier.FramesStationaryFall switch
        {
            1 => TransientPhaseFlagSet.StationaryFall,
            2 => TransientPhaseFlagSet.StationaryStop,
            3 => TransientPhaseFlagSet.StationaryStuck,
            _ => TransientPhaseFlagSet.None,
        };
        return impactHandlerOutcome;
    }

    internal bool ProcessSetLocusImpactDossiers(
        SimActorRecord capture,
        ulong locusArbiterVer,
        ulong spatialArbiterVer,
        double kineticsMoment,
        bool earlierLink,
        bool earlierOnPassable,
        bool collidedWithSurroundings,
        System.Collections.Immutable.ImmutableArray<uint> collidedObjectIdents,
        out bool impactHandlerOutcome)
    {
        impactHandlerOutcome = false;
        if (!Entities.IsCurrent(capture)
            || capture.PositionAuthorityVersion != locusArbiterVer
            || capture.SpatialAuthorityVersion != spatialArbiterVer
            || capture.KineticBody is not { } corpus)

            return false;

        impactHandlerOutcome = ImpactDossiers.ProcessDossiers(
            capture,
            corpus,
            kineticsMoment,
            earlierLink,
            earlierOnPassable,
            collidedWithSurroundings,
            collidedObjectIdents);
        return Entities.IsCurrent(capture)
            && capture.PositionAuthorityVersion == locusArbiterVer
            && capture.SpatialAuthorityVersion == spatialArbiterVer
            && ReferenceEquals(capture.KineticBody, corpus)
            && corpus.InWorld;
    }

    private SimContactEpochCommit ExpectingActivation(
        SimContactPrefixEdit alteration)
    {
        return new(
        new SimContactAck(
            alteration.LbIdent,
            alteration.MarkGen,
            alteration.WasHoused,
            Ready: alteration.EngineAlterationSealed && alteration.Ready),
        Array.Empty<uint>(),
        EngineCommitted: alteration.EngineAlterationSealed,
        Completed: false);
    }

    private SimContactEpochCommit PushActivation(
        SimContactPrefixEdit alteration)
    {
        if (!alteration.EngineAlterationSealed)
            throw new InvalidOperationException(
                "Collision activation can't release prior to its engine transaction commits");

        bool finished = SetPosition.FreeImpactStemFollowingAlteration(
            alteration.Stillness,
            alteration.MarkGen,
            alteration.Ready);
        SimContactAck acknowledgement = new SimContactAck(
            alteration.LbIdent,
            alteration.MarkGen,
            alteration.WasHoused,
            alteration.Ready);
        if (!finished)
        {
            return new SimContactEpochCommit(
                acknowledgement,
                Array.Empty<uint>(),
                EngineCommitted: true,
                Completed: false);
        }

        SimContactIntake admission = alteration.Admission
            ?? throw new InvalidOperationException(
                "Collision activation lost its admission owner");
        admission.Completed = true;
        if (_linkIntakes.TryGetValue(
                alteration.LbIdent,
                out SimContactIntake? latest)
            && ReferenceEquals(latest, admission))

            _linkIntakes.Remove(alteration.LbIdent);
        _stemEdits.Remove(alteration.LbIdent);
        ProclaimEpochSealed(
            new SimContactEpochCommitted(
                acknowledgement.LandblockId,
                acknowledgement.Generation,
                acknowledgement.Ready));
        _ = SetPosition.TryRecoverPostponedForLb(
            acknowledgement.LandblockId);
        return new SimContactEpochCommit(
            acknowledgement,
            Array.Empty<uint>(),
            EngineCommitted: true,
            Completed: true);
    }

    private static SimContactEditResult ExpectingSunset(
        SimContactPrefixEdit alteration)
    {
        return new(
        new SimContactAck(
            alteration.LbIdent,
            alteration.MarkGen,
            alteration.WasHoused,
            Ready: alteration.EngineAlterationSealed && alteration.Ready),
        Completed: false);
    }

    internal ulong ImpactRealmArbiter => _linkRealmArbiter;

    private void SealInvalidation(
        SimContactPrefixEdit alteration)
    {
        _linkEpochs[alteration.LbIdent] =
            alteration.MarkGen;
        SetPosition.AbortImpactGen(
            alteration.LbIdent,
            alteration.InvalidatedGen);
        if (_linkIntakes.TryGetValue(
                alteration.LbIdent,
                out SimContactIntake? latestAdmission)
            && ReferenceEquals(latestAdmission, alteration.Admission))

            _linkIntakes.Remove(alteration.LbIdent);
        if (_linedLinkEpochs.TryGetValue(
                alteration.LbIdent,
                out StagedLandblockContactEpoch? latestReadied)
            && ReferenceEquals(latestReadied, alteration.Prepared))
        {
            _linedLinkEpochs.Remove(alteration.LbIdent);
            latestReadied.Dispose();
        }
    }

    private SimContactEditResult PushSunset(
        uint landblockId,
        SimContactPrefixEditKind kind)
    {
        Live();
        DemandLinkThread();
        if (landblockId is 0u)
            throw new ArgumentOutOfRangeException(nameof(landblockId));
        uint canon = LbOf(landblockId);
        if (kind is SimContactPrefixEditKind.Activation)
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (!_stemEdits.TryGetValue(
                canon,
                out SimContactPrefixEdit? alteration))
        {
            ulong latestGen = _linkEpochs.TryGetValue(
                canon,
                out ulong latest)
                    ? latest
                    : 0UL;
            var admission =
                _linkIntakes.GetValueOrDefault(canon);
            ulong earlierGen = admission?.EarlierGen
                ?? latestGen;
            ulong invalidatedGen = admission is not null
                    ? admission.Generation
                    : checked(latestGen + 1UL);
            ulong markGen = checked(
                Math.Max(latestGen, invalidatedGen) + 1UL);
            SetPosition.CommenceImpactGen(
                canon,
                markGen);
            var ticket =
                CommenceImpactStemStillness(
                    canon,
                    markGen,
                    includeExteriorChambers:
                        kind is SimContactPrefixEditKind.Withdrawal);
            alteration = new SimContactPrefixEdit
            {
                Kind = kind,
                LbIdent = canon,
                EarlierGen = earlierGen,
                InvalidatedGen = invalidatedGen,
                MarkGen = markGen,
                Stillness = ticket,
                Admission = admission,
                Prepared = _linedLinkEpochs.GetValueOrDefault(
                    canon),
                WasHoused = Engine.IsLbLandHoused(canon),
            };
            _stemEdits.Add(canon, alteration);
        }
        if (alteration.Kind != kind)
        {
            throw new InvalidOperationException(
                "A different collision-prefix mutation owns this landblock");
        }

        if (!alteration.EngineAlterationSealed)
        {
            if (!TryObtainImpactStemAlterationPermission(
                    alteration.Stillness,
                    out SimContactPrefixEditLeave permission)
                || !IsImpactStemAlterationPermissionLatest(permission))

                return ExpectingSunset(alteration);
            alteration.Permission = permission;
            SealInvalidation(alteration);

            if (kind is SimContactPrefixEditKind.Demotion)
                Engine.DemoteLandblockToTerrain(canon);
            else
                Engine.RemoveLandblock(canon);
            BumpLinkRealmArbiter();
            alteration.EngineAlterationSealed = true;
            alteration.Ready = kind is SimContactPrefixEditKind.Demotion
                && Engine.IsLbLandHoused(canon);
            if (alteration.Ready)
            {
                SetPosition.CommitCollisionGeneration(
                    canon,
                    alteration.MarkGen,
                    primed: true);
            }
        }

        bool finished = SetPosition.FreeImpactStemFollowingAlteration(
            alteration.Stillness,
            alteration.MarkGen,
            alteration.Ready);
        if (finished)
            _stemEdits.Remove(canon);
        return new SimContactEditResult(
            new SimContactAck(
                canon,
                alteration.MarkGen,
                alteration.WasHoused,
                alteration.Ready),
            finished);
    }

    private void DemandLinkThread()
    {
        int latest = Environment.CurrentManagedThreadId;
        int holder = Interlocked.CompareExchange(
            ref _linkThreadIdent,
            latest,
            0);
        if (holder is not 0 && holder != latest)
        {
            throw new InvalidOperationException(
                "Collision generations has to be staged and committed on one update thread");
        }
    }

    private void DemandIntake(SimContactIntake admission)
    {
        Live();
        ArgumentNullException.ThrowIfNull(admission);
        if (!ReferenceEquals(admission.Owner, this)
            || !_linkIntakes.TryGetValue(
                admission.LandblockId,
                out SimContactIntake? latest)
            || !ReferenceEquals(latest, admission)
            || !_linkEpochs.TryGetValue(
                admission.LandblockId,
                out ulong gen)
            || gen != admission.Generation)
        {
            throw new InvalidOperationException(
                "Collision admission is stale or belongs to another Runtime");
        }
    }

    private void DemandLined(
        SimContactIntake admission,
        StagedLandblockContactEpoch readied)
    {
        ArgumentNullException.ThrowIfNull(readied);
        ObjectDisposedException.ThrowIf(readied.IsDestroyed, readied);
        if (!readied.Fits(this, admission))
        {
            throw new InvalidOperationException(
                "Prepared collision generation is stale or belongs to another admission");
        }
    }

    private void ProclaimEpochSealed(
        SimContactEpochCommitted committed)
    {
        for (int ordinal = 0;
             ordinal < _epochSealTaps.Count;
             ++ordinal)
        {
            try
            {
                _epochSealTaps[ordinal](committed);
            }
            catch (Exception problem)
            {
                System.Diagnostics.Trace.TraceError(
                    "Collision-generation commit observer failed after activation: {0}",
                    problem);
            }
        }
    }

    private void BumpLinkRealmArbiter() =>
        _linkRealmArbiter = checked(_linkRealmArbiter + 1UL);

    private bool OlderEpochLined(
        StagedLandblockContactEpoch contender)
    {
        foreach ((_, StagedLandblockContactEpoch another) in
                 _linedLinkEpochs)
        {
            if (another.Series < contender.Series)
                return true;
        }
        return false;
    }

    private static uint LbOf(uint val) =>
        (val & 0xFFFF0000u) | 0xFFFFu;
}

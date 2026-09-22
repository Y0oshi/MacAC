using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

public interface ISimEpochResetHarbor
{
    void RetireActorProj(SimActorRecord actor);

    void EmptyActorProjBoundary();

    void ConcludeActorProjSunset();
}

public enum SimEpochResetStage
{
    None = 0,
    Transit = 1,
    CommandTargets = 2,
    ExternalContainer = 3,
    Actions = 4,
    Movement = 5,
    ObjectTable = 6,
    Character = 7,
    ItemMana = 8,
    Friends = 9,
    Squelch = 10,
    NegotiatedChannels = 11,
    Fellowship = 12,
    Allegiance = 13,
    Trade = 14,
    House = 15,
    Contracts = 16,
    Journal = 17,
    BeginEntityRetirement = 18,
    RetireEntities = 19,
    DrainHostProjection = 20,
    CompleteCanonicalEntities = 21,
    CompleteHostProjection = 22,
    ChatIdentity = 23,
    PlayerSnapshots = 24,
    PlayerIdentity = 25,
    Complete = 26,
}

public readonly record struct SimEpochResetCapture(
    bool IsActive,
    bool IsExecuting,
    SimEpochTicket RetiringGeneration,
    SimEpochTicket LastCompletedGeneration,
    SimEpochResetStage Stage,
    int RetirementCount,
    int RetirementCursor,
    bool CurrentProjectionAcknowledged,
    long TransactionId)
{
    public bool IsConverged => !IsActive && !IsExecuting;
}

public sealed class SimEpochResetStageException(SimEpochTicket sunsettingGen, SimEpochResetStage juncture, Exception interiorException)
    : Exception($"Runtime generation {sunsettingGen.Value} reset stage '{juncture}' did not converge.", interiorException)
{
    public SimEpochTicket SunsettingGen { get; } = sunsettingGen;

    public SimEpochResetStage Stage { get; } = juncture;
}

public sealed class SimEpochReset
{
    private readonly SimRealmCrossingLedger _passage;
    private readonly SimCommsLedger _communication;
    private readonly SimStashLedger _satchel;
    private readonly SimActionLedger _actions;
    private readonly SimAvatarLocomotionLedger _movement;
    private readonly SimActorObjectLifetime _entityObjects;
    private readonly SimToonLedger _toon;
    private readonly SimAvatarIdentityLedger _identity;
    private readonly SimFellowsLedger _fellowship;
    private readonly SimAllegianceLedger _allegiance;
    private readonly SimBarterLedger _barter;
    private readonly SimContractLedger _contracts;
    private readonly SimDiaryLedger _journal;
    private readonly SimDwellingLedger _house;

    // The simple stages: each is one ledger reset, in enum order from Transit through Journal
    private readonly (SimEpochResetStage Stage, Action Run)[] _simpleJunctures;

    private Run? _exec;
    private SimEpochTicket _previousFinishedGen;
    private bool _hasFinishedGen;
    private bool _executing;
    private long _upcomingTransactionIdent;

    internal SimEpochReset(
        SimRealmCrossingLedger transit,
        SimCommsLedger communication,
        SimStashLedger inventory,
        SimActionLedger actions,
        SimAvatarLocomotionLedger movement,
        SimActorObjectLifetime entityObjects,
        SimToonLedger character,
        SimAvatarIdentityLedger identity,
        SimFellowsLedger fellowship,
        SimAllegianceLedger allegiance,
        SimBarterLedger trade,
        SimDwellingLedger house,
        SimContractLedger contracts,
        SimDiaryLedger journal)
    {
        _passage = transit ?? throw new ArgumentNullException(nameof(transit));
        _communication = communication ?? throw new ArgumentNullException(nameof(communication));
        _satchel = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _entityObjects = entityObjects ?? throw new ArgumentNullException(nameof(entityObjects));
        _toon = character ?? throw new ArgumentNullException(nameof(character));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _fellowship = fellowship ?? throw new ArgumentNullException(nameof(fellowship));
        _allegiance = allegiance ?? throw new ArgumentNullException(nameof(allegiance));
        _barter = trade ?? throw new ArgumentNullException(nameof(trade));
        _house = house ?? throw new ArgumentNullException(nameof(house));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));

        _simpleJunctures =
        [
            (SimEpochResetStage.Transit, _passage.ResetSession),
            (SimEpochResetStage.CommandTargets, _communication.RestartDirectiveMarks),
            (SimEpochResetStage.ExternalContainer, () => { _satchel.RestartExternalVessel(); _satchel.RestartMerchant(); }),
            (SimEpochResetStage.Actions, _actions.ResetSession),
            (SimEpochResetStage.Movement, _movement.ResetSession),
            (SimEpochResetStage.ObjectTable, _entityObjects.WipeObjects),
            (SimEpochResetStage.Character, _toon.ResetSession),
            (SimEpochResetStage.ItemMana, _satchel.RestartGearMana),
            (SimEpochResetStage.Friends, _communication.RestartFriends),
            (SimEpochResetStage.Squelch, _communication.RestartSquelch),
            (SimEpochResetStage.NegotiatedChannels, _communication.RestartNegotiatedLanes),
            (SimEpochResetStage.Fellowship, _fellowship.ResetSession),
            (SimEpochResetStage.Allegiance, _allegiance.ResetSession),
            (SimEpochResetStage.Trade, _barter.Clear),
            (SimEpochResetStage.House, _house.ResetSession),
            (SimEpochResetStage.Contracts, _contracts.ResetSession),
            (SimEpochResetStage.Journal, _journal.ResetSession),
            (SimEpochResetStage.ChatIdentity, () => { _communication.RestartCommsPersona(); _communication.RestartSpewBbox(); }),
            (SimEpochResetStage.PlayerSnapshots, _satchel.RestartAvatarCaptures),
        ];
    }

    public SimEpochTicket? EngagedSunsettingGen => _exec?.Generation;

    public SimEpochResetCapture CaptureSnapshot()
    {
        Run? exec = _exec;
        if (exec is null)
        {
            return new SimEpochResetCapture(
                false, _executing, default, _previousFinishedGen,
                _hasFinishedGen ? SimEpochResetStage.Complete : SimEpochResetStage.None,
                0, 0, false, 0);
        }
        return new SimEpochResetCapture(
            true, _executing, exec.Generation, _previousFinishedGen, exec.Stage,
            exec.Retirements?.Length ?? 0, exec.SunsetCursor, exec.LatestProjAcknowledged, exec.TransactionIdent);
    }

    public void Reset(SimEpochTicket sunsettingGen, ISimEpochResetHarbor hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        if (_executing)
            throw new InvalidOperationException("Runtime generation reset can't run concurrently or reentrantly");

        Run exec = Admit(sunsettingGen, hub);
        if (exec.Stage is SimEpochResetStage.Complete)
            return;

        _executing = true;
        try
        {
            while (exec.Stage is not SimEpochResetStage.Complete)
                ResetLoop(exec);
        }
        catch (Exception problem)
        {
            throw new SimEpochResetStageException(exec.Generation, exec.Stage, problem);
        }
        finally
        {
            _executing = false;
        }
    }

    // Resumes a reset that stopped part-way, if there is one
    internal void EmptyQueued()
    {
        if (_exec is { } exec)
            Reset(exec.Generation, exec.Host);
    }

    private Run Admit(SimEpochTicket gen, ISimEpochResetHarbor hub)
    {
        if (_exec is { } queued)
        {
            if (queued.Generation != gen)
            {
                throw new InvalidOperationException(
                    $"Runtime generation {queued.Generation.Value} reset must converge prior to generation {gen.Value} can reset");
            }
            if (!ReferenceEquals(queued.Host, hub))
                throw new InvalidOperationException("An in-progress Runtime generation reset can't replace its borrowed projection host");
            return queued;
        }

        if (_hasFinishedGen)
        {
            if (gen == _previousFinishedGen)
                return Run.AlreadyDone(gen, hub);
            if (gen.Value < _previousFinishedGen.Value)
            {
                throw new InvalidOperationException(
                    $"Runtime generation {gen.Value} reset is stale; generation {_previousFinishedGen.Value} by now converged");
            }
        }

        return _exec = new Run(gen, hub, checked(++_upcomingTransactionIdent));
    }

    private void ResetLoop(Run exec)
    {
        foreach ((SimEpochResetStage juncture, Action job) in _simpleJunctures)
        {
            if (juncture == exec.Stage)
            {
                job();
                exec.Stage++;
                return;
            }
        }

        switch (exec.Stage)
        {
            case SimEpochResetStage.BeginEntityRetirement:
                _ = _entityObjects.OpenSessWipe();
                exec.Retirements = [.. _entityObjects.GrabSessWipeRetirements()];
                exec.Stage = SimEpochResetStage.RetireEntities;
                break;
            case SimEpochResetStage.RetireEntities:
                RetireUpcoming(exec);
                break;
            case SimEpochResetStage.DrainHostProjection:
                exec.Host.EmptyActorProjBoundary();
                exec.Stage = SimEpochResetStage.CompleteCanonicalEntities;
                break;
            case SimEpochResetStage.CompleteCanonicalEntities:
                if (!_entityObjects.FinishSessWipeIfConverged())
                    throw new InvalidOperationException("Canonical entity/object lifetime still owns an unacknowledged session retirement");
                exec.Stage = SimEpochResetStage.CompleteHostProjection;
                break;
            case SimEpochResetStage.CompleteHostProjection:
                exec.Host.ConcludeActorProjSunset();
                exec.Stage = SimEpochResetStage.ChatIdentity;
                break;
            case SimEpochResetStage.PlayerIdentity:
                _identity.ResetSession();
                exec.Stage = SimEpochResetStage.Complete;
                _previousFinishedGen = exec.Generation;
                _hasFinishedGen = true;
                _exec = null;
                break;
            default:
                throw new InvalidOperationException($"Not supported Runtime generation reset stage {exec.Stage}.");
        }
    }

    // Retires one entity: the host lets go of its projection first, then the lifetime completes it
    private void RetireUpcoming(Run exec)
    {
        SimActorRecord[] retirements = exec.Retirements ?? [];
        if (exec.SunsetCursor >= retirements.Length)
        {
            exec.Stage = SimEpochResetStage.DrainHostProjection;
            return;
        }

        var latest = retirements[exec.SunsetCursor];
        if (!exec.LatestProjAcknowledged)
        {
            exec.Host.RetireActorProj(latest);
            exec.LatestProjAcknowledged = true;
        }

        _entityObjects.ConcludeSessActorSunset(latest);
        exec.LatestProjAcknowledged = false;
        exec.SunsetCursor++;
    }

    private sealed class Run(SimEpochTicket gen, ISimEpochResetHarbor hub, long transactionIdent)
    {
        public SimEpochTicket Generation { get; } = gen;
        public ISimEpochResetHarbor Host { get; } = hub;
        public long TransactionIdent { get; } = transactionIdent;
        public SimEpochResetStage Stage { get; set; } = SimEpochResetStage.Transit;
        public SimActorRecord[]? Retirements { get; set; }
        public int SunsetCursor { get; set; }
        public bool LatestProjAcknowledged { get; set; }

        public static Run AlreadyDone(SimEpochTicket gen, ISimEpochResetHarbor hub) =>
            new(gen, hub, 0) { Stage = SimEpochResetStage.Complete };
    }
}

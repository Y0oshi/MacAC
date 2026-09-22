using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Character select: entering the world, logging a character off, create/delete/restore.</summary>
public sealed partial class RealmSession
{
    private const uint SrvPrimedOpcode = 0xF7DFu;

    internal readonly record struct EntryPick(ToonRoster.Toon Character, byte[] EnterWorldBody);

    public bool IsToonLogOffConfirmed => Volatile.Read(ref _toonLogOffConfirmed) is not 0;

    public void LaunchToonPickTake()
    {
        if (LatestPhase != State.InCharacterSelect)
            throw new InvalidOperationException("character-selection receive needs InCharacterSelect state");
        SecureNetTakeLoopBegun();
    }

    public void RequestCharacterLogOff()
    {
        if (LatestPhase != State.InWorld || _engagedToonIdent is 0)
            throw new InvalidOperationException("character logoff needs an in-world session with an active character");

        Interlocked.Exchange(ref _toonLogOffConfirmed, 0);
        TransmitPlayMsg(CharacterExit.ConstructReqCorpus(_engagedToonIdent));
    }

    public void YieldToCharacterSelect()
    {
        if (LatestPhase is not (State.InWorld or State.EnteringWorld))
            throw new InvalidOperationException("return-to-character-select needs an in-world session");

        _engagedToonIdent = 0;
        _playActSeries = 0;
        Interlocked.Exchange(ref _toonLogOffConfirmed, 0);
        Transition(State.InCharacterSelect);
    }

    public void EnterWorld(int toonOrdinal = 0, TimeSpan? timeout = null)
    {
        if (Characters is null || Characters.Characters.Count is 0)
            throw new InvalidOperationException("Connect() must complete with a non-empty ToonRoster");
        EntryPick choose = PickToonForJoinRealm(Characters, toonOrdinal);
        JoinRealmCore(choose.Character.Id, choose.EnterWorldBody, timeout);
    }

    public void EnterWorld(uint toonOid, string acctLabel, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(acctLabel);
        JoinRealmCore(toonOid, CharacterEntry.AssembleJoinRealmCorpus(toonOid, acctLabel), timeout);
    }

    public void TransmitEraseToon(string acctLabel, int activeIndex)
    {
        ArgumentNullException.ThrowIfNull(acctLabel);
        if (activeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(activeIndex));
        TransmitPlayMsg(CharacterErase.AssembleReqCorpus(acctLabel, checked((uint)activeIndex)), GameQueueGroup.LoginQueue);
    }

    public void TransmitRevertToon(uint toonIdent)
    {
        _genesisAwaiting = GenesisAwaiting.Restore;
        TransmitControlMsg(CharacterRevive.BuildReqBody(toonIdent));
    }

    public void TransmitToonCreation(string acctLabel, CharacterForge.WireRequest req, ReadOnlySpan<uint> aptitudeAdvancementClasses)
    {
        byte[] corpus = CharacterForge.AssembleRequestBody(acctLabel, req, aptitudeAdvancementClasses);
        _genesisAwaiting = GenesisAwaiting.Create;
        TransmitPlayMsg(corpus, GameQueueGroup.LoginQueue);
    }

    internal static EntryPick PickToonForJoinRealm(ToonRoster.ParsedUnit toons, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(toons);
        if (characterIndex < 0 || characterIndex >= toons.Characters.Count)
            throw new ArgumentOutOfRangeException(nameof(characterIndex));

        var chosen = toons.Characters[characterIndex];
        if (!ToonRoster.IsOnHandEngagedPersona(chosen))
            throw new InvalidOperationException("selected character has to be an active, non-greyed identity");
        return new EntryPick(chosen, CharacterEntry.AssembleJoinRealmCorpus(chosen.Id, toons.AccountName));
    }

    // Asks to enter, waits for ServerReady (or a character error), then sends the enter-world body
    private void JoinRealmCore(uint toonOid, byte[] joinRealmCorpus, TimeSpan? timeout)
    {
        HurlIfBlobVerifyFailed();
        if (!_blobVerifyDone)
            throw new InvalidOperationException("The game-data check must finish prior to entering the world");

        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        _engagedToonIdent = toonOid;
        Transition(State.EnteringWorld);
        TransmitPlayMsg(CharacterEntry.AssembleJoinRealmReqCorpus());
        _previousToonPickProblem = null;

        bool primed = _netTakeTask is null
            ? PumpUntilSrvPrimed(deadline)
            : deadline - DateTime.UtcNow is { } left && left > TimeSpan.Zero && PauseForToonLogOffAck(
                _incomingFifo.Reader,
                left,
                datagram =>
                {
                    List<uint> opcodes = new List<uint>();
                    Absorb(datagram.Memory, opcodes);
                    return opcodes.Contains(SrvPrimedOpcode) || _previousToonPickProblem is not null;
                },
                YieldIncomingDatagram,
                SweepConveyance,
                TimeSpan.FromMilliseconds(25));

        if (_previousToonPickProblem is { } refusal)
        {
            Transition(State.InCharacterSelect);
            SecureNetTakeLoopBegun();
            throw new CharacterPickRefusedException(refusal);
        }
        if (!primed)
        {
            Transition(State.Failed);
            throw new TimeoutException("ServerReady not received");
        }

        TransmitPlayMsg(joinRealmCorpus);
        Transition(State.InWorld);
        SecureNetTakeLoopBegun();
    }

    private bool PumpUntilSrvPrimed(DateTime deadline)
    {
        while (DateTime.UtcNow < deadline && _previousToonPickProblem is null)
        {
            bool got = PumpOnce(out List<uint> opcodes);
            SweepConveyance();
            if (got && opcodes.Contains(SrvPrimedOpcode))
                return true;
        }
        return false;
    }
}

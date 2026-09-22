using System.Net;
using System.Net.Sockets;
using MacAC.Mechanics.Genesis;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionDriver
{

    public SimDirectiveResult Highlight(
        SimEpochTicket anticipatedGen,
        uint toonIdent)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            SimDirectiveStatus condition =
                ToonPickPhase.TryHighlight(toonIdent)
                    ? SimDirectiveStatus.Accepted
                    : SimDirectiveStatus.Rejected;
            return ChooseOutcome(condition, toonIdent);
        }
    }

    public SimDirectiveResult Enter(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            return _depth is not 0 ? ChooseOutcome(SimDirectiveStatus.Rejected) : AtTopTier(JoinPicked);
        }
    }

    public SimDirectiveResult ReqErase(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            SimDirectiveStatus condition =
                ToonPickPhase.TryReqErase(out uint toonIdent)
                    ? SimDirectiveStatus.Accepted
                    : SimDirectiveStatus.Rejected;
            return ChooseOutcome(condition, toonIdent);
        }
    }

    public SimDirectiveResult ConfirmErase(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            if (!ToonPickPhase.TryGrabEraseAck(
                    out SimToonPickEntry toon,
                    out string acctLabel))

                return ChooseOutcome(SimDirectiveStatus.Rejected);

            try
            {
                _ops.EraseToon(
                    _ambit!.Session,
                    acctLabel,
                    toon.ActiveIndex);
                return ChooseOutcome(
                    SimDirectiveStatus.Accepted,
                    toon.CharacterId);
            }
            catch
            {
                ToonPickPhase.ImposeProblem(
                    new CharacterFault.ParsedDef(
                        (uint)CharacterFault.WireCode.Delete));
                return ChooseOutcome(
                    SimDirectiveStatus.Rejected,
                    toon.CharacterId);
            }
        }
    }

    public SimDirectiveResult Restore(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            if (!ToonPickPhase.TryCommenceRevert(
                    out SimToonPickEntry toon))

                return ChooseOutcome(SimDirectiveStatus.Rejected);

            try
            {
                _ops.ReinstateToon(
                    _ambit!.Session,
                    toon.CharacterId);
                return ChooseOutcome(
                    SimDirectiveStatus.Accepted,
                    toon.CharacterId);
            }
            catch
            {
                ToonPickPhase.ImposeProblem(
                    new CharacterFault.ParsedDef(
                        (uint)CharacterFault.WireCode.Undefined));
                return ChooseOutcome(
                    SimDirectiveStatus.Rejected,
                    toon.CharacterId);
            }
        }
    }

    public SimDirectiveResult Cancel(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchChoose(
                anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return ChooseOutcome(latch);
            return ChooseOutcome(
                ToonPickPhase.Cancel()
                    ? SimDirectiveStatus.Accepted
                    : SimDirectiveStatus.Rejected);
        }
    }

    public SimDirectiveResult OpenToonLogOff(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            SimEpochTicket latest = new(_epoch);
            if (anticipatedGen != latest)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.StaleGeneration,
                    latest);
            }
            if (_destroyed
                || _teardownAsked
                || _ambit is null
                || !_inRealm
                || _depth is not 0)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.Inactive,
                    latest);
            }

            Scope ambit = _ambit;
            ExecuteLogoffDrainTap(ambit.Session);
            try
            {
                _ops.ReqToonLogOff(ambit.Session);
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine(
                    $"live: character-logoff request failed: {problem.Message}");
                return new SimDirectiveResult(
                    SimDirectiveStatus.Rejected,
                    latest);
            }

            return new SimDirectiveResult(
                SimDirectiveStatus.Accepted,
                latest);
        }
    }

    public SimDirectiveResult FinishToonLogOff(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            SimEpochTicket latest = new(_epoch);
            if (anticipatedGen != latest)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.StaleGeneration,
                    latest);
            }
            if (_destroyed
                || _teardownAsked
                || _ambit is null
                || _sunsetting is not null
                || !_inRealm)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.Inactive,
                    latest);
            }
            if (_depth is not 0)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.Rejected,
                    latest);
            }

            return AtTopTier(CompleteLogOff);
        }
    }
    private PickWireBinding AttachToonPick(
        Scope ambit,
        ulong gen)
    {
        return new(
            ambit.Session,
            lineup =>
            {
                lock (_latch)
                {
                    if (!AmbitHolds(ambit, gen))
                        return;
                    _createsSinceLineup = 0;
                    var dossier = LineupOf(lineup);
                    ToonPickPhase.ImposeLineup(dossier);
                    ambit.Host.AnnounceLineup(dossier);
                }
            },
            () =>
            {
                lock (_latch)
                {
                    if (AmbitHolds(ambit, gen))
                        ToonPickPhase.ImposeEraseAcknowledged();
                }
            },
            revert =>
            {
                lock (_latch)
                {
                    if (AmbitHolds(ambit, gen))
                        ToonPickPhase.ImposeRevert(revert);
                }
            },
            problem =>
            {
                lock (_latch)
                {
                    if (AmbitHolds(ambit, gen))
                        ToonPickPhase.ImposeProblem(problem);
                }
            },
            realmLabel =>
            {
                lock (_latch)
                {
                    if (AmbitHolds(ambit, gen))
                        ToonPickPhase.ImposeRealmLabel(realmLabel.WorldName);
                }
            },
            built =>
            {
                lock (_latch)
                {
                    if (AmbitHolds(ambit, gen))
                        OnGenesisVerdict(ambit, gen, built);
                }
            });
    }

    private SimDirectiveResult CompleteLogOff()
    {
        Scope ambit = _ambit!;
        var hub = ambit.Host;
        var sess = ambit.Session;
        var sunsetting = ambit.Generation;
        try
        {
            ambit.Mapping?.Dispose();
            ambit.ToonPickMapping?.Dispose();

            if (ambit.HubAffixed)
            {
                hub.UnfastenSess(sess);
                ambit.HubAffixed = false;
            }
            hub.RewindSessPhase(sunsetting);

            _ops.YieldToCharacterSelect(sess);

            ulong gen = ++_epoch;
            SimEpochTicket engagedGen = new SimEpochTicket(gen);
            _inRealm = false;
            _choose = null;
            ToonPickPhase.Reset(engagedGen);
            ToonCreationPhase.Reset(engagedGen);
            _createsSinceLineup = 0;
            ToonPickPhase.Begin(engagedGen);
            ToonCreationPhase.Begin(engagedGen);

            Scope newAmbit = new Scope(sess, hub, engagedGen);
            _ambit = newAmbit;
            var mapping = hub.AttachSess(sess);
            newAmbit.Mapping = mapping;
            newAmbit.HubAffixed = true;
            newAmbit.ToonPickMapping = AttachToonPick(
                newAmbit,
                gen);

            var toons =
                _ops.FetchToons(sess);
            if (toons is not null)
            {
                _createsSinceLineup = 0;
                var lineup = LineupOf(toons);
                ToonPickPhase.ImposeLineup(lineup);
                hub.AnnounceLineup(lineup);
            }
            if (_ops.FetchSrvDetails(sess) is { } srvDetails)
                ToonPickPhase.ImposeRealmLabel(srvDetails.WorldName);

            uint upcomingSignin = _upcomingSigninIdent;
            if (upcomingSignin is not 0u
                && ToonPickPhase.TryHighlight(upcomingSignin))
            {
                var entered = JoinPicked();
                if (entered.Status == SimDirectiveStatus.Accepted)
                    _upcomingSigninIdent = 0u;
                return entered;
            }

            Console.WriteLine(
                "live: character logoff complete - returned to character "
                + "select (session connected)");
            return new SimDirectiveResult(
                SimDirectiveStatus.Accepted,
                engagedGen);
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine(
                "live: return-to-character-select failed; stopping session: "
                + problem.Message);
            _ = HaltFollowing(problem);
            return new SimDirectiveResult(
                SimDirectiveStatus.Rejected,
                new SimEpochTicket(_epoch));
        }
    }

    private SimDirectiveResult JoinPicked()
    {
        return JoinHighlighted(static (ops, sess, toon, _) =>
            ops.EnterWorld(sess, toon.ActiveIndex));
    }

    private SimDirectiveResult JoinBuilt(
        SimToonGenesisIdentity persona)
    {
        return JoinHighlighted((ops, sess, _, acctLabel) =>
            ops.JoinRealmByOid(sess, persona.Guid, acctLabel));
    }

    private SimDirectiveResult JoinHighlighted(
        Action<IOnlineSessionOps, RealmSession, SimToonPickEntry, string> transmitJoinRealm)
    {
        Scope ambit = _ambit!;
        ulong gen = _epoch;
        if (!ToonPickPhase.CommenceJoin(
                out SimToonPickEntry toon))

            return ChooseOutcome(SimDirectiveStatus.Rejected);

        var capture =
            ToonPickPhase.Snapshot;
        OnlineSessionToonPick pick = new OnlineSessionToonPick(
            toon.ActiveIndex,
            toon.CharacterId,
            toon.Name,
            capture.AccountName);
        try
        {
            ambit.Host.ImposeChosenToon(pick);
            if (!AmbitHolds(ambit, gen))
                return ChooseOutcome(SimDirectiveStatus.Inactive);

            transmitJoinRealm(_ops, ambit.Session, toon, capture.AccountName);
            if (!AmbitHolds(ambit, gen))
                return ChooseOutcome(SimDirectiveStatus.Inactive);

            ambit.Mapping!.EngageDirectives();
            if (!AmbitHolds(ambit, gen))
                return ChooseOutcome(SimDirectiveStatus.Inactive);

            _inRealm = true;
            _choose = pick;
            ToonPickPhase.ConcludeJoin(toon.CharacterId);
            ToonCreationPhase.ConcludeJoin();
            ambit.Host.ImposeEnteredRealm(pick);
            if (!AmbitHolds(ambit, gen))
                return ChooseOutcome(SimDirectiveStatus.Inactive);

            Console.WriteLine("live: in world - ObjectCreation stream active");
            return ChooseOutcome(
                SimDirectiveStatus.Accepted,
                toon.CharacterId);
        }
        catch (CharacterPickRefusedException rejected)
        {
            if (ToonPickPhase.Snapshot.Error?.RawCode
                != rejected.Error.RawErrorCode)

                ToonPickPhase.ImposeProblem(rejected.Error);
            ToonPickPhase.YieldToPick();
            return ChooseOutcome(
                SimDirectiveStatus.Rejected,
                toon.CharacterId);
        }
        catch (Exception problem)
        {
            _ = HaltFollowing(problem);
            return ChooseOutcome(
                SimDirectiveStatus.Rejected,
                toon.CharacterId);
        }
    }

    private SimDirectiveStatus LatchChoose(
        SimEpochTicket anticipatedGen)
    {
        SimEpochTicket latest = new(_epoch);
        if (anticipatedGen != latest)
            return SimDirectiveStatus.StaleGeneration;
        if (_destroyed || _teardownAsked || _ambit is null || _inRealm)
            return SimDirectiveStatus.Inactive;
        return ToonPickPhase.Snapshot.Lifecycle
            == SimToonPickLifespan.AwaitingSelection
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Inactive;
    }

    private SimDirectiveResult ChooseOutcome(
        SimDirectiveStatus condition,
        uint toonIdent = 0u)
    {
        return new(
            condition,
            new SimEpochTicket(_epoch),
            toonIdent);
    }
}

using System.Net;
using System.Net.Sockets;
using MacAC.Mechanics.Genesis;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionDriver
{
    public OnlineSessionStartResult Start(
        OnlineSessionConnectOptions knobs,
        IOnlineSessionLifespanHarbor hub)
    {
        ArgumentNullException.ThrowIfNull(knobs);
        ArgumentNullException.ThrowIfNull(hub);
        lock (_latch)
        {
            Live();
            if (_depth is not 0)
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            if (_inRealm)
                return Connected();
            if (_ambit is not null)
            {
                return new OnlineSessionStartResult(
                    _ambit.ConnectingKnobs is not null ? OnlineSessionStartStatus.Deferred
                        : OnlineSessionStartStatus.AwaitingCharacterSelection);
            }
            return AtTopTier(() => BeginInstant(knobs, hub, restartHub: true));
        }
    }

    public OnlineSessionStartResult Reconnect(
        OnlineSessionConnectOptions knobs,
        IOnlineSessionLifespanHarbor hub)
    {
        ArgumentNullException.ThrowIfNull(knobs);
        ArgumentNullException.ThrowIfNull(hub);
        lock (_latch)
        {
            Live();
            if (_depth is not 0)
            {
                Queue(new QueuedOp(QueuedKind.Reconnect, knobs, hub));
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            }
            return AtTopTier(() => ReconnectInstant(knobs, hub));
        }
    }

    public void Stop()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            if (_depth is not 0)
            {
                Queue(new QueuedOp(QueuedKind.Stop));
                return;
            }
            AtTopTier(HaltInstant);
        }
    }

    public SimTeardownAck Stop(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            SimEpochTicket latest = new(_epoch);
            if (_destroyed)
            {
                return new SimTeardownAck(
                    anticipatedGen,
                    latest,
                    SimDirectiveStatus.Inactive,
                    _teardownJunctures);
            }
            if (anticipatedGen != latest)
            {
                return new SimTeardownAck(
                    anticipatedGen,
                    latest,
                    SimDirectiveStatus.StaleGeneration,
                    SimTeardownStage.None);
            }

            try
            {
                Stop();
                return new SimTeardownAck(
                    anticipatedGen,
                    new SimEpochTicket(_epoch),
                    SimDirectiveStatus.Accepted,
                    _teardownJunctures);
            }
            catch (Exception problem)
            {
                return new SimTeardownAck(
                    anticipatedGen,
                    new SimEpochTicket(_epoch),
                    SimDirectiveStatus.Rejected,
                    _sunsetting?.FinishedStages ?? _teardownJunctures,
                    problem);
            }
        }
    }

    private OnlineSessionStartResult ReconnectInstant(
        OnlineSessionConnectOptions knobs,
        IOnlineSessionLifespanHarbor hub)
    {
        IOnlineSessionLifespanHarbor? formerHub =
            _ambit?.Host
            ?? _sunsetting?.Host
            ?? _queuedHarborRestart?.Host;
        ulong genPriorHalt = _epoch;
        try
        {
            HaltInstant();
        }
        catch (Exception problem)
        {
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Failed, Error: problem);
        }
        if (_epoch != unchecked(genPriorHalt + 1))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
        return BeginInstant(knobs, hub, restartHub: !ReferenceEquals(formerHub, hub));
    }

    private OnlineSessionStartResult BeginInstant(
        OnlineSessionConnectOptions knobs,
        IOnlineSessionLifespanHarbor hub,
        bool restartHub)
    {
        SimEpochTicket restartGen = new(_epoch);
        ulong gen = ++_epoch;
        SimEpochTicket engagedGen = new(gen);
        ToonPickPhase.Reset(engagedGen);
        ToonCreationPhase.Reset(engagedGen);
        _connect.Reset();
        _createsSinceLineup = 0;
        try
        {
            EmptySunsetting();
            if (_epoch != gen)
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            if (restartHub)
            {
                RestartHarborPriorBegin(
                    hub,
                    gen,
                    restartGen);
            }
            if (_epoch != gen)
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
        }
        catch (Exception problem)
        {
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Failed, Error: problem);
        }

        if (!knobs.Enabled)
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Disabled);
        if (string.IsNullOrEmpty(knobs.User) || string.IsNullOrEmpty(knobs.Password))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.MissingCredentials);

        ToonPickPhase.Begin(engagedGen);
        ToonCreationPhase.Begin(engagedGen);
        _connect.Apply(new(LinkPhase.Connecting));

        Scope? ambit = null;
        try
        {
            IPEndPoint endpoint = _ops.LocateEndpoint(knobs.Host, knobs.Port);
            if (_epoch != gen)
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            Console.WriteLine($"live: connecting to {endpoint} as {knobs.User}");
            var sess = _ops.BuildSess(endpoint);
            ambit = new Scope(
                sess,
                hub,
                new SimEpochTicket(gen));
            _ambit = ambit;
            _inRealm = false;
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

            var mapping = hub.AttachSess(sess);
            ambit.Mapping = mapping;
            ambit.HubAffixed = true;
            if (!ReferenceEquals(mapping.Session, sess))
            {
                throw new InvalidOperationException(
                    "The live-session host returned a binding for a different session");
            }
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

            hub.AnnounceConnecting(knobs.Host, knobs.Port, knobs.User);
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

            sess.ConnectionProgressChanged += _connect.Apply;
            if (knobs.PollConnectionDuringTicks)
            {
                ambit.ConnectingKnobs = knobs;
                _ops.CommenceLink(sess, knobs.User, knobs.Password);
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            }
            try
            {
                _ops.Connect(sess, knobs.User, knobs.Password);
            }
            finally
            {
                sess.ConnectionProgressChanged -= _connect.Apply;
            }
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            return ConcludeBegin(ambit, knobs);
        }
        catch (Exception beginProblem)
        {
            Exception reportedProblem = HaltFollowing(beginProblem);
            _connect.Fail(beginProblem);
            return new OnlineSessionStartResult(
                OnlineSessionStartStatus.Failed,
                Error: reportedProblem);
        }
    }

    private OnlineSessionStartResult ConcludeBegin(Scope ambit, OnlineSessionConnectOptions knobs)
    {
        var sess = ambit.Session;
        var hub = ambit.Host;
        ulong gen = ambit.Generation.Value;
        var mapping = ambit.Mapping!;
        _connect.Apply(new(LinkPhase.Ready));
        ambit.ToonPickMapping = AttachToonPick(
            ambit,
            gen);
        hub.AnnounceConnected();
        if (!AmbitHolds(ambit, gen))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

        var toons = _ops.FetchToons(sess);
        if (toons is not null)
        {
            _createsSinceLineup = 0;
            var lineup = LineupOf(toons);
            ToonPickPhase.ImposeLineup(lineup);
            hub.AnnounceLineup(lineup);
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
        }

        if (AmbitHolds(ambit, gen)
            && _ops.FetchSrvDetails(sess) is { } srvDetails)
            ToonPickPhase.ImposeRealmLabel(srvDetails.WorldName);

        if (knobs.Probe && toons is not null)
        {
            Console.WriteLine(
                "live: probe complete - disconnecting prior to EnterWorld");
            HaltInstant();
            return new OnlineSessionStartResult(OnlineSessionStartStatus.ProbeComplete);
        }

        if (knobs.AwaitCharacterSelection
            && knobs.Character is null
            && toons is not null)
        {
            _ops.BeginToonPickTake(sess);
            if (!AmbitHolds(ambit, gen))
                return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
            Console.WriteLine(
                "live: awaiting character selection prior to EnterWorld");
            return new OnlineSessionStartResult(
                OnlineSessionStartStatus.AwaitingCharacterSelection);
        }

        if (toons is null
            || !TryChooseToon(
                toons,
                knobs.Character,
                out ToonRoster.WireSelection chosen))
        {
            Console.WriteLine("live: no available characters on account; disconnecting");
            HaltInstant();
            return new OnlineSessionStartResult(OnlineSessionStartStatus.NoCharacters);
        }

        OnlineSessionToonPick pick = new OnlineSessionToonPick(
            chosen.ActiveIndex,
            chosen.Character.Id,
            chosen.Character.Name,
            toons.AccountName);
        if (!ToonPickPhase.TryHighlight(pick.CharacterId)
            || !ToonPickPhase.CommenceJoin(out _))
        {
            throw new InvalidOperationException(
                "Runtime character selection rejected the validated active character");
        }
        hub.ImposeChosenToon(pick);
        if (!AmbitHolds(ambit, gen))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

        Console.WriteLine(
            $"live: entering world as 0x{pick.CharacterId:X8} {pick.CharacterName}");
        _ops.EnterWorld(sess, pick.ActiveIndex);
        if (!AmbitHolds(ambit, gen))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

        mapping.EngageDirectives();
        if (!AmbitHolds(ambit, gen))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);
        _inRealm = true;
        _choose = pick;
        ToonPickPhase.ConcludeJoin(pick.CharacterId);
        ToonCreationPhase.ConcludeJoin();
        hub.ImposeEnteredRealm(pick);
        if (!AmbitHolds(ambit, gen))
            return new OnlineSessionStartResult(OnlineSessionStartStatus.Deferred);

        Console.WriteLine("live: in world - ObjectCreation stream active");
        return new OnlineSessionStartResult(
            OnlineSessionStartStatus.Connected,
            pick);
    }

    private Exception HaltFollowing(Exception opProblem)
    {
        try
        {
            HaltInstant();
            return opProblem;
        }
        catch (Exception tidyProblem)
        {
            return new AggregateException(
                "Live-session operation and cleanup both failed",
                opProblem,
                tidyProblem);
        }
    }

    private void HaltInstant()
    {
        _connect.Reset();
        if (_inRealm && _ambit is { } engagedAmbit)
            ExecuteLogoffDrainTap(engagedAmbit.Session);

        ++_epoch;
        _inRealm = false;
        _choose = null;
        ToonPickPhase.Reset(new SimEpochTicket(_epoch));
        ToonCreationPhase.Reset(new SimEpochTicket(_epoch));
        _createsSinceLineup = 0;
        if (_ambit is { } ambit)
        {
            ambit.Session.ConnectionProgressChanged -= _connect.Apply;
            ambit.ConnectingKnobs = null;
            _ambit = null;
            if (_sunsetting is not null && !ReferenceEquals(_sunsetting, ambit))
                throw new InvalidOperationException(
                    "A second live-session scope can't retire prior to the first converges");
            _sunsetting = ambit;
        }
        EmptySunsetting();
        EmptyHarborRestart();
        if (_sunsetting is null && _queuedHarborRestart is null)
            _teardownJunctures = SimTeardownStage.Complete;
    }

    private void ExecuteLogoffDrainTap(RealmSession sess)
    {
        if (_logoffDrainTap is not { } tap)
            return;
        try
        {
            tap(sess);
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine(
                $"live: pre-logoff character-options flush failed: {problem.Message}");
        }
    }

    private void RestartHarborPriorBegin(
        IOnlineSessionLifespanHarbor hub,
        ulong gen,
        SimEpochTicket restartGen)
    {
        bool askedHubAlreadyRestart = false;
        if (_queuedHarborRestart is { } queued)
        {
            queued.Host.RewindSessPhase(queued.Generation);
            _queuedHarborRestart = null;
            askedHubAlreadyRestart =
                ReferenceEquals(queued.Host, hub);
        }
        if (askedHubAlreadyRestart || _epoch != gen)
            return;

        try
        {
            hub.RewindSessPhase(restartGen);
        }
        catch
        {
            _queuedHarborRestart =
                new HarborReset(hub, restartGen);
            throw;
        }
    }

    private OnlineSessionStartResult Connected()
        => new(OnlineSessionStartStatus.Connected, _choose);

    private static OnlineSessionRosterNotice LineupOf(
        ToonRoster.ParsedUnit toons)
    {
        var listings = new OnlineSessionRosterEntry[toons.Characters.Count];
        for (int idx = 0; idx < listings.Length; ++idx)
        {
            var toon = toons.Characters[idx];
            listings[idx] = new OnlineSessionRosterEntry(
                toon.Id,
                toon.Name,
                toon.SecondsGreyedOut);
        }
        return new OnlineSessionRosterNotice(
            toons.AccountName,
            toons.SlotCount,
            listings);
    }

    private static bool TryChooseToon(
        ToonRoster.ParsedUnit toons,
        OnlineSessionToonSelector? selector,
        out ToonRoster.WireSelection pick)
    {
        ArgumentNullException.ThrowIfNull(toons);
        if (selector is null)
        {
            return ToonRoster.TryPickLeadOnHand(
                toons,
                out pick);
        }

        int selectorTally =
            (selector.ActiveIndex.HasValue ? 1 : 0)
            + (selector.CharacterId.HasValue ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(selector.CharacterName) ? 1 : 0);
        if (selectorTally is not 1)
        {
            pick = default;
            return false;
        }

        for (int ordinal = 0; ordinal < toons.Characters.Count; ++ordinal)
        {
            var toon =
                toons.Characters[ordinal];
            if (!ToonRoster.IsOnHandEngagedPersona(toon))
                continue;
            if (selector.ActiveIndex is { } askedOrdinal
                && askedOrdinal != ordinal)

                continue;
            if (selector.CharacterId is { } askedIdent
                && askedIdent != toon.Id)

                continue;
            if (selector.CharacterName is { } askedLabel
                && !string.Equals(
                    askedLabel,
                    toon.Name,
                    StringComparison.OrdinalIgnoreCase))

                continue;

            pick = new ToonRoster.WireSelection(ordinal, toon);
            return true;
        }

        pick = default;
        return false;
    }

    private sealed class LineupGatherer(List<OnlineSessionRosterEntry> listings)
        : ISimToonPickVisitor
    {
        public void Visit(in SimToonPickEntry toon)
        {
            listings.Add(new OnlineSessionRosterEntry(
                toon.CharacterId,
                toon.Name,
                toon.SecondsGreyedOut));
        }
    }
}

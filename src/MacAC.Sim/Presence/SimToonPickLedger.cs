using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class SimToonPickLedger : IDisposable
{
    internal static readonly TimeSpan RevertCorrelationTimeout =
        TimeSpan.FromSeconds(5);

    private sealed class Lens(SimToonPickLedger holder)
        : ISimToonPickLens
    {
        public SimToonPickCapture Snapshot => holder.Snapshot;

        public bool TryFetchAt(
            int readoutOrdinal,
            out SimToonPickEntry toon) =>
            holder.TryAt(readoutOrdinal, out toon);

        public bool TryGet(
            uint toonIdent,
            out SimToonPickEntry toon) =>
            holder.TryGet(toonIdent, out toon);

        public void Call(ISimToonPickVisitor visitor) =>
            holder.Tour(visitor);

        public IDisposable Subscribe(
            ISimToonPickWatcher watcher) =>
            holder._signals.Subscribe(watcher);
    }

    private readonly object _latch = new();

    private readonly SimToonPickEventFlow _signals = new();

    private readonly Lens _lens;

    private readonly TimeProvider _clock;

    private SimToonPickEntry[] _lineup = [];

    private SimEpochTicket _epoch;

    private SimToonPickLifespan _stage;

    private long _rev;

    private string _acct = string.Empty;

    private int _sockets;

    private string _world = string.Empty;

    private uint _highlighted;

    private uint _deleting;

    private uint _restoring;

    private bool _revertLoaded;

    private long _revertLoadedAt;

    private bool _revertAmbiguous;

    private SimToonPickOperation _op;

    private SimToonPickError? _problem;

    private bool _destroyed;

    public SimToonPickLedger(TimeProvider? momentSupplier = null)
    {
        _clock = momentSupplier ?? TimeProvider.System;
        _lens = new Lens(this);
    }

    public ISimToonPickLens View => _lens;

    public SimToonPickCapture Snapshot
    {
        get
        {
            lock (_latch)
            {
                int chosenOrdinal = OrdinalOf(_highlighted);
                var btns =
                    BtnsFor(chosenOrdinal);
                return new SimToonPickCapture(
                    _epoch,
                    _stage,
                    _rev,
                    _acct,
                    _sockets,
                    _lineup.Length,
                    _world,
                    _highlighted,
                    chosenOrdinal,
                    _deleting,
                    _restoring,
                    _op,
                    _problem,
                    btns);
            }
        }
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _stage = SimToonPickLifespan.Inactive;
            Wipe();
            ++_rev;
        }
        _signals.Dispose();
    }

    internal void Begin(SimEpochTicket gen)
    {
        lock (_latch)
        {
            Live();
            _epoch = gen;
            _stage = SimToonPickLifespan.Connecting;
            Wipe();
            ++_rev;
        }
        Publish(SimToonPickDiffKind.Reset);
    }

    internal void ImposeRealmLabel(string realmLabel)
    {
        ArgumentNullException.ThrowIfNull(realmLabel);
        lock (_latch)
        {
            if (_destroyed || _world == realmLabel)
                return;
            _world = realmLabel;
            ++_rev;
        }
        Publish(SimToonPickDiffKind.WorldNameChanged);
    }

    internal bool TryHighlight(uint toonIdent)
    {
        lock (_latch)
        {
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || EraseInFlight()
                || !Contains(toonIdent))

                return false;
            if (_highlighted == toonIdent)
                return true;

            _highlighted = toonIdent;
            _deleting = 0u;
            _problem = null;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.HighlightChanged,
            toonIdent);
        return true;
    }

    internal bool Cancel()
    {
        SimToonPickDiffKind sort;
        uint toonIdent;
        lock (_latch)
        {
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection)

                return false;

            if (_deleting is not 0u)
            {
                toonIdent = _deleting;
                _deleting = 0u;
                sort = SimToonPickDiffKind.DeleteConfirmationCancelled;
            }
            else if (_problem is not null)
            {
                toonIdent = 0u;
                _problem = null;
                sort = SimToonPickDiffKind.ErrorChanged;
            }
            else
            {
                return false;
            }
            ++_rev;
        }
        Publish(sort, toonIdent);
        return true;
    }

    internal void ImposeProblem(CharacterFault.ParsedDef problem)
    {
        if (problem.AsCode == CharacterFault.WireCode.NumErrors)
            return;

        string msg = ProblemPhrase(problem.RawErrorCode, problem.AsCode);
        lock (_latch)
        {
            if (_destroyed
                || _stage is not (
                    SimToonPickLifespan.AwaitingSelection
                    or SimToonPickLifespan.EnteringWorld))
                return;
            _deleting = 0u;
            if (_revertLoaded)
                _revertAmbiguous = true;
            _revertLoaded = false;
            _revertLoadedAt = 0;
            _op = SimToonPickOperation.None;
            _problem = new SimToonPickError(
                problem.RawErrorCode,
                problem.AsCode,
                msg);
            if (_stage == SimToonPickLifespan.EnteringWorld)
                _stage = SimToonPickLifespan.AwaitingSelection;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.ErrorChanged,
            problemCode: problem.RawErrorCode);
    }

    internal bool CommenceJoin(out SimToonPickEntry toon)
    {
        lock (_latch)
        {
            int ordinal = OrdinalOf(_highlighted);
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || EraseInFlight()
                || ordinal < 0
                || !_lineup[ordinal].CanJoin)
            {
                toon = default;
                return false;
            }
            toon = _lineup[ordinal];
            _deleting = 0u;
            _restoring = 0u;
            _revertLoaded = false;
            _revertLoadedAt = 0;
            _revertAmbiguous = false;
            _op = SimToonPickOperation.None;
            _problem = null;
            _stage = SimToonPickLifespan.EnteringWorld;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.EnteringWorld,
            toon.CharacterId);
        return true;
    }

    internal void ConcludeJoin(uint toonIdent)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _stage = SimToonPickLifespan.InWorld;
            _highlighted = toonIdent;
            _op = SimToonPickOperation.None;
            _problem = null;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.EnteredWorld,
            toonIdent);
    }

    internal void YieldToPick()
    {
        lock (_latch)
        {
            if (_destroyed
                || _stage != SimToonPickLifespan.EnteringWorld)

                return;
            _stage = SimToonPickLifespan.AwaitingSelection;
            ++_rev;
        }
    }

    internal void Reset(SimEpochTicket gen)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _epoch = gen;
            _stage = SimToonPickLifespan.Inactive;
            Wipe();
            ++_rev;
        }
        Publish(SimToonPickDiffKind.Reset);
    }

    private bool TryAt(
        int readoutOrdinal,
        out SimToonPickEntry toon)
    {
        lock (_latch)
        {
            if ((uint)readoutOrdinal >= (uint)_lineup.Length)
            {
                toon = default;
                return false;
            }
            toon = _lineup[readoutOrdinal];
            return true;
        }
    }

    private bool TryGet(
        uint toonIdent,
        out SimToonPickEntry toon)
    {
        lock (_latch)
        {
            int ordinal = OrdinalOf(toonIdent);
            if (ordinal < 0)
            {
                toon = default;
                return false;
            }
            toon = _lineup[ordinal];
            return true;
        }
    }

    private void Tour(ISimToonPickVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_latch)
        {
            foreach (SimToonPickEntry toon in _lineup)
                visitor.Visit(in toon);
        }
    }

    private SimToonPickButtons BtnsFor(int chosenOrdinal)
    {
        bool canBuild = _lineup.Length < _sockets;

        if (_op is SimToonPickOperation.DeleteRequested
            or SimToonPickOperation.DeleteAcknowledged)
        {
            return SimToonPickButtons.None with { CanCreate = canBuild };
        }
        if (chosenOrdinal < 0)
            return SimToonPickButtons.None with { CanCreate = canBuild };

        var chosen = _lineup[chosenOrdinal];
        if (chosen.IsQueuedErase)
        {
            return new SimToonPickButtons(
                CanEnter: false,
                CanDelete: false,
                CanRestore: !_revertLoaded,
                DeleteVisible: false,
                RestoreVisible: true,
                CanCreate: canBuild);
        }

        return new SimToonPickButtons(
            CanEnter: chosen.CanJoin,
            CanDelete: chosen.CanJoin,
            CanRestore: false,
            DeleteVisible: true,
            RestoreVisible: false,
            CanCreate: canBuild);
    }

    private int OrdinalOf(uint toonIdent)
    {
        if (toonIdent is 0u)
            return -1;
        for (int idx = 0; idx < _lineup.Length; ++idx)
        {
            if (_lineup[idx].CharacterId == toonIdent)
                return idx;
        }
        return -1;
    }

    private bool Contains(uint toonIdent) =>
        OrdinalOf(toonIdent) >= 0;

    private static string ProblemPhrase(
        uint rawCode,
        CharacterFault.WireCode code)
    {
        return code switch
        {
            CharacterFault.WireCode.Logon =>
                "Another account is already logged on from this client.",
            CharacterFault.WireCode.LoggedOn =>
                "This account is already logged on.",
            CharacterFault.WireCode.AccountLogon =>
                "The server could not access the account. Please try again shortly.",
            CharacterFault.WireCode.ServerCrash or CharacterFault.WireCode.AccountInUse =>
                "The server disconnected. Please try again shortly.",
            CharacterFault.WireCode.Logoff =>
                "The server could not log off the character.",
            CharacterFault.WireCode.Delete =>
                "The server could not delete the character.",
            CharacterFault.WireCode.NoPremade =>
                "No premade character is available.",
            CharacterFault.WireCode.AccountInvalid =>
                "The account name is not valid.",
            CharacterFault.WireCode.AccountDoesntExist =>
                "The account does not exist.",
            CharacterFault.WireCode.EnterGameGeneric =>
                "The character could not enter the world.",
            CharacterFault.WireCode.EnterGameStressAccount =>
                "A stress-test character cannot enter the world.",
            CharacterFault.WireCode.EnterGameCharacterInWorld =>
                "One of this account's characters is still in the world. Please try again shortly.",
            CharacterFault.WireCode.EnterGamePlayerAccountMissing =>
                "The server could not find the player account. Please try again later.",
            CharacterFault.WireCode.EnterGameCharacterNotOwned =>
                "This account does not own the selected character.",
            CharacterFault.WireCode.EnterGameCharacterInWorldServer =>
                "One of this account's characters is already in the world.",
            CharacterFault.WireCode.EnterGameOldCharacter =>
                "The selected character must be updated before entering the world.",
            CharacterFault.WireCode.EnterGameCorruptCharacter =>
                "The selected character's data is corrupt.",
            CharacterFault.WireCode.EnterGameStartServerDown =>
                "The selected character's starting server is unavailable.",
            CharacterFault.WireCode.EnterGameCouldntPlaceCharacter =>
                "The selected character could not be placed in the world. Please try again shortly.",
            CharacterFault.WireCode.LogonServerFull =>
                "The server is currently full. Please try again later.",
            CharacterFault.WireCode.CharacterIsBooted =>
                "The selected character is temporarily unavailable.",
            CharacterFault.WireCode.EnterGameCharacterLocked =>
                "A save of the selected character is still in progress. Please try again later.",
            CharacterFault.WireCode.SubscriptionExpired =>
                "The account subscription has expired.",
            _ => $"Character selection failed (error 0x{rawCode:X8}).",
        };
    }

    private void Wipe()
    {
        _lineup = [];
        _acct = string.Empty;
        _sockets = 0;
        _world = string.Empty;
        _highlighted = 0u;
        _deleting = 0u;
        _restoring = 0u;
        _revertLoaded = false;
        _revertLoadedAt = 0;
        _revertAmbiguous = false;
        _op = SimToonPickOperation.None;
        _problem = null;
    }

    private void Publish(
        SimToonPickDiffKind sort,
        uint toonIdent = 0u,
        uint problemCode = 0u)
    {
        SimEpochTicket gen;
        long rev;
        lock (_latch)
        {
            if (_destroyed)
                return;
            gen = _epoch;
            rev = _rev;
        }
        _signals.Publish(gen, rev, sort, toonIdent, problemCode);
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);
}

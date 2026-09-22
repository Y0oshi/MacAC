using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class SimToonPickLedger
{
    internal void ImposeLineup(OnlineSessionRosterNotice lineup)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        uint chosen;
        lock (_latch)
        {
            Live();
            uint earlier = _highlighted;
            SimToonPickEntry[] wireListings = new SimToonPickEntry[
                lineup.Entries.Count];
            uint backup = 0u;
            bool locatedOnHandBackup = false;
            for (int idx = 0; idx < wireListings.Length; ++idx)
            {
                var src = lineup.Entries[idx];
                wireListings[idx] = new SimToonPickEntry(
                    idx,
                    src.Id,
                    src.Name,
                    src.SecondsGreyedOut);
                if (backup is 0u && src.Id is not 0u)
                    backup = src.Id;
                if (!locatedOnHandBackup
                    && src.Id is not 0u
                    && src.SecondsGreyedOut is 0u)
                {
                    backup = src.Id;
                    locatedOnHandBackup = true;
                }
            }

            Array.Sort(
                wireListings,
                static (left, right) =>
                    string.CompareOrdinal(left.Name, right.Name));
            _lineup = GreyedToRear(wireListings);
            _acct = lineup.AccountName;
            _sockets = lineup.SlotCount;
            _stage = SimToonPickLifespan.AwaitingSelection;
            _deleting = 0u;
            _restoring = 0u;
            _revertLoaded = false;
            _revertLoadedAt = 0;
            _revertAmbiguous = false;
            _op = SimToonPickOperation.None;
            _problem = null;
            _highlighted = Contains(earlier)
                ? earlier
                : backup;
            chosen = _highlighted;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.RosterChanged,
            chosen);
    }

    internal void AffixBuiltToon(uint toonIdent, string label, int wireOrdinal)
    {
        ArgumentNullException.ThrowIfNull(label);
        lock (_latch)
        {
            Live();
            SimToonPickEntry[] appended = new SimToonPickEntry[_lineup.Length + 1];
            Array.Copy(_lineup, appended, _lineup.Length);
            appended[^1] = new SimToonPickEntry(
                wireOrdinal,
                toonIdent,
                label,
                SecondsGreyedOut: 0u);

            Array.Sort(
                appended,
                static (left, right) =>
                    string.CompareOrdinal(left.Name, right.Name));
            _lineup = GreyedToRear(appended);
            ++_rev;
        }
        Publish(SimToonPickDiffKind.RosterChanged, toonIdent);
    }

    internal bool TryReqErase(out uint toonIdent)
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
                toonIdent = 0u;
                return false;
            }
            toonIdent = _lineup[ordinal].CharacterId;
            _deleting = toonIdent;
            _problem = null;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.DeleteConfirmationOpened,
            toonIdent);
        return true;
    }

    internal bool TryGrabEraseAck(
        out SimToonPickEntry toon,
        out string acctLabel)
    {
        lock (_latch)
        {
            int ordinal = OrdinalOf(_deleting);
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || _deleting is 0u
                || _op is SimToonPickOperation.DeleteRequested
                    or SimToonPickOperation.DeleteAcknowledged
                || ordinal < 0
                || !_lineup[ordinal].CanJoin)
            {
                toon = default;
                acctLabel = string.Empty;
                return false;
            }

            toon = _lineup[ordinal];
            acctLabel = _acct;
            _deleting = 0u;
            _op = SimToonPickOperation.DeleteRequested;
            _problem = null;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.DeleteRequested,
            toon.CharacterId);
        return true;
    }

    internal bool TryCommenceRevert(
        out SimToonPickEntry toon)
    {
        lock (_latch)
        {
            int ordinal = OrdinalOf(_highlighted);
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || EraseInFlight()
                || _revertLoaded
                || ordinal < 0
                || !_lineup[ordinal].IsQueuedErase)
            {
                toon = default;
                return false;
            }

            toon = _lineup[ordinal];
            _deleting = 0u;
            _restoring = toon.CharacterId;
            _revertLoaded = true;
            _revertLoadedAt =
                _clock.GetTimestamp();
            _op = SimToonPickOperation.RestoreRequested;
            _problem = null;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.RestoreRequested,
            toon.CharacterId);
        return true;
    }

    internal bool SweepRevertCorrelation()
    {
        uint toonIdent;
        lock (_latch)
        {
            if (_destroyed
                || !_revertLoaded
                || _clock.GetElapsedTime(
                    _revertLoadedAt)
                    < RevertCorrelationTimeout)

                return false;

            toonIdent = _restoring;
            _revertLoaded = false;
            _revertLoadedAt = 0;
            _revertAmbiguous = true;
            if (!EraseConfirmed())
                _op = SimToonPickOperation.None;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.RestoreCorrelationExpired,
            toonIdent);
        return true;
    }

    internal void ImposeEraseAcknowledged()
    {
        uint toonIdent;
        lock (_latch)
        {
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || _op != SimToonPickOperation.DeleteRequested)

                return;
            _op = SimToonPickOperation.DeleteAcknowledged;
            toonIdent = _highlighted;
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.DeleteAcknowledged,
            toonIdent);
    }

    internal void ImposeRevert(CharacterRevive.Parsed response)
    {
        uint toonIdent;
        uint problemCode = 0u;
        bool eraseInFlight;
        lock (_latch)
        {
            if (_destroyed
                || _stage != SimToonPickLifespan.AwaitingSelection
                || !_revertLoaded)
                return;
            if (response.Guid is null
                && _revertAmbiguous)

                return;
            if (response.Guid is { } responseOid
                && responseOid != _restoring)

                return;
            _revertLoaded = false;
            _revertLoadedAt = 0;
            toonIdent = response.Guid
                ?? _restoring;
            eraseInFlight = EraseConfirmed();

            if (response.IsOk
                && response.Guid is { } oid
                && response.SecondsGreyedOut is { } secs)
            {
                int ordinal = OrdinalOf(oid);
                if (ordinal >= 0)
                {
                    var latest = _lineup[ordinal];
                    _lineup[ordinal] = latest with
                    {
                        Name = response.Name ?? latest.Name,
                        SecondsGreyedOut = secs,
                    };
                    Array.Sort(
                        _lineup,
                        static (left, right) =>
                            string.CompareOrdinal(left.Name, right.Name));
                    _lineup = GreyedToRear(_lineup);
                }
                if (!eraseInFlight)
                {
                    _op =
                        SimToonPickOperation.RestoreSucceeded;
                }
                _problem = null;
            }
            else
            {
                problemCode = response.VerificationFlag;
                if (!eraseInFlight)
                {
                    _op =
                        SimToonPickOperation.RestoreRejected;
                }
                _problem = new SimToonPickError(
                    response.VerificationFlag,
                    CharacterFault.WireCode.Undefined,
                    $"The character could not be restored (verification 0x{response.VerificationFlag:X8}).");
            }
            ++_rev;
        }
        Publish(
            SimToonPickDiffKind.RestoreCompleted,
            toonIdent,
            problemCode);
    }

    private bool EraseInFlight()
    {
        return _deleting is not 0u
        || EraseConfirmed();
    }

    private bool EraseConfirmed()
    {
        return _op is SimToonPickOperation.DeleteRequested
            or SimToonPickOperation.DeleteAcknowledged;
    }

    private static SimToonPickEntry[] GreyedToRear(
        SimToonPickEntry[] sorted)
    {
        if (sorted.Length < 2)
            return sorted;
        SimToonPickEntry[] outcome = new SimToonPickEntry[sorted.Length];
        int locus = 0;
        foreach (SimToonPickEntry listing in sorted)
        {
            if (!listing.IsQueuedErase)
                outcome[locus++] = listing;
        }
        foreach (SimToonPickEntry listing in sorted)
        {
            if (listing.IsQueuedErase)
                outcome[locus++] = listing;
        }
        return outcome;
    }
}

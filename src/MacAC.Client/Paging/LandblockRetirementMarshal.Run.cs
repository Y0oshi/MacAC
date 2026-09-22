using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed partial class LandblockSunsetTicket
{
    public bool RunOnce(LandblockSunsetJuncture juncture, Action op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return true;

        try
        {
            op();
            FinishedJunctures |= juncture;
            _misses.Remove(juncture);
            return true;
        }
        catch (Exception problem)
        {
            _misses[juncture] = problem;
            return false;
        }
    }

    public bool RunOnce(LandblockSunsetJuncture juncture, Func<bool> op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return true;

        try
        {
            if (!op())
                return false;
            FinishedJunctures |= juncture;
            _misses.Remove(juncture);
            return true;
        }
        catch (Exception problem)
        {
            _misses[juncture] = problem;
            return false;
        }
    }

    public bool ExecuteForEachActor(
        LandblockSunsetJuncture juncture,
        Func<RealmActor, bool> predicate,
        Action<RealmActor> op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return true;

        _actorCursors.TryGetValue(juncture, out int cur);
        while (cur < Entities.Count)
        {
            RealmActor actor = Entities[cur];
            if (predicate(actor))
            {
                try
                {
                    op(actor);
                }
                catch (Exception problem)
                {
                    _actorCursors[juncture] = cur;
                    _misses[juncture] = problem;
                    return false;
                }
            }

            ++cur;
            _actorCursors[juncture] = cur;
        }

        FinishedJunctures |= juncture;
        _actorCursors.Remove(juncture);
        _misses.Remove(juncture);
        return true;
    }

    internal LandblockSunsetOpOutcome ExecuteOnceHop(
        LandblockSunsetJuncture juncture,
        Action op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return LandblockSunsetOpOutcome.NoWork;

        try
        {
            op();
            FinishedJunctures |= juncture;
            _misses.Remove(juncture);
            return LandblockSunsetOpOutcome.Progressed;
        }
        catch (Exception problem)
        {
            _misses[juncture] = problem;
            return LandblockSunsetOpOutcome.Failed;
        }
    }

    internal LandblockSunsetOpOutcome ExecuteOnceHop(
        LandblockSunsetJuncture juncture,
        Func<bool> op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return LandblockSunsetOpOutcome.NoWork;

        try
        {
            if (!op())
                return LandblockSunsetOpOutcome.Pending;

            FinishedJunctures |= juncture;
            _misses.Remove(juncture);
            return LandblockSunsetOpOutcome.Progressed;
        }
        catch (Exception problem)
        {
            _misses[juncture] = problem;
            return LandblockSunsetOpOutcome.Failed;
        }
    }

    internal LandblockSunsetOpOutcome ExecuteActorHop(
        LandblockSunsetJuncture juncture,
        Func<RealmActor, bool> predicate,
        Action<RealmActor> op)
    {
        VetSingleJuncture(juncture);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(op);
        if ((FinishedJunctures & juncture) != 0)
            return LandblockSunsetOpOutcome.NoWork;

        _actorCursors.TryGetValue(juncture, out int cur);
        if (cur >= Entities.Count)
        {
            FinishedJunctures |= juncture;
            _actorCursors.Remove(juncture);
            _misses.Remove(juncture);
            return LandblockSunsetOpOutcome.Progressed;
        }

        RealmActor actor = Entities[cur];
        if (predicate(actor))
        {
            try
            {
                op(actor);
            }
            catch (Exception problem)
            {
                _actorCursors[juncture] = cur;
                _misses[juncture] = problem;
                return LandblockSunsetOpOutcome.Failed;
            }
        }

        ++cur;
        if (cur == Entities.Count)
        {
            FinishedJunctures |= juncture;
            _actorCursors.Remove(juncture);
            _misses.Remove(juncture);
        }
        else
        {
            _actorCursors[juncture] = cur;
        }

        return LandblockSunsetOpOutcome.Progressed;
    }
}

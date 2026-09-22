using MacAC.Mechanics.Fighting;

namespace MacAC.Wire;

/// <summary>Feeds the player's combat-mode property updates into <see cref="FightingPhase"/>.</summary>
public static class CombatStateBindings
{
    public const uint FightingMannerPropIdent = 40u;

    public static IDisposable Wire(RealmSession sess, FightingPhase fighting, Func<bool>? accepting = null)
    {
        ArgumentNullException.ThrowIfNull(sess);
        ArgumentNullException.ThrowIfNull(fighting);

        void OnRefresh(RealmSession.PlayerIntNotice notice)
        {
            if (accepting?.Invoke() == false)
                return;
            ImposeAvatarIntProp(fighting, notice.Property, notice.Value);
        }

        sess.PlayerIntPropertyUpdated += OnRefresh;
        return new Unhook(() => sess.PlayerIntPropertyUpdated -= OnRefresh);
    }

    /// <summary>True when the property was the combat mode and carried a mode the client models.</summary>
    public static bool ImposeAvatarIntProp(FightingPhase fighting, uint prop, int val)
    {
        ArgumentNullException.ThrowIfNull(fighting);
        if (prop != FightingMannerPropIdent)
            return false;

        FightingManner manner = (FightingManner)val;
        if (manner is not (FightingManner.NonCombat or FightingManner.Melee or FightingManner.Missile or FightingManner.Magic))
            return false;

        fighting.ApplyFightingManner(manner);
        return true;
    }

    // Runs its action exactly once, on the first Dispose
    private sealed class Unhook(Action unfasten) : IDisposable
    {
        private Action? _unfasten = unfasten;

        public void Dispose() => Interlocked.Exchange(ref _unfasten, null)?.Invoke();
    }
}

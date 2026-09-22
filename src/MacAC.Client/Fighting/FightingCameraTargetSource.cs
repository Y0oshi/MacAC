using System.Numerics;
using MacAC.Client.Dealing;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Fighting;

internal interface IFightingCameraTargetSource
{
    Vector3? FetchFollowedMarkPt();
}

internal sealed class FightingCameraTargetSource(
    IFightingGameplayPreferencesSource settings,
    FightingPhase combat,
    PickPhase selection,
    IRealmPickingProbe world) : IFightingCameraTargetSource
{
    private readonly IFightingGameplayPreferencesSource _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly FightingPhase _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));
    private readonly IRealmPickingProbe _realm = world ?? throw new ArgumentNullException(nameof(world));

    public Vector3? FetchFollowedMarkPt()
    {
        return _prefs.ViewCombatTarget
        && FightInputPlanner.SupportsTargetedAssault(_fighting.LatestMode)
        && _pick.ChosenObjectTag is uint chosen
            ? _realm.GetCombatCameraTargetPoint(chosen)
            : null;
    }
}

using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Fighting;

namespace MacAC.Cockpit.Panels.Vitals;

public sealed class VitalsModel(FightingPhase combat, SelfState? ownAvatar = null)
{
    private readonly FightingPhase _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
    private uint _avatarOid;

    public void AssignOwnAvatarOid(uint oid) => _avatarOid = oid;

    public float HealthPercent => ownAvatar?.HealthPercent ?? _fighting.FetchHealthPct(_avatarOid);

    public float? StaminaPct => ownAvatar?.StaminaPercent;

    public float? ManaPercent => ownAvatar?.ManaPercent;

    public uint? HealthCurrent => Current(SelfState.VitalSort.Health);

    public uint? HealthMax => Max(SelfState.VitalSort.Health);

    public uint? StaminaCurrent => Current(SelfState.VitalSort.Stamina);

    /// <summary>Includes buffs and vitae.</summary>
    public uint? StaminaMax => Max(SelfState.VitalSort.Stamina);

    public uint? ManaCurrent => Current(SelfState.VitalSort.Mana);

    /// <summary>Includes buffs and vitae.</summary>
    public uint? ManaMax => Max(SelfState.VitalSort.Mana);

    private uint? Current(SelfState.VitalSort vital) => ownAvatar?.Get(vital)?.Current;

    private uint? Max(SelfState.VitalSort vital) => ownAvatar?.FetchUpperApprox(vital);
}

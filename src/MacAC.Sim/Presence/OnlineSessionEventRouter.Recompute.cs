using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Traits;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionEventRouter
{
    private static void PushTravelAptitudeSums(OnlineToonSessionWiring toon)
    {
        (int exec, int leap) = toon.Character.LocalPlayer.TravelAptitudeSums();
        if (exec < 0 && leap < 0)
            return;
        toon.Character.RefreshTravelAptitudeBase(exec, leap);
        toon.OnSkillsUpdated?.Invoke(exec, leap);
        toon.OnMovementStatsUpdated?.Invoke();
    }

    private static void RecomputeBurden(OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, bool alert = true)
    {
        uint self = satchel.PlayerGuid();
        var me = satchel.Objects.Get(self);
        int strength = toon.Character.LocalPlayer.FetchNetAttr(SelfState.StatKind.Strength) ?? 0;
        RecomputeBurdenRest(self, satchel, me, strength, toon, alert);
    }

    private static void RecomputeBurdenRest(uint self, OnlineStashSessionWiring satchel, ClientThing? me, int strength, OnlineToonSessionWiring toon, bool alert)
    {
        int aug = me?.Properties.FetchInt((uint)TraitInt.AugmentationIncreasedCarryingCapacity) ?? 0;
        int cap = BurdenSystem.EncumbranceCapacity(strength, aug);
        RecomputeBurdenTail(self, satchel, me, toon, cap, alert);
    }

    private static void RecomputeBurdenTail(uint self, OnlineStashSessionWiring satchel, ClientThing? me, OnlineToonSessionWiring toon, int cap, bool alert)
    {
        int burden = me is not null && me.Properties.Ints.TryGetValue((uint)TraitInt.EncumbranceVal, out int wireBurden)
                ? wireBurden
                : satchel.Objects.TotalCarriedBurden(self);
        toon.Character.TravelAptitudes.RefreshBurden(BurdenSystem.Load(cap, burden));
        if (alert)
            toon.OnMovementStatsUpdated?.Invoke();
    }

    private static void RecomputeAvatarQualities(OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon)
    {
        RecomputeBurden(satchel, toon, alert: false);
        RecomputePvpCondition(satchel, toon, alert: false);

        TraitBundle traits = satchel.Objects.Get(satchel.PlayerGuid())?.Properties ?? toon.Character.LocalPlayer.Properties;
        toon.Character.RefreshTravelAptitudeAugmentations(SkillRules.AugBonuses.FromProps(traits));
        toon.OnMovementStatsUpdated?.Invoke();
    }

    private static void RecomputePvpCondition(OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, bool alert = true)
    {
        var me = satchel.Objects.Get(satchel.PlayerGuid());
        uint bitset = me?.PublicWeenieBitfield ?? 0u;
        int pkCondition = me?.Properties.Ints.TryGetValue((uint)TraitInt.PlayerKillerStatus, out int wirePk) == true ? wirePk : -1;
        float? previousPkAssault = me?.Properties.Floats.TryGetValue((uint)TraitFloat.LastPkAttackTimestamp, out double at) == true ? (float)at : null;
        RecomputePvpConditionRest(bitset, pkCondition, previousPkAssault, toon, alert);
    }

    private static void RecomputePvpConditionRest(uint bitset, int pkCondition, float? previousPkAssault, OnlineToonSessionWiring toon, bool alert)
    {
        toon.Character.TravelAptitudes.RefreshOwnPwdBitfield(bitset);
        toon.Character.TravelAptitudes.RefreshAvatarKillerCondition(pkCondition, previousPkAssault);
        if (alert)
            toon.OnMovementStatsUpdated?.Invoke();
    }

    private static void RecomputeStamina(SelfState.VitalSort sort, OnlineToonSessionWiring toon)
    {
        if (sort != SelfState.VitalSort.Stamina)
            return;
        if (toon.Character.LocalPlayer.Get(SelfState.VitalSort.Stamina) is not SelfState.VitalFrame stamina)
            return;
        toon.Character.TravelAptitudes.RefreshStamina((int)stamina.Current);
        toon.OnMovementStatsUpdated?.Invoke();
    }

    private static void CourseTurbineComms(ChatTranscript comms, TurbineComms.Parsed decoded)
    {
        switch (decoded.Body)
        {
            case TurbineComms.Cargo.RoomSend msg:
                comms.OnLaneAir(
                    msg.RoomId,
                    msg.SenderName,
                    msg.Message,
                    tracePhraseKind: TurbineCommsLabels.TraceWordingKind(msg.ChatType),
                    laneLabel: TurbineCommsLabels.Resolve(msg.RoomId, msg.ChatType));
                return;
            case TurbineComms.Cargo.Reply { HResult: not 0 } response:
                comms.OnSysMsg($"TurbineComms send rejected (hresult=0x{unchecked((uint)response.HResult):X8}).", (uint)CanonLogTextType.Default);
                return;
            default:
                // Response with HResult==0, or Unknown - nothing to surface
                return;
        }
    }
}

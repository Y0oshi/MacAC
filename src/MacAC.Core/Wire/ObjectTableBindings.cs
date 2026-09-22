using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Avatar;

namespace MacAC.Wire;

public static class ObjectTableBindings
{
    public static IDisposable Wire(RealmSession sess, ClientThingChart chart, Func<uint>? avatarOid = null, SelfState? ownAvatar = null, Func<bool>? accepting = null)
    {
        ArgumentNullException.ThrowIfNull(sess);
        ArgumentNullException.ThrowIfNull(chart);
        SubscriptionLedger register = new SubscriptionLedger();
        bool Open() => accepting?.Invoke() != false;

        // Every int trait on a visible object is applied - the server owns object state.
        void OnObjectInt(RealmSession.ObjectIntNotice num)
        {
            if (Open())
                chart.RefreshIntProp(num.Guid, num.Property, num.Value);
        }
        void OnAvatarInt(RealmSession.PlayerIntNotice num)
        {
            if (Open() && avatarOid is not null)
                chart.RefreshIntProp(avatarOid(), num.Property, num.Value);
        }
        void OnAvatarInt64(RealmSession.PlayerInt64Notice num)
        {
            if (Open())
                ImposeAvatarInt64PropRefresh(chart, ownAvatar, avatarOid?.Invoke() ?? 0u, num);
        }
        void OnPile(RealmSession.StackSizeNotice num)
        {
            if (Open())
                chart.RefreshPileDims(num.Guid, num.StackSize, num.Value);
        }
        void OnDropped(uint oid)
        {
            if (Open())
                chart.Remove(oid);
        }

        sess.ObjectIntPropertyUpdated += OnObjectInt;
        register.Add(() => sess.ObjectIntPropertyUpdated -= OnObjectInt);
        sess.PlayerIntPropertyUpdated += OnAvatarInt;
        register.Add(() => sess.PlayerIntPropertyUpdated -= OnAvatarInt);
        sess.PlayerInt64PropertyUpdated += OnAvatarInt64;
        register.Add(() => sess.PlayerInt64PropertyUpdated -= OnAvatarInt64);
        sess.StackSizeUpdated += OnPile;
        register.Add(() => sess.StackSizeUpdated -= OnPile);
        sess.InventoryObjectRemoved += OnDropped;
        register.Add(() => sess.InventoryObjectRemoved -= OnDropped);
        return register;
    }

    public static bool ImposeActorSummon(ClientThingChart chart, RealmSession.MoverSpawn summon, bool replaceGen = false, Func<bool>? accepting = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (accepting?.Invoke() == false)
            return false;

        var capture = ToWeenieBlob(summon);
        if (replaceGen)
            return chart.ReplaceGen(capture, summon.InstanceSequence, accepting) is not null;

        chart.Ingest(capture);
        return accepting?.Invoke() != false;
    }

    public static void ImposeActorErase(ClientThingChart chart, Messages.ObjectDeletion.Parsed erase) =>
        chart.DropLogicalGen(erase.Guid, erase.InstanceSequence);

    public static WeenieRecord ToWeenieBlob(RealmSession.MoverSpawn spawn)
    {
        return new(
        Guid: spawn.Guid,
        Name: spawn.Name,
        Type: spawn.ItemType is { } sort ? (GearKind)sort : null,
        WeenieClassId: spawn.WeenieClassId,
        IconId: spawn.IconId,
        IconOverlayId: spawn.IconOverlayId,
        IconUnderlayId: spawn.IconUnderlayId,
        Effects: spawn.UiEffects,
        Value: spawn.Value,
        StackSize: spawn.StackSize,
        StackSizeMax: spawn.StackSizeMax,
        Burden: spawn.Burden,
        ContainerId: spawn.ContainerId,
        WielderId: spawn.WielderId,
        ValidLocations: spawn.ValidLocations,
        CurrentWieldedLocation: spawn.CurrentWieldedLocation,
        Priority: spawn.Priority,
        ItemsCapacity: spawn.ItemsCapacity,
        ContainersCapacity: spawn.ContainersCapacity,
        HookItemTypes: spawn.HookItemTypes,
        HookType: spawn.HookType,
        Structure: spawn.Structure,
        MaxStructure: spawn.MaxStructure,
        Workmanship: spawn.Workmanship,
        Useability: spawn.Useability,
        TargetType: spawn.TargetType,
        RadarBlipColor: spawn.RadarBlipColor,
        RadarBehavior: spawn.RadarBehavior,
        PublicWeenieBitfield: spawn.ObjectDescriptionFlags,
        CombatUse: spawn.CombatUse,
        PluralName: spawn.PluralName,
        PetOwnerId: spawn.PetOwnerId,
        AmmoType: spawn.AmmoType,
        SpellId: spawn.SpellId,
        CooldownId: spawn.CooldownId,
        CooldownDuration: spawn.CooldownDuration,
        MaterialType: spawn.MaterialType,
        HouseOwnerId: spawn.HouseOwnerId,
        MonarchId: spawn.MonarchId,
        Restrictions: spawn.Restrictions);
    }

    internal static void ImposeAvatarInt64PropRefresh(ClientThingChart chart, SelfState? ownAvatar, uint avatarOid, RealmSession.PlayerInt64Notice refresh)
    {
        if (avatarOid is not 0u)
            chart.RefreshInt64Prop(avatarOid, refresh.Property, refresh.Value);
        ownAvatar?.OnInt64PropRefresh(refresh.Property, refresh.Value);
    }
}

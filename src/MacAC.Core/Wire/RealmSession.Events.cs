using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>The typed notifications a session raises as server messages are decoded.</summary>
public sealed partial class RealmSession
{
    public readonly record struct MoverSpawn(
        uint Guid,
        ObjectCreation.RemotePosition? Position,
        uint? SetupTableId,
        IReadOnlyList<ObjectCreation.AnimPartSwap> AnimPartChanges,
        IReadOnlyList<ObjectCreation.TextureSwap> TextureChanges,
        IReadOnlyList<ObjectCreation.PaletteSwap> SubPalettes,
        uint? BasePaletteId,
        float? ObjScale,
        string? Name,
        uint? ItemType,
        ObjectCreation.RemoteMotionState? MotionState,
        uint? MotionTableId,
        uint? PhysicsState = null,
        uint? ObjectDescriptionFlags = null,
        float? Friction = null,
        float? Elasticity = null,
        uint? Useability = null,
        float? UseRadius = null,
        uint? TargetType = null,
        uint IconId = 0,
        uint IconOverlayId = 0,
        uint IconUnderlayId = 0,
        uint UiEffects = 0,
        uint WeenieClassId = 0,
        int? Value = null,
        int? StackSize = null,
        int? StackSizeMax = null,
        int? Burden = null,
        int? ItemsCapacity = null,
        int? ContainersCapacity = null,
        uint? ContainerId = null,
        uint? WielderId = null,
        uint? ValidLocations = null,
        uint? CurrentWieldedLocation = null,
        uint? Priority = null,
        int? Structure = null,
        int? MaxStructure = null,
        float? Workmanship = null,
        ushort InstanceSequence = 0,
        ushort MovementSequence = 0,
        ushort ServerControlSequence = 0,
        ushort PositionSequence = 0,
        uint? ParentGuid = null,
        uint? ParentLocation = null,
        uint? PlacementId = null,
        byte? RadarBlipColor = null,
        byte? RadarBehavior = null,
        byte? CombatUse = null,
        string? PluralName = null,
        uint? PetOwnerId = null,
        ushort? AmmoType = null,
        uint? SpellId = null,
        uint? CooldownId = null,
        double? CooldownDuration = null,
        KineticSpawnData? Physics = null,
        uint? HookItemTypes = null,
        uint? HookType = null,
        uint? MaterialType = null,
        uint? HouseOwnerId = null,
        uint? MonarchId = null,
        HouseAccessRecord? Restrictions = null);

    internal static MoverSpawn ToActorSummon(ObjectCreation.Parsed decoded)
    {
        return new(
        decoded.Guid,
        decoded.Position,
        decoded.SetupTableId,
        decoded.AnimPartChanges,
        decoded.TextureChanges,
        decoded.SubPalettes,
        decoded.BasePaletteId,
        decoded.ObjScale,
        decoded.Name,
        decoded.ItemType,
        decoded.MotionState,
        decoded.MotionTableId,
        decoded.PhysicsState,
        decoded.ObjectDescriptionFlags,
        decoded.Friction,
        decoded.Elasticity,
        decoded.Useability,
        decoded.UseRadius,
        decoded.TargetType,
        decoded.IconId,
        decoded.IconOverlayId,
        decoded.IconUnderlayId,
        decoded.UiEffects,
        decoded.WeenieClassId,
        decoded.Value,
        decoded.StackSize,
        decoded.StackSizeMax,
        decoded.Burden,
        decoded.ItemsCapacity,
        decoded.ContainersCapacity,
        decoded.ContainerId,
        decoded.WielderId,
        decoded.ValidLocations,
        decoded.CurrentWieldedLocation,
        decoded.Priority,
        decoded.Structure,
        decoded.MaxStructure,
        decoded.Workmanship,
        InstanceSequence: decoded.InstanceSequence,
        MovementSequence: decoded.MovementSequence,
        ServerControlSequence: decoded.ServerControlSequence,
        PositionSequence: decoded.PositionSequence,
        ParentGuid: decoded.ParentGuid,
        ParentLocation: decoded.ParentLocation,
        PlacementId: decoded.PlacementId,
        RadarBlipColor: decoded.RadarBlipColor,
        RadarBehavior: decoded.RadarBehavior,
        CombatUse: decoded.CombatUse,
        PluralName: decoded.PluralName,
        PetOwnerId: decoded.PetOwnerId,
        AmmoType: decoded.AmmoType,
        SpellId: decoded.SpellId,
        CooldownId: decoded.CooldownId,
        CooldownDuration: decoded.CooldownDuration,
        Physics: decoded.Physics,
        HookItemTypes: decoded.HookItemTypes,
        HookType: decoded.HookType,
        MaterialType: decoded.MaterialType,
        HouseOwnerId: decoded.HouseOwnerId,
        MonarchId: decoded.MonarchId,
        Restrictions: decoded.Restrictions);
    }

    public event Action<MoverSpawn>? EntitySpawned;
    public event Action<ObjectDeletion.Parsed>? EntityDeleted;
    public event Action<PickupNotice.Parsed>? EntityPickedUp;

    public readonly record struct MoverMotionUpdate(
        uint Guid,
        ObjectCreation.RemoteMotionState MotionState,
        ushort InstanceSequence,
        ushort MovementSequence,
        ushort ServerControlSequence,
        bool IsAutonomous);

    public event Action<MoverMotionUpdate>? MotionUpdated;

    public readonly record struct MoverPositionUpdate(
        uint Guid,
        ObjectCreation.RemotePosition Position,
        System.Numerics.Vector3? Velocity,
        uint? PlacementId,
        bool IsGrounded,
        ushort InstanceSequence,
        ushort PositionSequence,
        ushort TeleportSequence,
        ushort ForcePositionSequence);

    /// <summary>A 0xF748 position update was decoded.</summary>
    public event Action<MoverPositionUpdate>? PositionUpdated;

    public event Action<VelocityUpdate.Parsed>? VectorUpdated;
    public event Action<AncestorSignal.Parsed>? ParentUpdated;
    public event Action<GroupPhase.Parsed>? StateUpdated;

    public readonly record struct ObjectIntNotice(uint Guid, uint Property, int Value);
    public event Action<ObjectIntNotice>? ObjectIntPropertyUpdated;

    /// <summary>One int trait changed on the player (0x02CD); EncumbranceVal (5) drives the burden bar.</summary>
    public readonly record struct PlayerIntNotice(uint Property, int Value);
    public event Action<PlayerIntNotice>? PlayerIntPropertyUpdated;

    public readonly record struct PlayerInt64Notice(uint Property, long Value);
    public event Action<PlayerInt64Notice>? PlayerInt64PropertyUpdated;

    /// <summary>A 0x0197 stack-size change was decoded.</summary>
    public readonly record struct StackSizeNotice(uint Guid, int StackSize, int Value);
    public event Action<StackSizeNotice>? StackSizeUpdated;

    /// <summary>A guid left the player's inventory view (0x0024).</summary>
    public event Action<uint>? InventoryObjectRemoved;

    public event Action<uint>? TeleportStarted;
    public event Action<ObjDescNotice.Parsed>? AppearanceUpdated;
    public event Action<SpeechLine.Parsed>? SpeechHeard;
    public event Action<EmoteLine.Parsed>? EmoteHeard;
    public event Action<WireSoulEmote.Parsed>? SoulEmoteHeard;
    public event Action<ServerLine.Parsed>? ServerMessageReceived;
    public event Action<PlayerDeath.Parsed>? PlayerKilledReceived;
    public event Action<TurbineComms.Parsed>? TurbineChatReceived;
    public event Action<GroupTurbineCommsLanes.Parsed>? TurbineChannelsReceived;
    public event Action<OwnVitalUpdate.DecodedWhole>? VitalUpdated;
    public event Action<OwnVitalUpdate.DecodedLatest>? VitalCurrentUpdated;
    public event Action<OwnAttributeUpdate.Parsed>? AttributeUpdated;
    public event Action<OwnSkillUpdate.Parsed>? SkillUpdated;
    public event Action<PlayKineticsProgram>? PlayPhysicsScriptReceived;
    public event Action<PlayKineticsProgramKind>? PlayPhysicsScriptTypeReceived;
    public event Action<SfxSignal>? SoundEventReceived;

    /// <summary>Carries the environ-change type from an AdminEnvirons push.</summary>
    public event Action<uint>? EnvironChanged;

    public event Action<ToonRoster.ParsedUnit>? CharacterListReceived;
    public event Action? CharacterDeleteAcknowledged;
    public event Action<CharacterRevive.Parsed>? CharacterRestoreReceived;
    public event Action<GenesisVerdict.Parsed>? CharacterCreateResponseReceived;
    public event Action<CharacterFault.ParsedDef>? CharacterErrorReceived;
    public event Action<RealmName.Parsed>? ServerNameReceived;
}

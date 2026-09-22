using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public static partial class ObjectCreation
{
    public const uint Opcode = 0xF745u;

    /// <summary>DAT id type prefixes that the wire strips from packed ids.</summary>
    public const uint GfxObjRefKindStem = 0x01000000u;
    public const uint SwatchKindStem = 0x04000000u;
    public const uint CanvasTextureKindStem = 0x05000000u;
    public const uint GlyphKindStem = 0x06000000u;

    public readonly record struct Parsed(
        uint Guid,
        RemotePosition? Position,
        uint? SetupTableId,
        IReadOnlyList<AnimPartSwap> AnimPartChanges,
        IReadOnlyList<TextureSwap> TextureChanges,
        IReadOnlyList<PaletteSwap> SubPalettes,
        uint? BasePaletteId,
        float? ObjScale,
        string? Name,
        uint? ItemType,
        RemoteMotionState? MotionState,
        uint? MotionTableId,
        ushort InstanceSequence = 0,
        ushort TeleportSequence = 0,
        ushort ServerControlSequence = 0,
        ushort ForcePositionSequence = 0,
        ushort MovementSequence = 0,
        ushort PositionSequence = 0,
        uint? ParentGuid = null,
        uint? ParentLocation = null,
        uint? PlacementId = null,
        uint? PhysicsState = null,
        uint? ObjectDescriptionFlags = null,
        float? Friction = null,
        float? Elasticity = null,
        uint IconId = 0,
        uint? Useability = null,
        float? UseRadius = null,
        uint? TargetType = null,
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
        uint? HookItemTypes = null,
        uint? HookType = null,
        uint? ContainerId = null,
        uint? WielderId = null,
        uint? ValidLocations = null,
        uint? CurrentWieldedLocation = null,
        uint? Priority = null,
        int? Structure = null,
        int? MaxStructure = null,
        float? Workmanship = null,
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
        uint? MaterialType = null,
        uint? HouseOwnerId = null,
        uint? MonarchId = null,
        HouseAccessRecord? Restrictions = null);

    public readonly record struct RemoteMotionState(
        ushort Stance,
        ushort? ForwardCommand,
        float? ForwardSpeed = null,
        IReadOnlyList<MotionWireRow>? Commands = null,
        ushort? SideStepCommand = null,
        float? SideStepSpeed = null,
        ushort? TurnCommand = null,
        float? TurnSpeed = null,
        byte MovementType = 0,
        uint? MoveToParameters = null,
        float? MoveToSpeed = null,
        float? MoveToRunRate = null,
        RelocateToTrailBlob? MoveToPath = null,
        PivotToTrailBlob? TurnToPath = null,
        uint? StickyObjectGuid = null,
        bool StandingLongJump = false)
    {
        public bool IsSrvControlledRelocateTo => MovementType is 6 or 7;

        public bool IsSrvControlledPivotTo => MovementType is 8 or 9;

        public bool RelocateToCanExec => !MoveToParameters.HasValue || (MoveToParameters.Value & 0x2u) is not 0;

        public bool RelocateTowards => MoveToParameters.HasValue && (MoveToParameters.Value & 0x200u) is not 0;

        public bool CanCharge => MoveToParameters.HasValue && (MoveToParameters.Value & 0x10u) is not 0;
    }

    /// <summary><c>Bitfield</c> is the raw UnPackNet flags word that feeds LocomotionParams.FromWire.</summary>
    public readonly record struct RelocateToTrailBlob(
        uint? TargetGuid,
        uint OriginCellId,
        float OriginX,
        float OriginY,
        float OriginZ,
        float DistanceToObject,
        float MinDistance,
        float FailDistance,
        float WalkRunThreshold,
        float DesiredHeading,
        uint Bitfield = 0);

    public readonly record struct PivotToTrailBlob(uint? TargetGuid, float? WireHeading, uint Bitfield, float Speed, float DesiredHeading);

    public readonly record struct MotionWireRow(ushort Command, ushort PackedSequence, float Speed);

    public readonly record struct TextureSwap(byte PartIndex, uint OldTexture, uint NewTexture);

    public readonly record struct PaletteSwap(uint SubPaletteId, byte Offset, byte Length);

    /// <summary>Landblock id, local XYZ, and a unit quaternion in W-X-Y-Z wire order.</summary>
    public readonly record struct RemotePosition(uint LandblockId, float PositionX, float PositionY, float PositionZ, float RotationW, float RotationX, float RotationY, float RotationZ);

    public readonly record struct AnimPartSwap(byte PartIndex, uint NewModelId);

    public readonly record struct SchemeBlob(uint? BasePaletteId, IReadOnlyList<PaletteSwap> SubPalettes, IReadOnlyList<TextureSwap> TextureChanges, IReadOnlyList<AnimPartSwap> AnimPartChanges);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        try
        {
            var cursor = new WireCursor(corpus);
            if (cursor.Word() != Opcode)
                return null;
            uint oid = cursor.Word();
            SchemeBlob model = ScanModelBlob(ref cursor);
            if (ScanKinetics(ref cursor) is not { } kinetics)
                return null;
            var descriptor = WeenieDescReader.Decode(ref cursor);
            var stamps = kinetics.Timestamps;

            return new Parsed(
                oid,
                kinetics.Position,
                kinetics.SetupTableId,
                model.AnimPartChanges,
                model.TextureChanges,
                model.SubPalettes,
                model.BasePaletteId,
                kinetics.Scale,
                descriptor.Name,
                descriptor.ItemType,
                kinetics.Movement?.MotionState,
                kinetics.MotionTableId,
                stamps.Instance,
                stamps.Teleport,
                stamps.ServerControlledMove,
                stamps.ForcePosition,
                stamps.Movement,
                PositionSequence: stamps.Position,
                ParentGuid: kinetics.Parent?.Guid,
                ParentLocation: kinetics.Parent?.LocationId,
                PlacementId: kinetics.AnimationFrame,
                PhysicsState: kinetics.RawState,
                ObjectDescriptionFlags: descriptor.ObjectDescriptionFlags,
                Friction: kinetics.Friction,
                Elasticity: kinetics.Elasticity,
                IconId: descriptor.IconId,
                Useability: descriptor.Useability,
                UseRadius: descriptor.UseRadius,
                TargetType: descriptor.TargetType,
                IconOverlayId: descriptor.IconOverlayId,
                IconUnderlayId: descriptor.IconUnderlayId,
                UiEffects: descriptor.UiEffects,
                WeenieClassId: descriptor.WeenieClassId,
                Value: descriptor.Value,
                StackSize: descriptor.StackSize,
                StackSizeMax: descriptor.StackSizeMax,
                Burden: descriptor.Burden,
                ItemsCapacity: descriptor.ItemsCapacity,
                ContainersCapacity: descriptor.ContainersCapacity,
                HookItemTypes: descriptor.HookItemTypes,
                HookType: descriptor.HookType,
                ContainerId: descriptor.ContainerId,
                WielderId: descriptor.WielderId,
                ValidLocations: descriptor.ValidLocations,
                CurrentWieldedLocation: descriptor.CurrentWieldedLocation,
                Priority: descriptor.Priority,
                Structure: descriptor.Structure,
                MaxStructure: descriptor.MaxStructure,
                Workmanship: descriptor.Workmanship,
                RadarBlipColor: descriptor.RadarBlipColor,
                RadarBehavior: descriptor.RadarBehavior,
                CombatUse: descriptor.CombatUse,
                PluralName: descriptor.PluralName,
                PetOwnerId: descriptor.PetOwnerId,
                AmmoType: descriptor.AmmoType,
                SpellId: descriptor.SpellId,
                CooldownId: descriptor.CooldownId,
                CooldownDuration: descriptor.CooldownDuration,
                Physics: kinetics,
                MaterialType: descriptor.MaterialType,
                HouseOwnerId: descriptor.HouseOwnerId,
                MonarchId: descriptor.MonarchId,
                Restrictions: descriptor.Restrictions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Position-based entry point kept for callers that walk a body by index.</summary>
    public static SchemeBlob PullModelBlob(ReadOnlySpan<byte> corpus, ref int spot)
    {
        var cursor = new WireCursor(corpus);
        cursor.Skip(spot);
        SchemeBlob model = ScanModelBlob(ref cursor);
        spot = cursor.At;
        return model;
    }

    // marker, then counts of sub-palettes / texture swaps / part swaps, each list, then 4-byte
    // alignment
    internal static SchemeBlob ScanModelBlob(ref WireCursor cursor)
    {
        if (!cursor.Has(4))
            throw new FormatException("truncated SchemeBlob header");
        cursor.Skip(1); // marker byte
        byte swatchTally = cursor.U8();
        byte textureTally = cursor.U8();
        byte pieceTally = cursor.U8();

        uint? baseSwatch = swatchTally > 0 ? cursor.PackedDword(SwatchKindStem) : null;

        PaletteSwap[] swatches = swatchTally is 0 ? [] : new PaletteSwap[swatchTally];
        for (int idx = 0; idx < swatches.Length; ++idx)
        {
            uint ident = cursor.PackedDword(SwatchKindStem);
            if (!cursor.Has(2))
                throw new FormatException("truncated PaletteSwap");
            swatches[idx] = new PaletteSwap(ident, cursor.U8(), cursor.U8());
        }

        TextureSwap[] textures = textureTally is 0 ? [] : new TextureSwap[textureTally];
        for (int idx = 0; idx < textures.Length; ++idx)
        {
            if (!cursor.Has(1))
                throw new FormatException("truncated TextureSwap");
            byte piece = cursor.U8();
            uint former = cursor.PackedDword(CanvasTextureKindStem);
            textures[idx] = new TextureSwap(piece, former, cursor.PackedDword(CanvasTextureKindStem));
        }

        AnimPartSwap[] pieces = pieceTally is 0 ? [] : new AnimPartSwap[pieceTally];
        for (int idx = 0; idx < pieces.Length; ++idx)
        {
            if (!cursor.Has(1))
                throw new FormatException("truncated AnimPartSwap");
            byte piece = cursor.U8();
            pieces[idx] = new AnimPartSwap(piece, cursor.PackedDword(GfxObjRefKindStem));
        }

        cursor.Align4();
        return new SchemeBlob(baseSwatch, swatches, textures, pieces);
    }
}

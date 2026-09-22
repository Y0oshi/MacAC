using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Shell;

/// <summary>What a world object is, as far as the radar cares.</summary>
public readonly record struct RadarObjectFacets(
    bool IsValid = true,
    byte BlipColorOverride = 0,
    bool IsPortal = false,
    bool IsVendor = false,
    bool IsAttackable = false,
    bool IsCreature = false,
    bool IsPlayer = false,
    bool IsAdmin = false,
    bool IsHiddenAdmin = false,
    bool IsPlayerKiller = false,
    bool IsPkLite = false,
    bool IsFreePk = false)
{
    private static class Bit
    {
        public const uint Player = 0x00000008u;
        public const uint Attackable = 0x00000010u;
        public const uint AvatarKiller = 0x00000020u;
        public const uint HiddenAdmin = 0x00000040u;
        public const uint Vendor = 0x00000200u;
        public const uint Portal = 0x00040000u;
        public const uint Admin = 0x00100000u;
        public const uint ReleasePk = 0x00200000u;
        public const uint PkLite = 0x02000000u;
        public const uint Invalid = 0x80000000u;
    }

    public static RadarObjectFacets FromPublicWeenieBlurb(
        uint gearKind,
        uint bitfield,
        byte blipTintOverride = 0)
    {
        bool Has(uint bit) => (bitfield & bit) is not 0;
        return new RadarObjectFacets(
            IsValid: !Has(Bit.Invalid),
            BlipColorOverride: blipTintOverride,
            IsPortal: Has(Bit.Portal),
            IsVendor: Has(Bit.Vendor),
            IsAttackable: Has(Bit.Attackable),
            IsCreature: (gearKind & (uint)GearKind.Creature) is not 0,
            IsPlayer: Has(Bit.Player),
            IsAdmin: Has(Bit.Admin),
            IsHiddenAdmin: Has(Bit.HiddenAdmin),
            IsPlayerKiller: Has(Bit.AvatarKiller),
            IsPkLite: Has(Bit.PkLite),
            IsFreePk: Has(Bit.ReleasePk));
    }
}

/// <summary>How the local player relates to a world object.</summary>
public readonly record struct RadarRelationFacets(
    bool IsFellowshipMember = false,
    bool IsFellowshipLeader = false,
    bool IsAllegianceMember = false,
    bool PlayerIsPlayerKiller = false,
    bool PlayerIsPkLite = false);

public static class RadarBlipPalette
{
    public readonly record struct MechRgba(float Red, float Green, float Blue, float Alpha)
    {
        public byte R => Quantize(Red);

        public byte G => Quantize(Green);

        public byte B => Quantize(Blue);

        public byte A => Quantize(Alpha);

        public MechRgba DimRgb(float multiplier) =>
            new(Red * multiplier, Green * multiplier, Blue * multiplier, Alpha);

        /// <summary>Packed <c>0xAABBGGRR</c>, the ImGui/OpenGL byte order.</summary>
        public uint ToAbgr32() => ((uint)A << 24) | ((uint)B << 16) | ((uint)G << 8) | R;

        private static byte Quantize(float lane) =>
            (byte)Math.Clamp((int)MathF.Round(lane * 255f), 0, 255);
    }

    public static readonly MechRgba Blue = new(0.25f, 0.660000026f, 1f, 1f);
    public static readonly MechRgba Gold = new(1f, 0.670000017f, 0f, 1f);
    public static readonly MechRgba Yellow = new(1f, 1f, 0.5f, 1f);
    public static readonly MechRgba White = new(1f, 1f, 1f, 1f);
    public static readonly MechRgba Red = new(1f, 0.25f, 0.389999986f, 1f);
    public static readonly MechRgba Purple = new(0.75f, 0.389999986f, 1f, 1f);
    public static readonly MechRgba Pink = new(1f, 0.660000026f, 0.75f, 1f);
    public static readonly MechRgba Green = new(0f, 0.5f, 0.25f, 1f);
    public static readonly MechRgba Cyan = new(0f, 1f, 1f, 1f);
    public static readonly MechRgba BrightGreen = new(0f, 1f, 0f, 1f);

    public static readonly MechRgba Default = White;
    public static readonly MechRgba Item = White;
    public static readonly MechRgba Admin = Cyan;
    public static readonly MechRgba Advocate = Pink;
    public static readonly MechRgba Creature = Gold;
    public static readonly MechRgba LifeStone = Blue;
    public static readonly MechRgba NPC = Yellow;
    public static readonly MechRgba PlayerKiller = Red;
    public static readonly MechRgba Portal = Purple;
    public static readonly MechRgba Sentinel = Cyan;
    public static readonly MechRgba Vendor = Yellow;
    public static readonly MechRgba Fellowship = BrightGreen;
    public static readonly MechRgba FellowshipLeader = BrightGreen;
    public static readonly MechRgba PKLite = Pink;

    private static readonly MechRgba[] OverrideSockets =
        [Default, Blue, Gold, White, Purple, Red, Pink, Green, Yellow, Cyan, BrightGreen];

    public static MechRgba For(uint gearKind, uint pwdBitfield)
    {
        return For(RadarObjectFacets.FromPublicWeenieBlurb(gearKind, pwdBitfield));
    }

    public static MechRgba For(RadarObjectFacets traits, RadarRelationFacets relationship = default)
    {
        if (!traits.IsValid)
            return Default;
        if (traits.BlipColorOverride is not 0)
            return ForOverride(traits.BlipColorOverride);
        if (traits.IsPortal)
            return Portal;
        if (traits.IsVendor)
            return Vendor;
        if (traits.IsAttackable && traits.IsCreature && !traits.IsPlayer)
            return Creature;
        if (!traits.IsPlayer)
            return Default;

        // Fellowship membership outranks everything else about a player
        if (relationship.IsFellowshipLeader)
            return FellowshipLeader;
        if (relationship.IsFellowshipMember)
            return Fellowship;

        if (traits.IsAdmin && !traits.IsHiddenAdmin)
            return Admin;
        if (traits.IsPlayerKiller)
            return PlayerKiller;
        return traits.IsPkLite ? PKLite : traits.IsFreePk ? Creature : Default;
    }

    public static MechRgba ForOverride(byte overrideVal)
    {
        return overrideVal < OverrideSockets.Length ? OverrideSockets[overrideVal] : Default;
    }
}

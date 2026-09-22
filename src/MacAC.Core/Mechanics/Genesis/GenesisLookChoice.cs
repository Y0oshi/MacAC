namespace MacAC.Mechanics.Genesis;

/// <summary>Every index and slider the character-creation appearance page exposes.</summary>
public readonly record struct GenesisLookChoice(
    uint EyesStrip,
    uint NoseStrip,
    uint MouthStrip,
    uint HairStyle,
    uint HairColor,
    uint EyeColor,
    uint HeadgearStyle,
    uint HeadgearColor,
    uint ShirtStyle,
    uint ShirtColor,
    uint TrousersStyle,
    uint TrousersColor,
    uint FootwearStyle,
    uint FootwearColor,
    double SkinShade,
    double HairShade,
    double HeadgearShade,
    double ShirtShade,
    double TrousersShade,
    double FootwearShade)
{
    public const uint Unset = 0xFFFFFFFFu;

    public const double UnsetShade = -1.0;

    public static GenesisLookChoice Default { get; } = new(
        Unset, Unset, Unset,
        Unset, Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        UnsetShade, UnsetShade, UnsetShade,
        UnsetShade, UnsetShade, UnsetShade);
}

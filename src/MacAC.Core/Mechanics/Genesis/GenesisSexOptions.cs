namespace MacAC.Mechanics.Genesis;

public sealed record GenesisSexOptions(
    int GenderKey,
    string Name,
    uint Scale,
    uint SetupId,
    uint SoundTableId,
    uint IconId,
    uint BasePaletteId,
    uint SkinPalSetId,
    uint PhysicsTableId,
    uint MotionTableId,
    uint CombatTableId,
    GenesisObjDesc BaseObjDesc,
    IReadOnlyList<uint> HairColors,
    IReadOnlyList<GenesisHairStyle> HairStyles,
    IReadOnlyList<uint> EyeColors,
    IReadOnlyList<GenesisEyeStrip> EyeStrips,
    IReadOnlyList<GenesisFaceStrip> NoseStrips,
    IReadOnlyList<GenesisFaceStrip> MouthStrips,
    IReadOnlyList<GenesisGearOption> Headgears,
    IReadOnlyList<GenesisGearOption> Shirts,
    IReadOnlyList<GenesisGearOption> Pants,
    IReadOnlyList<GenesisGearOption> Footwear,
    IReadOnlyList<uint> ClothingColors)
{
    public bool HasAnyLooksKnobs
    {
        get
        {
            return HairStyles.Count + EyeStrips.Count + NoseStrips.Count + MouthStrips.Count
        + Headgears.Count + Shirts.Count + Pants.Count + Footwear.Count > 0;
        }
    }
}

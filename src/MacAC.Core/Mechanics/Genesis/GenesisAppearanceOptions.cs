namespace MacAC.Mechanics.Genesis;

public sealed record GenesisHairStyle(
    uint IconId,
    bool Bald,
    uint AlternateSetup,
    GenesisObjDesc ObjDesc);

public sealed record GenesisEyeStrip(
    uint IconId,
    uint BaldIconId,
    GenesisObjDesc ObjDesc,
    GenesisObjDesc BaldObjDesc);

public sealed record GenesisFaceStrip(uint IconId, GenesisObjDesc ObjDesc);

public sealed record GenesisGearOption(
    string Name,
    uint ClothingTableId,
    uint WeenieDefaultId);

using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public readonly record struct GearCooldownAssets(IReadOnlyList<uint> Sprites)
{
    public const uint RegistryArrangementTag = 0x21000037u;
    public const uint SharedGearPrototypeIdent = 0x1000033Eu;
    public const uint LeadTopLayerElemIdent = 0x1000054Fu;
    public const int TopLayerTally = 10;

    public static GearCooldownAssets? TryLoad(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        var prototype = ArrangementLoader.ImportInfos(
            datFiles,
            RegistryArrangementTag,
            SharedGearPrototypeIdent);
        if (prototype is null)
            return null;

        uint[] sprites = new uint[TopLayerTally];
        for (int ordinal = 0; ordinal < sprites.Length; ++ordinal)
        {
            var topLayer = Find(
                prototype,
                LeadTopLayerElemIdent + (uint)ordinal);
            if (topLayer is null
                || !topLayer.StateMedia.TryGetValue("", out var media)
                || media.File is 0u)
                return null;
            sprites[ordinal] = media.File;
        }

        return new GearCooldownAssets(sprites);
    }

    private static ElemDetails? Find(ElemDetails trunk, uint ident)
    {
        if (trunk.Id == ident)
            return trunk;
        foreach (ElemDetails descendant in trunk.Children)
            if (Find(descendant, ident) is { } fit)
                return fit;
        return null;
    }
}

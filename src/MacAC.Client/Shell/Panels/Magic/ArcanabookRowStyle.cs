using System.Numerics;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public readonly record struct ArcanabookRowStyle(
    float Width,
    float Height,
    float IconLeft,
    float IconTop,
    float IconWidth,
    float IconHeight,
    float LabelLeft,
    float LabelWidth,
    uint FontDid,
    Vector4 LabelColor,
    uint BackgroundSprite,
    uint SelectedSprite)
{
    public const uint RegistryArrangementIdent = 0x21000037u;
    public const uint PrototypeIdent = 0x10000343u;
    public const uint ChosenTopLayerIdent = 0x10000342u;
    public const uint CaptionIdent = 0x10000344u;
    public const uint IconId = 0x1000033Bu;

    public static ArcanabookRowStyle? TryLoad(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        var prototype = ArrangementLoader.ImportInfos(datFiles, RegistryArrangementIdent, PrototypeIdent);
        return prototype is null
            || Find(prototype, ChosenTopLayerIdent) is not { } chosen
            || Find(prototype, CaptionIdent) is not { } caption
            || Find(prototype, IconId) is not { } glyph
            || !prototype.StateMedia.TryGetValue("", out var background)
            || !chosen.StateMedia.TryGetValue("", out var pick)
            || prototype.Width <= 0f
            || prototype.Height <= 0f
            ? null
            : new ArcanabookRowStyle(
            prototype.Width,
            prototype.Height,
            glyph.X,
            glyph.Y,
            glyph.Width,
            glyph.Height,
            caption.X,
            caption.Width,
            caption.FontDid,
            caption.FontColor ?? Vector4.One,
            background.File,
            pick.File);
    }

    private static ElemDetails? Find(ElemDetails trunk, uint ident)
    {
        if (trunk.Id == ident) return trunk;
        foreach (ElemDetails descendant in trunk.Children)
            if (Find(descendant, ident) is { } located) return located;
        return null;
    }
}

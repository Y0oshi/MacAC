using MacAC.Extensibility.Panels;

namespace MacAC.Client.Shell;

public static partial class MarkupDoc
{
    private const uint CoreHintTrunkElemIdent = 0x10000397u;

    private const uint CoreHintArrangementDid = 0x21000041u;

    private static Func<(uint tex, int w, int h)> AssembleGlyphSrc(
        string? glyphSort, Func<uint> identReader, IMarkupIconPicker? glyphs)
    {
        string sort = ValidateIconKind(glyphSort);
        return glyphs is null
            ? (static () => (0u, 0, 0))
            : sort switch
            {
                "did" => () => glyphs.LocateDid(
                    IconIdRules.Standardize(identReader())),
                "spell" => () => glyphs.LocateArcanum(identReader()),
                "item" => () => glyphs.LocateGear(identReader()),
                _ => throw new InvalidOperationException(
                    "unreachable - ValidateIconKind by now rejected anything else"),
            };
    }

    private static Func<uint, (uint tex, int w, int h)> AssembleRankGlyphLocate(
        string? glyphSort, IMarkupIconPicker glyphs)
    {
        string sort = ValidateIconKind(glyphSort);
        return sort switch
        {
            "did" => ident => glyphs.LocateDid(
                IconIdRules.Standardize(ident)),
            "spell" => glyphs.LocateArcanum,
            "item" => glyphs.LocateGear,
            _ => throw new InvalidOperationException(
                "unreachable - ValidateIconKind by now rejected anything else"),
        };
    }
}

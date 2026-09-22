namespace MacAC.Extensibility.Panels;

/// <summary>How an extension window should be presented by the host.</summary>
public sealed record PanelBlueprint(string WindowId, string Title)
{
    public string? GlyphPhrase { get; init; }

    public uint GlyphCanvasIdent { get; init; }

    public bool BeginShown { get; init; } = true;

    /// <summary>Whether the shared side panel shows a button for this window.</summary>
    public bool ShelfButtonVisible { get; init; } = true;
}

public readonly record struct PanelOwner(string Id, string DisplayName);

public static class IconIdRules
{
    private const uint BareOrdinalCeiling = 0x01000000u;

    private const uint RasterizeCanvasBase = 0x06000000u;

    public static uint Standardize(uint identOrOrdinal)
    {
        if (identOrOrdinal is 0u)
            return 0u;
        return identOrOrdinal < BareOrdinalCeiling
            ? RasterizeCanvasBase + identOrOrdinal
            : identOrOrdinal;
    }
}

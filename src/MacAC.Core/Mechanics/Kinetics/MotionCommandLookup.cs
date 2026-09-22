namespace MacAC.Mechanics.Kinetics;

/// <summary>Widens a 16-bit wire motion command back to its full 32-bit form.</summary>
public interface IMotionCommandLexicon
{
    /// <summary>The full command for a wire value, or 0 when the catalogue has no entry.</summary>
    uint ReconstructWholeDirective(ushort wireDirective);
}

/// <summary>Process-wide lookup backed by the catalogue the live servers speak.</summary>
public static class MotionCommandLookup
{
    private static readonly IMotionCommandLexicon Catalogue = new AceCurrentCommandCatalog();

    public static uint ReconstructWholeCommand(ushort wireDirective) => Catalogue.ReconstructWholeDirective(wireDirective);
}

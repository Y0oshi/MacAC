using MacAC.Assets.Pak;

namespace MacAC.Forge;

public static class PakProof
{
    public static void Check(
        string trail,
        in PakPreamble anticipatedPreamble,
        int anticipatedTocTally,
        IReadOnlyDictionary<PakAssetKind, int>? anticipatedKindCounts = null)
    {
        using PakScanner reader = new PakScanner(trail);
        PakPreamble actual = reader.Header;

        Demand(
            actual.FmtVer == PakFmt.LatestFmtVer,
            $"bake format version {actual.FmtVer} does not match {PakFmt.LatestFmtVer}");
        Demand(
            actual.BakeToolVer == PakFmt.LatestBakeToolVer,
            $"bake tool version {actual.BakeToolVer} does not match {PakFmt.LatestBakeToolVer}");
        Demand(
            actual.PortalIteration == anticipatedPreamble.PortalIteration
            && actual.CellIteration == anticipatedPreamble.CellIteration
            && actual.HighResIteration == anticipatedPreamble.HighResIteration
            && actual.LanguageIteration == anticipatedPreamble.LanguageIteration,
            "bake DAT iterations do not match the source collection");
        Demand(
            actual.TocTally == checked((uint)anticipatedTocTally),
            $"bake TOC count {actual.TocTally} does not match expected {anticipatedTocTally}");

        reader.VetTocStructure();
        if (anticipatedKindCounts is null)
            return;

        foreach ((PakAssetKind kind, int anticipated) in anticipatedKindCounts)
        {
            int counted = reader.TallyListings(kind);
            Demand(
                counted == anticipated,
                $"bake catalog type {kind} contains {counted} keys; expected {anticipated}");
        }
    }

    private static void Demand(bool holds, string complaint)
    {
        if (!holds)
            throw new InvalidDataException(complaint);
    }
}

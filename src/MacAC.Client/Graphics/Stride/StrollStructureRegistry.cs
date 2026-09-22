using System.Diagnostics.CodeAnalysis;

namespace MacAC.Client.Graphics.Stride;

public sealed class StrollStructureRegistry
{
    private readonly Dictionary<uint, IReadOnlyList<StrideBuildingMint.Entry>> _byLb = [];
    private readonly Dictionary<StrollStructure, StrideBuildingMint.Entry> _byStructure = [];

    public void Publish(uint lbIdent, IReadOnlyList<StrideBuildingMint.Entry> listings)
    {
        uint tag = lbIdent & 0xFFFF0000u;
        if (_byLb.TryGetValue(tag, out IReadOnlyList<StrideBuildingMint.Entry>? earlier))
        {
            foreach (StrideBuildingMint.Entry listing in earlier)
                _byStructure.Remove(listing.Building);
        }
        _byLb[tag] = listings;
        foreach (StrideBuildingMint.Entry listing in listings)
            _byStructure[listing.Building] = listing;
    }

    public void Retire(uint lbIdent)
    {
        uint tag = lbIdent & 0xFFFF0000u;
        if (_byLb.Remove(tag, out IReadOnlyList<StrideBuildingMint.Entry>? earlier))
        {
            foreach (StrideBuildingMint.Entry listing in earlier)
                _byStructure.Remove(listing.Building);
        }
    }

    public IReadOnlyList<StrideBuildingMint.Entry> FetchStructures(uint lbIdent)
    {
        return _byLb.TryGetValue(lbIdent & 0xFFFF0000u, out IReadOnlyList<StrideBuildingMint.Entry>? roster)
            ? roster
            : Array.Empty<StrideBuildingMint.Entry>();
    }

    public bool TryFetchListing(
        StrollStructure structure, [MaybeNullWhen(false)] out StrideBuildingMint.Entry listing) =>
        _byStructure.TryGetValue(structure, out listing);

    public int LbCount => _byLb.Count;
}

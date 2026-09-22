namespace MacAC.Client.Graphics.Batching;

public sealed class StructureRegistry
{
    private readonly Dictionary<uint, List<Structure>> _byChamberIdent = [];

    private readonly Dictionary<uint, Structure> _byStructureIdent = [];

    public void Add(Structure building)
    {
        if (_byStructureIdent.TryGetValue(building.StructureIdent, out var extant) && ReferenceEquals(extant, building))
            return;
        _byStructureIdent[building.StructureIdent] = building;
        foreach (var chamberIdent in building.EnvironChamberIdents)
        {
            if (!_byChamberIdent.TryGetValue(chamberIdent, out var roster))
            {
                roster = [];
                _byChamberIdent[chamberIdent] = roster;
            }
            if (!roster.Contains(building)) roster.Add(building);
        }
    }

    public IReadOnlyList<Structure> FetchStructuresContainingChamber(uint chamberIdent)
    {
        return _byChamberIdent.TryGetValue(chamberIdent, out var roster) ? roster : Array.Empty<Structure>();
    }

    public Structure? FetchByIdent(uint structureIdent)
    {
        return _byStructureIdent.TryGetValue(structureIdent, out var building) ? building : null;
    }

    /// <summary>Enumerates every registered building in unspecified order.</summary>
    public IEnumerable<Structure> All() => _byStructureIdent.Values;

    public int Count => _byStructureIdent.Count;
}

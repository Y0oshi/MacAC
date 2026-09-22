namespace MacAC.Client.Graphics.Stride;

public enum StrideEventKind
{
    Landscape,

    Building,

    DrawInside,

    DrawCells,
}

public readonly record struct StrideEvent(
    StrideEventKind Kind,
    uint CellId,
    int OutsideViewCount,
    IReadOnlyList<uint> Cells)
{
    public static StrideEvent Landscape(int engagedLensTally)
    {
        return new(StrideEventKind.Landscape, 0, engagedLensTally, Array.Empty<uint>());
    }

    public static StrideEvent Building(uint locusChamberIdent)
    {
        return new(StrideEventKind.Building, locusChamberIdent, 0, Array.Empty<uint>());
    }

    public static StrideEvent DrawInside(uint chamberIdent)
        => new(StrideEventKind.DrawInside, chamberIdent, 0, Array.Empty<uint>());

    public static StrideEvent DrawCells(int beyondLensTally, IReadOnlyList<uint> chambers)
        => new(StrideEventKind.DrawCells, 0, beyondLensTally, chambers);
}

public interface IStrollSignalDrain
{
    void Emit(in StrideEvent strollSignal);

    void OnSceneryViews(StridePortalView engagedViews) { }

    void OnLandChamberPivot(uint lbIdent, int flankChamberTally, int chamberOrdinal) { }

    void OnOrderChamberPivot(uint lbIdent, int flankChamberTally, int chamberOrdinal) { }

    void OnLandscapeCellTurn(uint chamberIdent) { }

    void OnLandscapeCellTurn(uint lbIdent, int flankChamberTally, int chamberOrdinal)
    {
        OnLandscapeCellTurn(
            (lbIdent & 0xFFFF0000u) | checked((uint)(chamberOrdinal + 1)));
    }

    void OnOrderChamberQuit(uint lbIdent, int flankChamberTally, int chamberOrdinal) { }

    void OnStructurePivot(StrollStructure structure) { }

    void OnStructureShellPivot(
        StrollStructure structure,
        StrideBuildingPicking pick)
    { }

    void OnPunchGeo(
        StrollStructure structure, StridePolygon polyg, int engagedLensOrdinal)
    { }

    void OnInteriorFloodPaintPivot(IReadOnlyList<uint> chambers, int beyondLensTally) { }

    void OnWeatherPivot(uint beholderChamberIdent) { }
}

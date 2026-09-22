using System.Numerics;

namespace MacAC.Mechanics.Realm.Cells;

/// <summary>A doorway between two cells, with the polygon that frames it.</summary>
public readonly struct ChamberGateway(
    uint anotherChamberIdent,
    ushort anotherGatewayIdent,
    ushort polygIdent,
    ushort flagSet,
    IReadOnlyList<Vector3>? polygOwn = null)
{
    private const ushort FlankBit = 0x2;

    public uint OtherCellId { get; } = anotherChamberIdent;

    public ushort OtherGatewayId { get; } = anotherGatewayIdent;

    public ushort PolygonId { get; } = polygIdent;

    public ushort Flags { get; } = flagSet;

    public bool PortalSide => (Flags & FlankBit) is 0;

    public IReadOnlyList<Vector3> PolygOwn { get; } = polygOwn ?? [];
}

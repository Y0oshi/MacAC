namespace MacAC.Mechanics.Kinetics;

/// <summary>One portal polygon of an environment cell and where it leads.</summary>
public readonly struct PortalFacts(ushort anotherChamberIdent, ushort polygIdent, ushort flagSet)
{
    private const ushort FlankBit = 2;

    public ushort OtherCellId { get; } = anotherChamberIdent;
    public ushort PolygonId { get; } = polygIdent;
    public ushort Flags { get; } = flagSet;

    public bool PortalSide => (Flags & FlankBit) is 0;
}

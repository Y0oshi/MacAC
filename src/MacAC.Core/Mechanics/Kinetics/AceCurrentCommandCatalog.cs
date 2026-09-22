using MacAC.Dat;
using DRWMotionCommand =  MacAC.Dat.MotionId;

namespace MacAC.Mechanics.Kinetics;

public sealed class AceCurrentCommandCatalog : IMotionCommandLexicon
{
    private readonly Dictionary<ushort, uint> _byWire = Index();

    public uint ReconstructWholeDirective(ushort wireDirective)
    {
        return wireDirective is not 0 && _byWire.TryGetValue(wireDirective, out uint whole) ? whole : 0u;
    }

    /// <summary>Among commands sharing a wire value, the one with the lowest class byte.</summary>
    public static uint LocateClassPrecedence(IReadOnlyList<uint> contenders)
    {
        uint finest = contenders[0];
        for (int idx = 1; idx < contenders.Count; ++idx)
        {
            if ((contenders[idx] >> 24) < (finest >> 24))
                finest = contenders[idx];
        }
        return finest;
    }

    private static Dictionary<ushort, uint> Index()
    {
        var contenders = new Dictionary<ushort, List<uint>>(512);
        foreach (DRWMotionCommand directive in Enum.GetValues<DRWMotionCommand>())
        {
            uint whole = (uint)directive;
            ushort wire = (ushort)(whole & 0xFFFFu);
            if (wire is 0)
                continue; // Invalid / unmappable

            if (!contenders.TryGetValue(wire, out List<uint>? bin))
                contenders[wire] = bin = new List<uint>(1);
            if (!bin.Contains(whole))
                bin.Add(whole);
        }

        var byWire = new Dictionary<ushort, uint>(contenders.Count);
        foreach ((ushort wire, List<uint> bin) in contenders)
            byWire[wire] = bin.Count is 1 ? bin[0] : LocateClassPrecedence(bin);
        return byWire;
    }
}

using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Realm;

/// <summary>The 3x3 ring of landblocks around a centre, loaded straight from the DATs.</summary>
public sealed class RealmView
{
    private RealmView(uint middleLbIdent, IReadOnlyList<MountedLandblock> lbs)
    {
        MiddleLbIdent = middleLbIdent;
        Landblocks = lbs;
    }

    public uint MiddleLbIdent { get; }

    public IReadOnlyList<MountedLandblock> Landblocks { get; }

    public IEnumerable<RealmActor> AllActors => Landblocks.SelectMany(static landblock => landblock.Entities);

    public static RealmView Load(IDatRecordSource datFiles, uint middleLbIdent)
    {
        var loop = new List<MountedLandblock>();
        foreach (uint ident in NeighborLbIdents(middleLbIdent))
        {
            if (LandblockReader.Load(datFiles, ident) is { } chunk)
                loop.Add(chunk);
        }
        return new RealmView(middleLbIdent, loop);
    }

    public static IEnumerable<uint> NeighborLbIdents(uint middleLbIdent)
    {
        int cx = (int)((middleLbIdent >> 24) & 0xFFu);
        int cy = (int)((middleLbIdent >> 16) & 0xFFu);
        for (int dy = -1; dy <= 1; ++dy)
        {
            for (int dx = -1; dx <= 1; ++dx)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x is < 0 or > 0xFF || y is < 0 or > 0xFF)
                    continue;
                yield return (uint)((x << 24) | (y << 16) | 0xFFFF);
            }
        }
    }
}

using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Paging;

internal interface ISealedDungeonChamberClassifier
{
    bool IsSealedDungeon(uint chamberIdent);
}

internal sealed class DatSealedDungeonChamberClassifier(
    IDatAccess dats,
    object datLock)
        : ISealedDungeonChamberClassifier
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));

    public bool IsSealedDungeon(uint chamberIdent)
    {
        uint lo = chamberIdent & 0xFFFFu;
        if (lo is < 0x0100u or >= 0xFFFEu)
            return false;

        RoomCell? environChamber;
        lock (_datMutex)
            environChamber = _datFiles.Get<RoomCell>(chamberIdent);
        return environChamber is not null
            && !environChamber.Bits.HasFlag(RoomCellBits.SeenOutside);
    }
}

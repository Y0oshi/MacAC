using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Paging;

internal sealed class DatSpawnClaimFillingClassifier
{
    private readonly Func<uint, TerrainTileExtras?> _consult;
    private (uint Claim, bool Unhydratable)? _memo;

    public DatSpawnClaimFillingClassifier(
        IDatAccess datFiles,
        object datMutex)
        : this(BuildConsult(datFiles, datMutex))
    {
    }

    internal DatSpawnClaimFillingClassifier(
        Func<uint, TerrainTileExtras?> lookup) =>
        _consult = lookup ?? throw new ArgumentNullException(nameof(lookup));

    public bool IsUnhydratable(uint claim)
    {
        uint lo = claim & 0xFFFFu;
        if (lo < 0x0100u)
            return false;
        if (_memo is { } memo && memo.Claim == claim)
            return memo.Unhydratable;

        var details = _consult((claim & 0xFFFF0000u) | 0xFFFEu);
        bool unhydratable = details is null
            || details.CellCount is 0
            || lo >= 0x0100u + details.CellCount;
        _memo = (claim, unhydratable);
        return unhydratable;
    }

    public void Reset() => _memo = null;

    private static Func<uint, TerrainTileExtras?> BuildConsult(
        IDatAccess datFiles,
        object datMutex)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(datMutex);
        return ident =>
        {
            lock (datMutex)
                return datFiles.Get<TerrainTileExtras>(ident);
        };
    }
}

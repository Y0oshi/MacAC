using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Kinetics;

internal sealed class OnlineContactAssetHerald(
    KineticAssetCache cache,
    IBakedContactSource source)
{
    private readonly KineticAssetCache _stash = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IBakedContactSource _src = source ?? throw new ArgumentNullException(nameof(source));

    public void StashGfxObjRef(uint ident, PartMesh gfxObjRef)
    {
        ArgumentNullException.ThrowIfNull(gfxObjRef);
        PackedGfxObjContactAsset? readied =
            _stash.FetchPlanarGfxObjRef(ident) is not null
                ? null
                : Demand(
                    _src.ScanGfxObjRefImpact(ident),
                    "GfxObj collision",
                    ident);
        _stash.CacheGfxObj(ident, gfxObjRef, readied);
    }

    public void StashRig(uint ident, RigSpec rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        PackedSetupContact? readied =
            _stash.FetchPlanarRig(ident) is not null
                ? null
                : Demand(
                    _src.ReadSetupCollision(ident),
                    "Setup collision",
                    ident);
        _stash.CacheSetup(ident, rig, readied);
    }

    private static T Demand<T>(
        BakedContactRead<T> outcome,
        string sort,
        uint ident)
        where T : class
    {
        return outcome.Status == BakedAssetReadStatus.Loaded &&
            outcome.Data is not null
            ? outcome.Data
            : throw new InvalidDataException(
            $"{sort} 0x{ident:X8} is {outcome.Status}. " +
            "Live collision can't publish only one representation");
    }
}

using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

internal static class FoliageWindTaxonomy
{
    internal const uint CutoutFoliageBit = 0x2u;

    internal const uint TrunkBit = 0x4u;

    internal static uint Classify(
        uint actorIdent,
        bool isExcluded,
        SeeThroughKind seeThrough,
        bool triMeshHasCutoutSubset)
    {
        if (!IsProceduralScenery(actorIdent) || isExcluded)
            return 0u;
        if (seeThrough == SeeThroughKind.ClipMap)
            return CutoutFoliageBit;
        return seeThrough == SeeThroughKind.Opaque && triMeshHasCutoutSubset ? TrunkBit : 0u;
    }

    internal static bool CalculateActorHasCutoutSubset<T, TContext>(
        IReadOnlyList<T> rigPieces,
        TContext ctx,
        Func<TContext, T, bool> hasCutoutSubset)
    {
        for (int idx = 0; idx < rigPieces.Count; ++idx)
        {
            if (hasCutoutSubset(ctx, rigPieces[idx]))
                return true;
        }
        return false;
    }

    internal static bool IsProceduralScenery(uint actorIdent) =>
        SceneryIdPool.IsInNamespace(actorIdent);
}

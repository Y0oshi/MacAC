using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{
    internal TriMeshPushPrice PlanPushPrice(
        HarvestedMesh triMeshBlob,
        IReadOnlySet<TextureAtlasKeeper> mipmapsAlreadyBudgeted,
        ulong fifoGen)
    {
        ArgumentNullException.ThrowIfNull(triMeshBlob);
        ArgumentNullException.ThrowIfNull(mipmapsAlreadyBudgeted);
        if (HasRasterizeBlob(triMeshBlob.ObjectId))
            return default;

        var plans = new Dictionary<
            (int Width, int Height, TexelLayout Format),
            List<PushTilesetScheme>>();
        long arrAllocOctets = 0;
        long mipmapOctets = 0;
        int newArrTally = 0;

        PlanTextureJob(
            triMeshBlob,
            plans,
            mipmapsAlreadyBudgeted,
            ref arrAllocOctets,
            ref mipmapOctets,
            ref newArrTally);

        var bufPlan = PlanGlobalBufJob(triMeshBlob);

        return new TriMeshPushPrice(
            GuessPushOctets(triMeshBlob),
            arrAllocOctets,
            mipmapOctets,
            newArrTally,
            bufPlan.UploadBytes,
            bufPlan.AllocationBytes,
            bufPlan.CopyBytes,
            bufPlan.NewBufferCount,
            fifoGen);
    }

    private GlobalTriMeshPushScheme PlanGlobalBufJob(HarvestedMesh triMeshBlob)
    {
        (int vertTally, int ordinalTally) = FetchGlobalTriMeshElemCounts(triMeshBlob);
        return vertTally is 0 || ordinalTally is 0 || GlobalBuf is null ? default : GlobalBuf.PlanPush(vertTally, ordinalTally);
    }

    private void PlanTextureJob(
        HarvestedMesh triMeshBlob,
        Dictionary<(int Width, int Height, TexelLayout Format), List<PushTilesetScheme>> plans,
        IReadOnlySet<TextureAtlasKeeper> mipmapsAlreadyBudgeted,
        ref long arrAllocOctets,
        ref long mipmapOctets,
        ref int newArrTally)
    {
        if (triMeshBlob.IsSetup)
        {
            if (triMeshBlob.EnvCellGeometry is { } nested
                && !HasRasterizeBlob(nested.ObjectId))
            {
                PlanTextureJob(
                    nested,
                    plans,
                    mipmapsAlreadyBudgeted,
                    ref arrAllocOctets,
                    ref mipmapOctets,
                    ref newArrTally);
            }
            return;
        }
        if (triMeshBlob.Vertices.Length is 0)
            return;

        foreach (var (fmt, lots) in triMeshBlob.TextureBatches)
        {
            if (!plans.TryGetValue(fmt, out List<PushTilesetScheme>? tilesetPlans))
            {
                tilesetPlans = [];
                if (_globalTilesets.TryGetValue(fmt, out List<TextureAtlasKeeper>? extantTilesets))
                {
                    foreach (TextureAtlasKeeper extant in extantTilesets)
                    {
                        tilesetPlans.Add(new PushTilesetScheme
                        {
                            Existing = extant,
                            Capacity = extant.SumSockets,
                            OnHandSockets = extant.OnHandSlots,
                            SumArrOctets = extant.TextureArr.SumDimsInOctets,
                        });
                    }
                }
                plans.Add(fmt, tilesetPlans);
            }

            foreach (TextureHarvestBatch lot in lots)
            {
                if (lot.Indices.Count is 0)
                    continue;

                PushTilesetScheme? chosen = null;
                for (int idx = 0; idx < tilesetPlans.Count; ++idx)
                {
                    var contender = tilesetPlans[idx];
                    if (contender.HasTexture(lot.Key))
                    {
                        chosen = contender;
                        break;
                    }
                }
                if (chosen is null)
                {
                    for (int idx = 0; idx < tilesetPlans.Count; ++idx)
                    {
                        var contender = tilesetPlans[idx];
                        if (contender.OnHandSockets > 0)
                        {
                            chosen = contender;
                            break;
                        }
                    }
                }

                if (chosen is null)
                {
                    int cap = TextureAtlasKeeper.DeriveStartingCap(
                        fmt.Width,
                        fmt.Height,
                        fmt.Format);
                    long sumArrOctets = TextureAtlasKeeper.DeriveArrOctets(
                        fmt.Width,
                        fmt.Height,
                        fmt.Format);
                    chosen = new PushTilesetScheme
                    {
                        Capacity = cap,
                        OnHandSockets = cap,
                        SumArrOctets = sumArrOctets,
                    };
                    tilesetPlans.Add(chosen);
                    arrAllocOctets = checked(arrAllocOctets + sumArrOctets);
                    ++newArrTally;
                }

                if (chosen.HasTexture(lot.Key))
                    continue;

                chosen.PlannedTags.Add(lot.Key);
                chosen.OnHandSockets--;
                if (!chosen.Touched)
                {
                    bool mipAlreadyBudgeted = chosen.Existing is not null
                        && mipmapsAlreadyBudgeted.Contains(chosen.Existing);
                    if (!mipAlreadyBudgeted)
                        mipmapOctets = checked(mipmapOctets + chosen.SumArrOctets);
                    chosen.Touched = true;
                }
            }
        }
    }
}

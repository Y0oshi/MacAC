using System.Collections.Concurrent;
using MacAC.Dat;
using MacAC.Mechanics.Data;
using DatEmitterType =  MacAC.Dat.EmitterShape;
using DatGfxObj =  MacAC.Dat.PartMesh;
using DatGfxObjDegradeInfo =  MacAC.Dat.LodTable;
using DatGfxObjFlags =  MacAC.Dat.PartMeshBits;
using DatParticleEmitter =  MacAC.Dat.EmitterDesc;
using DatParticleType =  MacAC.Dat.ParticleMotion;

namespace MacAC.Mechanics.Effects;

public enum EmitterSpecMissKind
{
    None,
    MissingEmitterInfo,
    InvalidHardwareGfxObjId,
    MissingHardwareGfxObj,
}

public readonly record struct EmitterSpecMiss(EmitterSpecMissKind Kind, uint RelatedDatId = 0u)
{
    public static EmitterSpecMiss None => default;
}

/// <summary>The retail LOD distance for a particle quad: the second-to-last degrade entry.</summary>
public static class CanonParticleLodDistance
{
    public const float DefaultGap = 100f;

    public static float FromListings(IReadOnlyList<LodLevel>? listings)
    {
        if (listings is null || listings.Count is 0)
            return DefaultGap;
        return listings[listings.Count <= 2 ? 0 : listings.Count - 2].MaxDist;
    }
}

public sealed class EmitterSpecRegistry
{
    private readonly record struct Resolution(bool Ok, float MaxDegradeDistance, EmitterSpecMiss Miss)
    {
        public static Resolution Success(float gap) => new(true, gap, EmitterSpecMiss.None);

        public static Resolution Fail(EmitterSpecMissKind sort, uint relatedDatIdent = 0u) =>
            new(false, 0f, new EmitterSpecMiss(sort, relatedDatIdent));
    }

    private readonly Func<uint, DatParticleEmitter?>? _fetch;
    private readonly Func<DatParticleEmitter, Resolution>? _lod;
    private readonly ConcurrentDictionary<uint, EmitterSpec> _recognized = new();
    private readonly ConcurrentDictionary<uint, EmitterSpecMiss> _misses = new();

    public EmitterSpecRegistry() : this((Func<uint, DatParticleEmitter?>?)null)
    {
    }

    public EmitterSpecRegistry(IDatRecordSource datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        _fetch = ident => Quietly<DatParticleEmitter>(datFiles, ident);
        _lod = spout => LodGap(datFiles, spout);
    }

    public EmitterSpecRegistry(Func<uint, DatParticleEmitter?>? locator) => _fetch = locator;

    public int Count => _recognized.Count;

    public int MissTally => _misses.Count;

    public void Register(EmitterSpec descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _recognized[descriptor.DatIdent] = descriptor;
        _misses.TryRemove(descriptor.DatIdent, out _);
    }

    public EmitterSpec Get(uint spoutIdent)
    {
        return TryGet(spoutIdent, out EmitterSpec? spec)
            ? spec
            : throw new KeyNotFoundException($"ParticleEmitterInfo 0x{spoutIdent:X8} wasn't found");
    }

    public bool TryGet(uint spoutIdent, out EmitterSpec descriptor) => TryGet(spoutIdent, out descriptor, out _);

    public bool TryGet(uint spoutIdent, out EmitterSpec descriptor, out EmitterSpecMiss miss)
    {
        if (_recognized.TryGetValue(spoutIdent, out descriptor!))
        {
            miss = EmitterSpecMiss.None;
            return true;
        }
        if (_misses.TryGetValue(spoutIdent, out miss))
        {
            descriptor = null!;
            return false;
        }

        if (_fetch?.Invoke(spoutIdent) is { } dat)
        {
            Resolution lod = _lod?.Invoke(dat) ?? Resolution.Success(CanonParticleLodDistance.DefaultGap);
            if (!lod.Ok)
                return Miss(spoutIdent, lod.Miss, out descriptor, out miss);

            descriptor = FromDat(spoutIdent, dat, lod.MaxDegradeDistance);
            _recognized[spoutIdent] = descriptor;
            miss = EmitterSpecMiss.None;
            return true;
        }

        return Miss(spoutIdent, new EmitterSpecMiss(EmitterSpecMissKind.MissingEmitterInfo, spoutIdent), out descriptor, out miss);
    }

    public static EmitterSpec FromDat(uint spoutIdent, DatParticleEmitter dat, float upperDowngradeGap = CanonParticleLodDistance.DefaultGap)
    {
        ArgumentNullException.ThrowIfNull(dat);

        float birthrate = MathF.Max(0f, (float)dat.Birthrate);
        float lifespan = MathF.Max(0f, (float)dat.Lifespan);
        float lifespanRand = MathF.Abs((float)dat.LifespanRand);
        float lifespanLower = MathF.Max(0f, lifespan - lifespanRand);

        EmitterBits flagSet = EmitterBits.Billboard | EmitterBits.FaceCamera;
        if (dat.IsParentLocal)
            flagSet |= EmitterBits.AttachLocal;

        return new EmitterSpec
        {
            UpperDowngradeGap = upperDowngradeGap,
            DatIdent = spoutIdent,
            Type = Map(dat.Motion),
            SpoutSort = Map(dat.Shape),
            Flags = flagSet,
            GfxObjId = dat.PartMeshId,
            HwGfxObjId = dat.HwPartMeshId,
            Birthrate = birthrate,
            EmitRate = dat.Shape == DatEmitterType.BirthratePerSec && birthrate > 0f ? 1f / birthrate : 0f,
            MaxParticles = Math.Max(1, dat.MaxParticles),
            InitialParticles = Math.Max(0, dat.InitialParticles),
            TotalParticles = Math.Max(0, dat.TotalParticles),
            SumInterval = MathF.Max(0f, (float)dat.TotalSeconds),
            Lifespan = lifespan,
            LifespanRand = lifespanRand,
            LifespanLower = lifespanLower,
            LifespanUpper = MathF.Max(lifespanLower, lifespan + lifespanRand),
            OffsetDir = dat.OffsetDir,
            MinOffset = dat.MinOffset,
            MaxOffset = dat.MaxOffset,
            SummonDiskRadius = dat.MaxOffset,
            StartingVel = dat.A,
            Gravity = dat.B,
            A = dat.A,
            MinA = dat.MinA,
            MaxA = dat.MaxA,
            B = dat.B,
            MinB = dat.MinB,
            MaxB = dat.MaxB,
            C = dat.C,
            MinC = dat.MinC,
            MaxC = dat.MaxC,
            BeginDims = dat.StartScale,
            FinishDims = dat.FinalScale,
            ScaleRand = dat.ScaleRand,
            BeginAlpha = 1f - Math.Clamp((float)dat.StartTrans, 0f, 1f),
            FinishAlpha = 1f - Math.Clamp((float)dat.FinalTrans, 0f, 1f),
            TransRand = dat.TransRand,
        };
    }

    internal static uint FetchCanonHardwareGfxObjRefIdent(DatParticleEmitter spout) => spout.HwPartMeshId;

    private bool Miss(uint spoutIdent, EmitterSpecMiss miss, out EmitterSpec descriptor, out EmitterSpecMiss failure)
    {
        _misses.TryAdd(spoutIdent, miss);
        failure = miss;
        descriptor = null!;
        return false;
    }

    private static Resolution LodGap(IDatRecordSource datFiles, DatParticleEmitter spout)
    {
        uint gfxObjRefIdent = FetchCanonHardwareGfxObjRefIdent(spout);
        if (gfxObjRefIdent is 0)
            return Resolution.Fail(EmitterSpecMissKind.InvalidHardwareGfxObjId);

        if (Quietly<DatGfxObj>(datFiles, gfxObjRefIdent) is not { } gfx)
            return Resolution.Fail(EmitterSpecMissKind.MissingHardwareGfxObj, gfxObjRefIdent);
        if (!gfx.Bits.HasFlag(DatGfxObjFlags.HasDIDDegrade) || gfx.LodTableId is 0)
            return Resolution.Success(CanonParticleLodDistance.DefaultGap);

        var downgrade = Quietly<DatGfxObjDegradeInfo>(datFiles, gfx.LodTableId);
        return Resolution.Success(CanonParticleLodDistance.FromListings(downgrade?.Levels));
    }

    private static T? Quietly<T>(IDatRecordSource datFiles, uint ident) where T : class, IDatRecord
    {
        if (datFiles is null)
            return null;
        try
        {
            return datFiles.Get<T>(ident);
        }
        catch
        {
            return null;
        }
    }

    private static EmitterSort Map(DatEmitterType kind)
    {
        return kind switch
        {
            DatEmitterType.BirthratePerSec => EmitterSort.BirthratePerSec,
            DatEmitterType.BirthratePerMeter => EmitterSort.BirthratePerMeter,
            _ => EmitterSort.Unknown,
        };
    }

    private static MoteKind Map(DatParticleType kind)
    {
        return kind switch
        {
            DatParticleType.Still => MoteKind.Still,
            DatParticleType.LocalVelocity => MoteKind.LocalVelocity,
            DatParticleType.ParabolicLVGA => MoteKind.ParabolicLVGA,
            DatParticleType.ParabolicLVGAGR => MoteKind.ParabolicLVGAGR,
            DatParticleType.Swarm => MoteKind.Swarm,
            DatParticleType.Explode => MoteKind.Explode,
            DatParticleType.Implode => MoteKind.Implode,
            DatParticleType.ParabolicLVLA => MoteKind.ParabolicLVLA,
            DatParticleType.ParabolicLVLALR => MoteKind.ParabolicLVLALR,
            DatParticleType.ParabolicGVGA => MoteKind.ParabolicGVGA,
            DatParticleType.ParabolicGVGAGR => MoteKind.ParabolicGVGAGR,
            DatParticleType.GlobalVelocity => MoteKind.GlobalVelocity,
            _ => MoteKind.Unknown,
        };
    }
}

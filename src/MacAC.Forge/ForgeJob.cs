using MacAC.Assets.Pak;

namespace MacAC.Forge;

/// <summary>One forge run's inputs (from the CLI via Program, or built directly by tests).</summary>
public sealed record ForgeJob
{
    public required string DatDirection { get; init; }
    public required string OutTrail { get; init; }
    public HashSet<uint>? IdentSift { get; init; }
    public HashSet<uint>? LbSift { get; init; }
    public int Threads { get; init; } = System.Environment.ProcessorCount;
    public CancellationToken AbortTicket { get; init; }
    public IForgeProgressTap? Progress { get; init; }
}

/// <summary>What a run wrote, counted per pak asset type, plus timing and memory peaks.</summary>
public sealed record ForgeTally
{
    public required PakPreamble Header { get; init; }
    public required int GfxObjRefTags { get; init; }
    public required int RigTags { get; init; }
    public required int EnvironChamberTags { get; init; }
    public required int UniqueEnvironChamberGeometries { get; init; }
    public required int EnvironChamberAliases { get; init; }
    public required int GfxObjRefImpactTags { get; init; }
    public required int UniqueGfxObjRefImpacts { get; init; }
    public required int GfxObjRefImpactAliases { get; init; }
    public required int RigImpactTags { get; init; }
    public required int UniqueRigImpacts { get; init; }
    public required int RigImpactAliases { get; init; }
    public required int ChamberStructureImpactTags { get; init; }
    public required int UniqueChamberStructureImpacts { get; init; }
    public required int ChamberStructureImpactAliases { get; init; }
    public required int EnvironChamberWiringTags { get; init; }
    public required int TextureCargoTags { get; init; }
    public required int FlankLinedTags { get; init; }
    public int FlankLinedDuplicateTags { get; init; }
    public required int PhysicalBlobs { get; init; }
    public required int SumTags { get; init; }
    public required int Failures { get; init; }
    public required TimeSpan ExtractionAndEmitPassed { get; init; }
    public required TimeSpan Passed { get; init; }
    public required long ProductOctets { get; init; }
    public required long PeakWorkingSetOctets { get; init; }
    public required long PeakPrivateOctets { get; init; }
    public required long DecodedCargoBytes { get; init; }
    public required long StoredCargoBytes { get; init; }
    public required int CompressedBlobs { get; init; }
    public required IReadOnlyDictionary<PakAssetKind, int> KindCounts { get; init; }

    public double EnvironChamberDedupRatio
    {
        get
        {
            return UniqueEnvironChamberGeometries is 0 ? 0 : (double)EnvironChamberTags / UniqueEnvironChamberGeometries;
        }
    }

    public double CargoCompressionRatio
    {
        get
        {
            return StoredCargoBytes is 0 ? 0 : (double)DecodedCargoBytes / StoredCargoBytes;
        }
    }
}

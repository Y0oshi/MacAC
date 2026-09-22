namespace MacAC.Client.Graphics.Tenancy;

internal enum TenancyDomain : byte
{
    ObjectMeshes,
    PreparedMeshCpu,
    MeshStaging,
    GlobalMeshArena,
    CompositeTextures,
    StandaloneTextures,
    PreparedPackage,
    Animations,
    Audio,
    AlphaScratch,
}

internal enum TenancyPriority : byte
{
    Speculative,
    Far,
    Near,
    Visible,
    DestinationCritical,
}

internal enum AssetTenancyPhase : byte
{
    Absent,
    Requested,
    Prepared,
    UploadPending,
    Resident,
    Retiring,
    Cancelled,
    Missing,
    Corrupt,
    Failed,
}

internal enum TenancyOwnerKind : byte
{
    Session,
    Landblock,
    Entity,
    ParticleEmitter,
    UserInterface,
    Tooling,
}

internal readonly record struct TenancyAssetKey(
    TenancyDomain Domain,
    ulong ContentKey,
    ulong Variant = 0);

internal readonly record struct AssetHnd<TAsset>(uint Index, ushort Generation)
{
    public bool IsValid => Index is not 0 && Generation is not 0;

    internal AssetRef Untyped =>
        new(Index, Generation, typeof(TAsset).TypeHandle);
}

internal readonly record struct HolderTicket(uint Index, ushort Generation)
{
    public bool IsValid => Index is not 0 && Generation is not 0;
}

internal readonly record struct AssetTenancy<TAsset>(
    AssetHnd<TAsset> Handle,
    HolderTicket Owner);

internal readonly record struct AssetRef(
    uint Index,
    ushort Generation,
    RuntimeTypeHandle AssetType)
{
    public bool IsValid
    {
        get
        {
            return Index is not 0
        && Generation is not 0
        && !AssetType.Equals(default(RuntimeTypeHandle));
        }
    }
}

internal readonly record struct TenancyCharges(
    long LogicalBytes = 0,
    long CpuPreparedBytes = 0,
    long DecodedBytes = 0,
    long ScratchBytes = 0,
    long PinnedBytes = 0,
    long StagingBytes = 0,
    long GpuRequestedBytes = 0,
    long GpuResidentBytes = 0,
    long RetiringBytes = 0,
    long MappedVirtualBytes = 0)
{
    public static TenancyCharges Zero => default;

    public long CommittedCpuBytes
    {
        get
        {
            return checked(
        LogicalBytes
        + CpuPreparedBytes
        + DecodedBytes
        + ScratchBytes
        + StagingBytes);
        }
    }

    public long PhysicalGpuBytes
    {
        get
        {
            return checked(
        GpuRequestedBytes
        + GpuResidentBytes
        + RetiringBytes);
        }
    }

    public bool IsZero => this == default;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(LogicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(CpuPreparedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(DecodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(ScratchBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(PinnedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(StagingBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(GpuRequestedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(GpuResidentBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(RetiringBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MappedVirtualBytes);
    }

    public static TenancyCharges operator +(
        TenancyCharges left,
        TenancyCharges right) =>
        new(
            checked(left.LogicalBytes + right.LogicalBytes),
            checked(left.CpuPreparedBytes + right.CpuPreparedBytes),
            checked(left.DecodedBytes + right.DecodedBytes),
            checked(left.ScratchBytes + right.ScratchBytes),
            checked(left.PinnedBytes + right.PinnedBytes),
            checked(left.StagingBytes + right.StagingBytes),
            checked(left.GpuRequestedBytes + right.GpuRequestedBytes),
            checked(left.GpuResidentBytes + right.GpuResidentBytes),
            checked(left.RetiringBytes + right.RetiringBytes),
            checked(left.MappedVirtualBytes + right.MappedVirtualBytes));
}

internal readonly record struct TenancyEntryCapture(
    AssetRef Asset,
    TenancyAssetKey Key,
    AssetTenancyPhase State,
    TenancyPriority Priority,
    ulong WorldGeneration,
    long LastUsedFrame,
    long RebuildCost,
    int OwnerCount,
    TenancyCharges Charges,
    string? Failure);

internal readonly record struct TenancyTrimAsk(
    AssetRef Asset,
    TenancyAssetKey Key,
    TenancyCharges Charges);

internal readonly record struct TenancyDomainCapture(
    TenancyDomain Domain,
    int EntryCount,
    int OwnerCount,
    TenancyCharges Charges,
    long BudgetBytes = 0,
    long CapacityBytes = 0,
    long UsedBytes = 0,
    long LargestFreeBytes = 0,
    long Hits = 0,
    long Misses = 0,
    long Evictions = 0)
{
    public long FreeBytes => Math.Max(0, CapacityBytes - UsedBytes);

    public long FragmentedFreeBytes =>
        Math.Max(0, FreeBytes - LargestFreeBytes);

    public void Validate()
    {
        if (!Enum.IsDefined(Domain))
            throw new ArgumentOutOfRangeException(nameof(Domain));
        ArgumentOutOfRangeException.ThrowIfNegative(EntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(OwnerCount);
        Charges.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(BudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(CapacityBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(UsedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(LargestFreeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(Hits);
        ArgumentOutOfRangeException.ThrowIfNegative(Misses);
        ArgumentOutOfRangeException.ThrowIfNegative(Evictions);
        if (UsedBytes > CapacityBytes && CapacityBytes is not 0)
        {
            throw new InvalidOperationException(
                $"{Domain} residency uses {UsedBytes} bytes from "
                + $"{CapacityBytes} bytes of physical capacity");
        }
        if (LargestFreeBytes > CapacityBytes)
        {
            throw new InvalidOperationException(
                $"{Domain} largest free range exceeds physical capacity");
        }
    }
}

internal readonly record struct TenancyCapture(
    IReadOnlyList<TenancyDomainCapture> Domains,
    TenancyCharges TotalCharges)
{
    public TenancyDomainCapture Get(TenancyDomain domain)
    {
        return Domains?.FirstOrDefault(capture => capture.Domain == domain)
        ?? default;
    }

    public int TotalEntries =>
        Domains?.Sum(static capture => capture.EntryCount) ?? 0;

    public int TotalOwners =>
        Domains?.Sum(static capture => capture.OwnerCount) ?? 0;

    public long TotalBudgetBytes =>
        Domains?.Sum(static capture => capture.BudgetBytes) ?? 0;

    public long TotalCapacityBytes =>
        Domains?.Sum(static capture => capture.CapacityBytes) ?? 0;

    public long TotalUsedBytes =>
        Domains?.Sum(static capture => capture.UsedBytes) ?? 0;

    public long TotalFragmentedFreeBytes =>
        Domains?.Sum(static capture => capture.FragmentedFreeBytes) ?? 0;

    public long TotalHits =>
        Domains?.Sum(static capture => capture.Hits) ?? 0;

    public long TotalMisses =>
        Domains?.Sum(static capture => capture.Misses) ?? 0;

    public long TotalEvictions =>
        Domains?.Sum(static capture => capture.Evictions) ?? 0;
}

internal interface ITenancyDomainSource
{
    TenancyDomain Domain { get; }

    TenancyDomainCapture GrabResidency();
}

internal enum TenancyObservationKind : byte
{
    Transition,
    Touch,
}

internal readonly record struct TenancyObservation(
    AssetRef Asset,
    TenancyObservationKind Kind,
    AssetTenancyPhase State,
    TenancyCharges Charges,
    TenancyPriority Priority,
    long Frame,
    string? Failure = null);

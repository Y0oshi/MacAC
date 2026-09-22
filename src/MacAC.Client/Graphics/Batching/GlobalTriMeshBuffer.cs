using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Batching;

internal sealed record GlobalTriMeshAlloc(
    TriMeshBufferSpan Vertices,
    TriMeshBufferSpan Indices,
    IReadOnlyList<int> BatchFirstIndices);

internal readonly record struct GlobalTriMeshPushScheme(
    long UploadBytes,
    long AllocationBytes,
    long CopyBytes,
    int NewBufferCount);

internal readonly record struct GlobalTriMeshMaintenanceHop(
    long AllocationBytes,
    long CopyBytes,
    int NewBufferCount,
    bool Completed);

internal sealed class GlobalTriMeshMoveCancelTicket
{
    private readonly RetryableGpuAssetFree _free;

    public GlobalTriMeshMoveCancelTicket(
        IClientGpuBuffer buf,
        long capOctets,
        RetryableGpuAssetFree release)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentOutOfRangeException.ThrowIfNegative(capOctets);
        Buffer = buf;
        CapOctets = capOctets;
        _free = release ?? throw new ArgumentNullException(nameof(release));
    }

    public IClientGpuBuffer Buffer { get; }
    public long CapOctets { get; }
    public bool IsDone => _free.IsComplete;

    public void Advance() => _free.Run();
}

internal enum GlobalTriMeshCapOutcome
{
    Ready,
    MigrationStarted,
    MigrationInProgress,
    NeedsReclamation,
}

public sealed partial class GlobalTriMeshBuffer : IDisposable
{
    internal const int StartingVertCap = 1024 * 1024;

    internal const int StartingOrdinalCap = 3 * 1024 * 1024;

    internal const int VertGrowthQuantum = 256 * 1024;

    internal const int OrdinalGrowthQuantum = 1024 * 1024;

    internal const long CeilingVertBufOctets = 384L * 1024 * 1024;

    internal const long CeilingOrdinalBufOctets = 128L * 1024 * 1024;

    internal const long CeilingPhysicalArenaOctets = 896L * 1024 * 1024;

    internal static readonly int CeilingVertCap = checked(
        (int)(CeilingVertBufOctets / VertLocusNormBitmap.Size));

    internal const int CeilingOrdinalCap =
        (int)(CeilingOrdinalBufOctets / sizeof(ushort));

    private readonly IClientGpuDevice _device;

    private readonly GpuSunsetRegister _sunsetRegister;

    private readonly GpuRetiredRangeAllotter _verts;

    private readonly GpuRetiredRangeAllotter _ordinals;
    private BufferMove? _migration;

    private GlobalTriMeshMoveCancelTicket? _migrationCancel;
    private int _vaultGen;

    private bool _destroyed;

    private RetryableAssetFreeRegister? _teardownAssetList;

    private enum BufferFlavor
    {
        Vertices,
        Indices,
    }

    private sealed record BufferMove(
        BufferFlavor Kind,
        IClientGpuBuffer OldBuffer,
        IClientGpuBuffer NewBuffer,
        int OldCapacity,
        int NewCapacity,
        long OldCapacityBytes,
        long NewCapacityBytes,
        long CopyBytes)
    {
        public long CopiedOctets { get; set; }
    }

    internal GlobalTriMeshBuffer(IClientGpuDevice device, IGpuAssetSunsetFifo sunset)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(sunset);
        _sunsetRegister = new GpuSunsetRegister(sunset);
        _verts = new GpuRetiredRangeAllotter(StartingVertCap, sunset); // ~32 MB
        _ordinals = new GpuRetiredRangeAllotter(StartingOrdinalCap, sunset);   // ~6 MB
        PrimeBufs();
    }
}

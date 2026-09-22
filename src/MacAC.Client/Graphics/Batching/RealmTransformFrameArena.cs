using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Batching;

internal readonly record struct RealmTransformFrameSlice(
    long FrameSerial,
    IClientGpuBuffer Buffer,
    uint BaseOffsetBytes,
    uint BindingSizeBytes,
    uint FirstInstance,
    uint InstanceCount)
{
    internal bool IsValidFor(IGpuCycle cycle)
    {
        return FrameSerial == cycle.SerialNo
        && Buffer is not null
        && BindingSizeBytes >= checked(
            (FirstInstance + InstanceCount) * RealmTransformCapRule.MatrixOctets)
        && BindingSizeBytes % RealmTransformCapRule.MatrixOctets is 0u
        && BaseOffsetBytes % RealmTransformCapRule.MatrixOctets is 0u
        && checked((long)BaseOffsetBytes + BindingSizeBytes) <= Buffer.SizeBytes;
    }
}

internal static class RealmTransformCapRule
{
    internal const uint MatrixOctets = 64u;
    internal const uint ConnectedDenseBootstrapInsts = 68_395u;
    internal const uint AllocQuantumOctets = 64u * 1024u;
    internal const uint StartingMappingByteSize =
        ((ConnectedDenseBootstrapInsts * MatrixOctets
            + AllocQuantumOctets - 1u) / AllocQuantumOctets)
        * AllocQuantumOctets;
    internal const uint VulkanGuaranteedUpperDepotBufSpanOctets =
        128u * 1024u * 1024u;

    internal static uint LocateMappingByteSize(
        uint neededInsts,
        uint upperDepotBufSpanOctets)
    {
        uint ceiling = upperDepotBufSpanOctets
            - (upperDepotBufSpanOctets % MatrixOctets);
        ulong neededOctets = (ulong)neededInsts * MatrixOctets;
        if (ceiling < MatrixOctets || neededOctets > ceiling)
        {
            throw new NotSupportedException(
                $"The enhanced frame needs {neededInsts:N0} world matrices "
                + $"({neededOctets:N0} bytes), but this adapter exposes only "
                + $"{ceiling:N0} bytes through one storage-buffer binding. "
                + "The pack will fail safe rather than split the authoritative pose buffer");
        }

        ulong markOctets = Math.Max(
            neededOctets,
            (ulong)ConnectedDenseBootstrapInsts * MatrixOctets);
        ulong growthOctets = checked(
            ((markOctets + AllocQuantumOctets - 1u)
                / AllocQuantumOctets)
            * AllocQuantumOctets);

        return (uint)Math.Min(growthOctets, ceiling);
    }

    internal static void VetMappingByteSize(
        uint bindingSizeBytes,
        uint neededInsts,
        uint upperDepotBufSpanOctets)
    {
        ulong neededOctets = (ulong)neededInsts * MatrixOctets;
        if (bindingSizeBytes < neededOctets
            || bindingSizeBytes % MatrixOctets is not 0u
            || bindingSizeBytes > upperDepotBufSpanOctets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bindingSizeBytes),
                bindingSizeBytes,
                $"A shared world-transform binding has to be matrix-aligned, contain "
                + $"all {neededInsts:N0} matrices, and not exceed the adapter's "
                + $"{upperDepotBufSpanOctets:N0}-byte storage range");
        }
    }
}

// Frame-local address allocator over the pack-on N.5 transform block
internal sealed class RealmTransformFrameArena
{
    private RealmTransformFrameSlice _allocation;
    private uint _consumedOctets;

    internal bool IsEngaged => _allocation.Buffer is not null;

    internal uint ConsumedInsts => _consumedOctets / 64u;

    internal RealmTransformFrameSlice Begin(
        IGpuCycle cycle,
        ReadOnlySpan<Matrix4x4> xforms)
    {
        return Begin(
            cycle,
            xforms,
            RealmTransformCapRule.LocateMappingByteSize(
                checked((uint)xforms.Length),
                RealmTransformCapRule.VulkanGuaranteedUpperDepotBufSpanOctets));
    }

    internal RealmTransformFrameSlice Begin(
        IGpuCycle cycle,
        ReadOnlySpan<Matrix4x4> xforms,
        uint bindingSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        RestartIfStale(cycle.SerialNo);
        if (IsEngaged)
        {
            throw new InvalidOperationException(
                "The directional-shadow transform frame was by now published");
        }

        uint byteTally = checked((uint)(xforms.Length * RealmTransformCapRule.MatrixOctets));
        if (bindingSizeBytes < byteTally
            || bindingSizeBytes % RealmTransformCapRule.MatrixOctets is not 0u)
            throw new ArgumentOutOfRangeException(nameof(bindingSizeBytes));

        var alloc = cycle.ReserveLoop(
            checked((int)bindingSizeBytes),
            GpuLoopPurpose.Storage);
        if (!xforms.IsEmpty)
            MemoryMarshal.AsBytes(xforms).CopyTo(alloc.Data);

        _allocation = new RealmTransformFrameSlice(
            cycle.SerialNo,
            alloc.Buffer,
            alloc.ShiftOctets,
            bindingSizeBytes,
            FirstInstance: 0,
            checked((uint)xforms.Length));
        _consumedOctets = byteTally;
        return _allocation;
    }

    internal RealmTransformFrameSlice CommenceKept(
        IGpuCycle cycle,
        in RealmTransformFrameSlice shadowPrefix)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        RestartIfStale(cycle.SerialNo);
        if (IsEngaged)
        {
            throw new InvalidOperationException(
                "The directional-shadow transform frame was by now published");
        }
        if (!shadowPrefix.IsValidFor(cycle)
            || shadowPrefix.FirstInstance is not 0
            || shadowPrefix.Buffer.Residency != GpuMemoryTenancy.HostWritable
            || !shadowPrefix.Buffer.HostWritesAreCoherent
            || !shadowPrefix.Buffer.Usage.HasFlag(GpuBufferPurpose.Storage))
        {
            throw new ArgumentException(
                "The retained shadow prefix has to be this frame's host-writable "
                + "matrix-aligned storage arena at base instance zero",
                nameof(shadowPrefix));
        }

        uint byteTally = checked(
            shadowPrefix.InstanceCount * RealmTransformCapRule.MatrixOctets);
        if (byteTally > shadowPrefix.BindingSizeBytes)
        {
            throw new ArgumentException(
                "The retained shadow prefix exceeds its storage binding",
                nameof(shadowPrefix));
        }

        _allocation = shadowPrefix;
        _consumedOctets = byteTally;
        return _allocation;
    }

    internal RealmTransformFrameSlice Affix(
        IGpuCycle cycle,
        ReadOnlySpan<Matrix4x4> xforms)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        RestartIfStale(cycle.SerialNo);
        if (!IsEngaged)
        {
            throw new InvalidOperationException(
                "The shared world-transform frame hasn't been published");
        }

        uint byteTally = checked(
            (uint)(xforms.Length * RealmTransformCapRule.MatrixOctets));
        uint begin = _consumedOctets;
        uint finish = checked(begin + byteTally);
        if (finish > _allocation.BindingSizeBytes)
        {
            throw new InvalidOperationException(
                $"The enhanced frame needs {finish / RealmTransformCapRule.MatrixOctets:N0} world matrices; "
                + $"this frame's shared transform binding contains "
                + $"{_allocation.BindingSizeBytes / RealmTransformCapRule.MatrixOctets:N0}. "
                + "The pack will fail safe rather than bind a second pose buffer");
        }

        if (!xforms.IsEmpty)
        {
            _allocation.Buffer.Upload(
                checked((long)_allocation.BaseOffsetBytes + begin),
                MemoryMarshal.AsBytes(xforms));
        }
        _consumedOctets = finish;
        return _allocation with
        {
            FirstInstance = begin / RealmTransformCapRule.MatrixOctets,
            InstanceCount = checked((uint)xforms.Length),
        };
    }

    internal bool IsEngagedFor(long cycleSerialNo) =>
        IsEngaged && _allocation.FrameSerial == cycleSerialNo;

    internal void Cancel(IGpuCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (IsEngagedFor(cycle.SerialNo))
            Reset();
    }

    internal void RestartIfStale(long cycleSerialNo)
    {
        if (IsEngaged && _allocation.FrameSerial != cycleSerialNo)
            Reset();
    }

    internal void Reset()
    {
        _allocation = default;
        _consumedOctets = 0;
    }
}

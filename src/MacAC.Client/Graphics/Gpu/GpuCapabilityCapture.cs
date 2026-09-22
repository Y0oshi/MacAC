namespace MacAC.Client.Graphics.Gpu;

internal sealed record GpuCapabilityCapture
{
    public required GpuBackendFlavor Backend { get; init; }

    // Adapter name, e.g. "AMD Radeon RX 9070 XT".
    public required string DeviceName { get; init; }

    // Driver identification string for diagnostics and bug reports
    public required string DriverInfo { get; init; }

    // API version actually in use, e.g. "OpenGL 4.6" or "Vulkan 1.3.280".
    public required string ApiVer { get; init; }

    public required uint UpperTextureChartSockets { get; init; }

    public required uint UpperDepotBufMappings { get; init; }

    public required uint UpperPushConstantBytes { get; init; }

    // Required alignment for a storage-buffer binding offset
    public required uint LowerDepotBufShiftAlignment { get; init; }

    public uint UpperDepotBufSpanOctets { get; init; } = 128u * 1024u * 1024u;

    // Required alignment for a uniform-buffer binding offset
    public required uint LowerUniformBufShiftAlignment { get; init; }

    public required uint UpperClipGaps { get; init; }

    public required uint UpperSpecimenTally { get; init; }

    // Largest supported two-dimensional image edge from the selected adapter
    public required uint UpperImageDimension2D { get; init; }

    public required uint UpperImageArrStrata { get; init; }

    // Total bytes in device-local heaps on the selected adapter
    public required ulong DevOwnMemoryOctets { get; init; }

    // Multi-draw-indirect
    public required bool SupportsMultiPaintIndirect { get; init; }

    // Shader draw parameters (gl_DrawID)
    public required bool SupportsPaintParams { get; init; }

    // BC1/2/3 sampling
    public required bool SupportsTextureCompressionBc { get; init; }

    // GPU timestamps
    public required bool SupportsStampAsks { get; init; }

    public required bool SupportsPersistentlyMappedRings { get; init; }

    public required bool SupportsRgba16FloatRasterizeMarks { get; init; }

    public required uint UpperRgba16FloatSpecimenTally { get; init; }

    public required bool SupportsSampledZDepth { get; init; }

    public required bool SupportsMultiview { get; init; }

    public IReadOnlyList<string> SupportFailures
    {
        get
        {
            List<string> misses = [];

            if (!SupportsMultiPaintIndirect)
                misses.Add("Multi-draw-indirect is required to submit world geometry.");
            if (!SupportsPaintParams)
                misses.Add("Shader draw parameters (gl_DrawID) are required to select per-draw batch data.");
            if (!SupportsTextureCompressionBc)
                misses.Add("BC (DXT) texture compression is required to upload DAT surfaces.");
            if (UpperTextureChartSockets < GpuBindingModel.TextureTableCapacity)
            {
                misses.Add(
                    $"The texture table needs {GpuBindingModel.TextureTableCapacity} slots; " +
                    $"this device provides {UpperTextureChartSockets}.");
            }

            if (UpperDepotBufMappings < GpuBindingModel.DepotMappingTally)
            {
                misses.Add(
                    $"{GpuBindingModel.DepotMappingTally} storage-buffer bindings are required; " +
                    $"this device provides {UpperDepotBufMappings}.");
            }

            if (UpperPushConstantBytes < GpuBindingModel.PushConstantOctets)
            {
                misses.Add(
                    $"{GpuBindingModel.PushConstantOctets} push-constant bytes are required; " +
                    $"this device provides {UpperPushConstantBytes}.");
            }

            if (UpperClipGaps < GpuBindingModel.ClipPlanesPerSocket)
            {
                misses.Add(
                    $"{GpuBindingModel.ClipPlanesPerSocket} clip distances are required by the per-cell clip gate; " +
                    $"this device provides {UpperClipGaps}.");
            }

            return misses;
        }
    }

    public bool IsSupported => SupportFailures.Count is 0;
}

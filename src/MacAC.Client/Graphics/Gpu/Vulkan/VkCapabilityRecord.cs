using System.Text.Json;
using System.Text.Json.Serialization;
using MacAC.Client.Machine;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed record VkDeviceFeatureSupport
{

    // The three MDI dispatch sites are the entire draw architecture
    public required bool MultiDrawIndirect { get; init; }

    public required bool DrawIndirectFirstInstance { get; init; }

    public required bool ShaderClipDistance { get; init; }

    // DXT1/3/5 DAT surfaces upload as BC1/2/3 with no transcode
    public required bool TextureCompressionBc { get; init; }

    // Sampler-quality parity with the GL path
    public required bool SamplerAnisotropy { get; init; }

    // gl_DrawID
    public required bool ShaderDrawParameters { get; init; }

    public required bool Multiview { get; init; }

    // One monotonic serial replaces the GL fence array; the retirement ledger keeps its keys
    public required bool TimelineSemaphore { get; init; }

    public required bool HostQueryReset { get; init; }

    // The global texture table is a runtime-sized descriptor array
    public required bool RuntimeDescriptorArray { get; init; }

    // Unregistered table slots are legitimately absent rather than an error
    public required bool DescriptorBindingPartiallyBound { get; init; }

    // Texture registration appends a descriptor write without rebuilding the set
    public required bool DescriptorBindingSampledImageUpdateAfterBind { get; init; }

    public required bool DescriptorBindingUpdateUnusedWhilePending { get; init; }

    public required bool DescriptorBindingVariableDescriptorCount { get; init; }

    // nonuniformEXT in the fragment shaders
    public required bool ShaderSampledImageArrayNonUniformIndexing { get; init; }

    // No render-pass or framebuffer objects anywhere in the frame
    public required bool DynamicRendering { get; init; }

    // Every barrier in the frame skeleton is a vkCmdPipelineBarrier2
    public required bool Synchronization2 { get; init; }

    // Relaxed shader interface rules for the dual-legal GLSL sources
    public required bool Maintenance4 { get; init; }

    public required bool ShaderDemoteToHelperInvocation { get; init; }

    // Every feature present
    internal static VkDeviceFeatureSupport Complete { get; } = new()
    {
        MultiDrawIndirect = true,
        DrawIndirectFirstInstance = true,
        ShaderClipDistance = true,
        TextureCompressionBc = true,
        SamplerAnisotropy = true,
        ShaderDrawParameters = true,
        Multiview = true,
        TimelineSemaphore = true,
        HostQueryReset = true,
        RuntimeDescriptorArray = true,
        DescriptorBindingPartiallyBound = true,
        DescriptorBindingSampledImageUpdateAfterBind = true,
        DescriptorBindingUpdateUnusedWhilePending = true,
        DescriptorBindingVariableDescriptorCount = true,
        ShaderSampledImageArrayNonUniformIndexing = true,
        DynamicRendering = true,
        Synchronization2 = true,
        Maintenance4 = true,
        ShaderDemoteToHelperInvocation = true,
    };

    internal VkDeviceFeatureSupport? Without(string featureLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureLabel);
        return featureLabel.Trim() switch
        {
            var num when Is(num, nameof(MultiDrawIndirect)) => this with { MultiDrawIndirect = false },
            var num when Is(num, nameof(DrawIndirectFirstInstance)) => this with { DrawIndirectFirstInstance = false },
            var num when Is(num, nameof(ShaderClipDistance)) => this with { ShaderClipDistance = false },
            var num when Is(num, nameof(TextureCompressionBc)) => this with { TextureCompressionBc = false },
            var num when Is(num, nameof(SamplerAnisotropy)) => this with { SamplerAnisotropy = false },
            var num when Is(num, nameof(ShaderDrawParameters)) => this with { ShaderDrawParameters = false },
            var num when Is(num, nameof(Multiview)) => this with { Multiview = false },
            var num when Is(num, nameof(TimelineSemaphore)) => this with { TimelineSemaphore = false },
            var num when Is(num, nameof(HostQueryReset)) => this with { HostQueryReset = false },
            var num when Is(num, nameof(RuntimeDescriptorArray)) => this with { RuntimeDescriptorArray = false },
            var num when Is(num, nameof(DescriptorBindingPartiallyBound)) => this with { DescriptorBindingPartiallyBound = false },
            var num when Is(num, nameof(DescriptorBindingSampledImageUpdateAfterBind)) => this with { DescriptorBindingSampledImageUpdateAfterBind = false },
            var num when Is(num, nameof(DescriptorBindingUpdateUnusedWhilePending)) => this with { DescriptorBindingUpdateUnusedWhilePending = false },
            var num when Is(num, nameof(DescriptorBindingVariableDescriptorCount)) => this with { DescriptorBindingVariableDescriptorCount = false },
            var num when Is(num, nameof(ShaderSampledImageArrayNonUniformIndexing)) => this with { ShaderSampledImageArrayNonUniformIndexing = false },
            var num when Is(num, nameof(DynamicRendering)) => this with { DynamicRendering = false },
            var num when Is(num, nameof(Synchronization2)) => this with { Synchronization2 = false },
            var num when Is(num, nameof(Maintenance4)) => this with { Maintenance4 = false },
            var num when Is(num, nameof(ShaderDemoteToHelperInvocation)) => this with { ShaderDemoteToHelperInvocation = false },
            _ => null,
        };

        static bool Is(string contender, string label)
            => string.Equals(contender, label, StringComparison.OrdinalIgnoreCase);
    }
}

// The device limits the plan asserts up front rather than discovering at draw time (plan §4.1,
// §3.4)
internal sealed record VkDeviceLimitSupport
{
    public required uint MaxPushConstantsSize { get; init; }

    public required uint MaxClipDistances { get; init; }

    // Sets 0, 1 and 2 are all bound simultaneously, so at least 3
    public required uint MaxBoundDescriptorSets { get; init; }

    public required uint MaxDescriptorSetStorageBuffersDynamic { get; init; }

    // Must reach every storage binding declared by descriptor set 0
    public required uint MaxDescriptorSetStorageBuffers { get; init; }

    public required uint MaxPerStageDescriptorStorageBuffers { get; init; }

    // Must reach the number of dynamic uniform bindings set 1 declares
    public required uint MaxDescriptorSetUniformBuffersDynamic { get; init; }

    public required uint MaxDescriptorSetUpdateAfterBindSampledImages { get; init; }

    public required uint MaxPerStageDescriptorUpdateAfterBindSampledImages { get; init; }

    // The frame profiler's GPU timings need graphics-queue timestamps
    public required bool TimestampComputeAndGraphics { get; init; }

    // Ring allocations must satisfy this; getting it wrong is a driver error on Vulkan
    public required uint MinStorageBufferOffsetAlignment { get; init; }

    public required uint MaxStorageBufferRange { get; init; }

    // As above, for the SceneLighting uniform block
    public required uint MinUniformBufferOffsetAlignment { get; init; }

    public required uint MaxImageDimension2D { get; init; }

    public required uint MaxImageArrayLayers { get; init; }

    // Sum of device-local heap bytes reported by the selected physical device
    public required ulong DeviceLocalHeapBytes { get; init; }

    public required uint MaxColorSampleCount { get; init; }

    // A profile that satisfies every requirement, used as the base for tests and for the
    // forced-unsupported knob
    internal static VkDeviceLimitSupport Complete { get; } = new()
    {
        MaxPushConstantsSize = GpuBindingModel.UpperPushConstantOctets,
        MaxClipDistances = GpuBindingModel.ClipPlanesPerSocket,
        MaxBoundDescriptorSets = 4,
        MaxDescriptorSetStorageBuffersDynamic = 4,
        MaxDescriptorSetStorageBuffers = GpuBindingModel.DepotMappingTally,
        MaxPerStageDescriptorStorageBuffers = GpuBindingModel.DepotMappingTally,
        MaxDescriptorSetUniformBuffersDynamic = 8,
        MaxDescriptorSetUpdateAfterBindSampledImages = GpuBindingModel.TextureTableCapacity,
        MaxPerStageDescriptorUpdateAfterBindSampledImages = GpuBindingModel.TextureTableCapacity,
        TimestampComputeAndGraphics = true,
        MinStorageBufferOffsetAlignment = 256,
        MaxStorageBufferRange = 128u * 1024u * 1024u,
        MinUniformBufferOffsetAlignment = 256,
        MaxImageDimension2D = 16384,
        MaxImageArrayLayers = 2048,
        DeviceLocalHeapBytes = 8UL * 1024 * 1024 * 1024,
        MaxColorSampleCount = 8,
    };
}

internal sealed record VkFormatSupport
{
    public required bool SwapchainUnormFormat { get; init; }

    public required Format DepthStencilFormat { get; init; }

    public required bool DepthStencilSampled { get; init; }

    public required bool Rgba16FloatColorAttachment { get; init; }

    // RGBA16F supports optimal-tiling sampled-image reads
    public required bool Rgba16FloatSampled { get; init; }

    // RGBA16F supports linear filtering, required by scaled bloom/ray passes
    public required bool Rgba16FloatLinearFilter { get; init; }

    public required uint MaxRgba16FloatSampleCount { get; init; }

    // BC1 (DXT1) sampled-image support with optimal tiling
    public required bool Bc1Sampled { get; init; }

    // BC2 (DXT3) sampled-image support with optimal tiling
    public required bool Bc2Sampled { get; init; }

    // BC3 (DXT5) sampled-image support with optimal tiling
    public required bool Bc3Sampled { get; init; }

    internal static VkFormatSupport Complete { get; } = new()
    {
        SwapchainUnormFormat = true,
        DepthStencilFormat = Format.D32SfloatS8Uint,
        DepthStencilSampled = true,
        Rgba16FloatColorAttachment = true,
        Rgba16FloatSampled = true,
        Rgba16FloatLinearFilter = true,
        MaxRgba16FloatSampleCount = 8,
        Bc1Sampled = true,
        Bc2Sampled = true,
        Bc3Sampled = true,
    };
}

internal sealed record VkSurfaceSupport(
    bool PresentSupported,
    Format SelectedFormat,
    ColorSpaceKHR SelectedColorSpace,
    PresentModeKHR SelectedPresentMode,
    uint SelectedImageCount,
    uint SelectedWidth,
    uint SelectedHeight,
    bool SupportsTransferSource,
    IReadOnlyList<Format> AvailableFormats,
    IReadOnlyList<PresentModeKHR> AvailablePresentModes);

internal sealed record VkPhysicalDeviceCandidate(
    int Index,
    string DeviceName,
    PhysicalDeviceType DeviceType,
    uint ApiVersion,
    uint DriverVersion,
    uint VendorId,
    uint DeviceId,
    ulong DeviceLocalHeapBytes);

internal sealed record VkFunctionProbeResult(
    bool DeviceCreation,
    bool DescriptorIndexingLayout,
    bool PushConstantLayout,
    bool DynamicRenderingClear,
    bool TimelineSemaphoreWait,
    bool HostQueryReset,
    bool OffscreenReadback,
    IReadOnlyList<string> Failures)
{
    internal static VkFunctionProbeResult NotExec { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        ["active Vulkan device probe did not run"]);
}

internal sealed record VkCapabilityRecord(
    DateTimeOffset CapturedAtUtc,
    string RuntimeIdentifier,
    GraphicalHubOperatingSys OperatingSystem,
    GraphicalReadoutProtocol RequestedDisplayProtocol,
    GraphicalReadoutProtocol ActiveDisplayProtocol,
    string InstanceApiVersion,
    string DeviceApiVersion,
    uint DeviceApiVersionPacked,
    string DeviceName,
    string DriverInfo,
    PhysicalDeviceType DeviceType,
    int SelectedDeviceIndex,
    string DeviceSelectionReason,
    string? RequestedDeviceOverride,
    string? ForcedUnsupportedFeature,
    IReadOnlyList<VkPhysicalDeviceCandidate> AvailableDevices,
    IReadOnlyList<string> InstanceExtensions,
    IReadOnlyList<string> DeviceExtensions,
    uint GraphicsQueueFamily,
    uint PresentQueueFamily,
    VkDeviceFeatureSupport Features,
    VkDeviceLimitSupport Limits,
    VkFormatSupport Formats,
    VkSurfaceSupport? Surface,
    VkFunctionProbeResult FunctionProbe,
    IReadOnlyList<string> SupportFailures)
{
    internal bool IsSupported => SupportFailures.Count is 0;

    internal GpuCapabilityCapture ToGpuCapabilityCapture()
    {
        return new()
        {
            Backend = GpuBackendFlavor.Vulkan,
            DeviceName = DeviceName,
            DriverInfo = DriverInfo,
            ApiVer = DeviceApiVersion,
            UpperTextureChartSockets =
            Math.Min(
                Limits.MaxDescriptorSetUpdateAfterBindSampledImages,
                Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages),
            UpperDepotBufMappings = Math.Min(
            Limits.MaxDescriptorSetStorageBuffers,
            Limits.MaxPerStageDescriptorStorageBuffers),
            UpperPushConstantBytes = Limits.MaxPushConstantsSize,
            LowerDepotBufShiftAlignment = Limits.MinStorageBufferOffsetAlignment,
            UpperDepotBufSpanOctets = Limits.MaxStorageBufferRange,
            LowerUniformBufShiftAlignment = Limits.MinUniformBufferOffsetAlignment,
            UpperClipGaps = Limits.MaxClipDistances,
            UpperSpecimenTally = Limits.MaxColorSampleCount,
            UpperImageDimension2D = Limits.MaxImageDimension2D,
            UpperImageArrStrata = Limits.MaxImageArrayLayers,
            DevOwnMemoryOctets = Limits.DeviceLocalHeapBytes,
            SupportsMultiPaintIndirect = Features.MultiDrawIndirect,
            SupportsPaintParams = Features.ShaderDrawParameters,
            SupportsTextureCompressionBc = Features.TextureCompressionBc,
            SupportsStampAsks = Limits.TimestampComputeAndGraphics,
            SupportsMultiview = Features.Multiview,
            SupportsPersistentlyMappedRings = true,
            SupportsRgba16FloatRasterizeMarks =
            Formats.Rgba16FloatColorAttachment
            && Formats.Rgba16FloatSampled
            && Formats.Rgba16FloatLinearFilter
            && Formats.MaxRgba16FloatSampleCount > 0,
            UpperRgba16FloatSpecimenTally =
            Formats.Rgba16FloatColorAttachment
            && Formats.Rgba16FloatSampled
            && Formats.Rgba16FloatLinearFilter
                ? Math.Min(Formats.MaxRgba16FloatSampleCount, Limits.MaxColorSampleCount)
                : 0u,
            SupportsSampledZDepth = Formats.DepthStencilSampled,
        };
    }
}

internal static partial class VkCapabilityRequirements
{
    internal const uint NeededApiMajor = 1;

    internal const uint NeededApiMinor = 3;

    // Sets 0 (storage), 1 (uniform) and 2 (texture table) are bound at once, so maxBoundDescriptorSets
    // must reach 3
    internal const uint VulkanDescriptorSetTally =
        GpuBindingModel.TextureChartSet + 1;

    internal static VkCapabilityRecord Reevaluate(VkCapabilityRecord capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var cleared = capabilities with { SupportFailures = [] };
        return cleared with { SupportFailures = Evaluate(cleared) };
    }

    // Apply MACAC_VULKAN_FORCE_UNSUPPORTED
    internal static VkCapabilityRecord ImposeForcedUnsupported(
        VkCapabilityRecord capabilities,
        string? featureLabel)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (string.IsNullOrWhiteSpace(featureLabel))
            return capabilities;

        var forced = capabilities.Features.Without(featureLabel);
        return forced is null
            ? Reevaluate(
                capabilities with
                {
                    ForcedUnsupportedFeature = featureLabel,
                    FunctionProbe = capabilities.FunctionProbe with
                    {
                        Failures =
                        [
                            .. capabilities.FunctionProbe.Failures,
                            $"MACAC_VULKAN_FORCE_UNSUPPORTED named '{featureLabel}', " +
                            "which is not a required Vulkan feature.",
                        ],
                    },
                })
            : Reevaluate(
            capabilities with
            {
                Features = forced,
                ForcedUnsupportedFeature = featureLabel,
            });
    }
}

// Packed VK_MAKE_API_VERSION arithmetic, kept out of the interop layer so it is testable
internal static class VulkanApiVer
{
    internal static uint Major(uint dense) => (dense >> 22) & 0x7Fu;

    internal static uint Minor(uint dense) => (dense >> 12) & 0x3FFu;

    internal static uint Fix(uint dense) => dense & 0xFFFu;

    internal static uint Make(uint major, uint minor, uint patch)
        => (major << 22) | (minor << 12) | patch;

    internal static string Depict(uint dense)
        => $"Vulkan {Major(dense)}.{Minor(dense)}.{Fix(dense)}";
}

internal static class VkCapabilityWarden
{
    // File name of the Vulkan report, beside the GL one in the diagnostics directory
    internal const string DossierFileLabel = "graphical-capabilities-vulkan.json";

    internal static void HurlIfUnsupported(
        VkCapabilityRecord capabilities,
        string dossierTrail)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.IsSupported)
            throw new NotSupportedException(ComposeUnsupportedMsg(capabilities, dossierTrail));
    }

    internal static string ComposeUnsupportedMsg(
        VkCapabilityRecord capabilities,
        string dossierTrail)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(dossierTrail);
        return
            "macac's Vulkan renderer is unsupported by the selected device.\n" +
            $"Platform: {capabilities.RuntimeIdentifier}, " +
            $"{capabilities.ActiveDisplayProtocol}, " +
            $"{capabilities.DeviceName} ({capabilities.DeviceType}), " +
            $"{capabilities.DeviceApiVersion}, {capabilities.DriverInfo}\n" +
            string.Join(
                "\n",
                capabilities.SupportFailures.Select(miss => $" - {miss}")) +
            $"\nFull capability report: {Path.GetFullPath(dossierTrail)}";
    }
}

internal static class VkCapabilityDigestWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    internal static void Write(string trail, VkCapabilityRecord capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trail);
        ArgumentNullException.ThrowIfNull(capabilities);
        string wholeTrail = Path.GetFullPath(trail);
        string? folder = Path.GetDirectoryName(wholeTrail);
        WriteRest(capabilities, wholeTrail, folder);
    }

    private static void WriteRest(VkCapabilityRecord capabilities, string wholeTrail, string? folder)
    {
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        string temporaryTrail = wholeTrail + ".tmp";
        File.WriteAllText(temporaryTrail, Serialize(capabilities));
        File.Move(temporaryTrail, wholeTrail, overwrite: true);
    }

    // Exposed so the report's shape can be asserted without touching the file system
    internal static string Serialize(VkCapabilityRecord capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return JsonSerializer.Serialize(capabilities, Options);
    }
}

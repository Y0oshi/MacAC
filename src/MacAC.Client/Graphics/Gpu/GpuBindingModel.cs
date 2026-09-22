using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Gpu;

internal static class GpuBindingModel
{

    // Per-instance transforms. std430 InstanceData { mat4 transform; }.
    public const uint DepotInsts = 0;

    public const uint DepotLots = 1;

    public const uint DepotClipZones = 2;

    public const uint DepotClipSockets = 3;

    // A7 Fix B global point/spot light array
    public const uint DepotGlobalLamps = 4;

    // Per-instance indices into the global light array: object submissions use stride 8; EnvCell
    // submissions use stride 47
    public const uint DepotInstLampSets = 5;

    public const uint DepotInstInside = 6;

    public const uint StorageInstanceAlpha = 7;

    public const uint DepotInstPickIllumination = 8;

    public const uint DepotInstSpecificsBucket = 9;

    public const uint DepotMappingTally = 10;

    public const uint UniformTableauIllumination = 1;

    public const uint UniformLandTiling = 3;

    public const uint UniformHeavensParameters = 4;

    // Immutable authored-atmosphere inputs for one enhanced world frame in opt-in render-pack
    // descriptor set 3
    public const uint UniformAtmosphericCycle = 5;

    public const uint UniformDirectedShade = 6;

    public const uint UniformBundlePass = 7;

    // Pack-declared settings in declaration order: sixteen std140 vec4 values (64 scalar slots)
    public const uint UniformBundlePrefs = ShaderAbi.BundlePrefsMapping;

    public const uint UniformSet = 1;

    public const uint RasterizeBundleUniformSet = ShaderAbi.UniformDescriptorSet;

    // Set index of the sampled-texture descriptor array
    public const uint TextureChartSet = 2;

    public const uint TextureChartMapping = 0;

    public const uint TextureTableCapacity = 16384;

    public const int PushConstantOctets = 96;

    public const int UpperPushConstantOctets = 128;

    public const int GpuLotBlobStrideOctets = 16;

    public const int ClipPlanesPerSocket = 8;

    public const int ClipZoneStrideOctets = 16 + (ClipPlanesPerSocket * 16);

    public const int UpperLampsPerObject = 8;

    public const int UpperLampsPerEnvironChamber = 47;
}

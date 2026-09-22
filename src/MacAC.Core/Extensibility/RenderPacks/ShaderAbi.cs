namespace MacAC.Extensibility.RenderPacks;

public static class ShaderAbi
{
    public const int ShaderAbiVer = 2;

    public const int UniformDescriptorSet = 3;

    public const int AtmosphericCycleMapping = 5;

    /// <summary>ABI v1 AtmosphericFrame: seven vec4/mat4 members, 160 bytes.</summary>
    public const int AtmosphericCycleByteSizeV1 = 160;

    public const int AtmosphericCycleByteSizeV2 = 192;

    public const int AtmosphericCycleByteSize = AtmosphericCycleByteSizeV2;

    public const int DirectedShadeMapping = 6;

    public const int DirectedShadeByteSize = 336;

    public const int BundlePassMapping = 7;

    public const int BundlePassByteSize = 64;

    public const int BundlePrefsMapping = 8;

    public const int BundlePrefsByteSize = 256;

    public const int BundleSettingScalarCap = 64;

    public const int SampledTextureDescriptorSet = 2;

    public const int SampledTextureMapping = 0;

    public const int SampledPassFeedCap = 4;

    public const int PushConstantByteSize = 96;

    public const int CeilingShaderAssetOctets = 16 * 1024 * 1024;
}

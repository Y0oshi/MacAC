using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct DirectionalShadeUniforms
{
    internal const int SizeInBytes = 336;

    public readonly Matrix4x4 RealmToClip0;
    public readonly Matrix4x4 RealmToClip1;
    public readonly Matrix4x4 RealmToClip2;
    public readonly Matrix4x4 RealmToClip3;
    public readonly Vector4 DivideFarawayMeters;
    public readonly Vector4 Control;
    public readonly Vector4 BiasMeters;
    public readonly ClientUInt4 TextureAndFlagSet;
    public readonly Vector4 LampDirAndSrc;

    internal DirectionalShadeUniforms(
        Matrix4x4 realmToClip0,
        Matrix4x4 realmToClip1,
        Matrix4x4 realmToClip2,
        Matrix4x4 realmToClip3,
        Vector4 divideFarawayMeters,
        Vector4 control,
        Vector4 biasMeters,
        ClientUInt4 textureAndFlagSet,
        Vector4 lampDirAndSrc)
    {
        RealmToClip0 = realmToClip0;
        RealmToClip1 = realmToClip1;
        RealmToClip2 = realmToClip2;
        RealmToClip3 = realmToClip3;
        DivideFarawayMeters = divideFarawayMeters;
        Control = control;
        BiasMeters = biasMeters;
        TextureAndFlagSet = textureAndFlagSet;
        LampDirAndSrc = lampDirAndSrc;
    }

    internal static DirectionalShadeUniforms Create(
        ReadOnlySpan<DirectionalShadeCascade> cascades,
        in DirectionalShadeEnvironmentLedger surroundings,
        in DirectionalShadeQuality fidelity,
        GpuTextureSlot textureSlot)
    {
        if (cascades.Length != fidelity.CascadeCount)
            throw new ArgumentException("The cascade span must match the selected quality", nameof(cascades));
        if (!textureSlot.IsAssigned)
            throw new ArgumentException("The directional depth array needs an assigned texture slot", nameof(textureSlot));

        Matrix4x4 matrix0 = cascades[0].WorldToShadowClip;
        Matrix4x4 matrix1 = cascades.Length > 1 ? cascades[1].WorldToShadowClip : Matrix4x4.Identity;
        Matrix4x4 matrix2 = cascades.Length > 2 ? cascades[2].WorldToShadowClip : Matrix4x4.Identity;
        Matrix4x4 matrix3 = cascades.Length > 3 ? cascades[3].WorldToShadowClip : Matrix4x4.Identity;
        float split0 = cascades[0].SplitFarMeters;
        float split1 = cascades.Length > 1 ? cascades[1].SplitFarMeters : fidelity.MaximumReachMeters;
        float split2 = cascades.Length > 2 ? cascades[2].SplitFarMeters : fidelity.MaximumReachMeters;
        float split3 = cascades.Length > 3 ? cascades[3].SplitFarMeters : fidelity.MaximumReachMeters;

        var bias = cascades[^1].Bias;
        float netReachMeters = cascades[^1].SplitFarMeters;
        return new DirectionalShadeUniforms(
            matrix0,
            matrix1,
            matrix2,
            matrix3,
            new Vector4(split0, split1, split2, split3),
            new Vector4(
                surroundings.Strength,
                surroundings.SoftnessMultiplier,
                netReachMeters,
                MathF.Max(1f, netReachMeters * 0.02f)),
            new Vector4(
                bias.ConstantDepthMeters,
                bias.SlopeDepthMeters,
                bias.NormalOffsetMeters,
                cascades[0].CasterDepthPaddingMeters),
            new ClientUInt4(
                textureSlot.Index,
                checked((uint)fidelity.CascadeCount),
                checked((uint)fidelity.MapResolution),
                1u | (checked((uint)fidelity.PcfRadiusTexels) << 8)),
            new Vector4(
                surroundings.SurfaceToLightDirection,
                checked((uint)surroundings.SourceKind)));
    }
}

// Four uints with the exact 16-byte std140 uvec4 representation
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct ClientUInt4(uint x, uint y, uint z, uint w)
{
    public readonly uint X = x;
    public readonly uint Y = y;
    public readonly uint Z = z;
    public readonly uint W = w;
}

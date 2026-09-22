using System.Numerics;
using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Gpu;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GpuShoveConstants
{
    public Matrix4x4 LensMirror;

    public int PaintIdentShift;

    // GLSL uLightingMode: 0 = object (plain Lambert + sun), 1 = EnvCell (half-Lambert wrap, no sun)
    public int IlluminationManner;

    // GLSL uRenderPass: 0 = opaque, 1 = translucent
    public int RasterizePass;

    public int LampDiag;

    public uint TextureIndexA;

    public uint TextureOrdinalB;

    public float ParamA;

    // GLSL uParamB
    public float ParameterB;

    // Neutral defaults: identity transform, opaque object lighting, no debug mode
    public static GpuShoveConstants Default
    {
        get
        {
            return new()
            {
                LensMirror = Matrix4x4.Identity,
                PaintIdentShift = 0,
                IlluminationManner = 0,
                RasterizePass = 0,
                LampDiag = 0,
                TextureIndexA = 0,
                TextureOrdinalB = 0,
                ParamA = 0f,
                ParameterB = 0f,
            };
        }
    }
}

using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Graphics.Batching
{
    public static class BitmapFmtExtensions
    {

        public static PushPixelFmt ToPixelFmt(this TexelLayout fmt)
        {
            return fmt switch
            {
                TexelLayout.RGBA8 => PushPixelFmt.Rgba,
                TexelLayout.RGB8 => PushPixelFmt.Rgb,
                TexelLayout.A8 => PushPixelFmt.Red,
                TexelLayout.Rgba32f => PushPixelFmt.Rgba,
                _ => throw new NotSupportedException($"Texture format {fmt} isn't supported"),
            };
        }

        public static PushPixelKind ToPixelKind(this TexelLayout fmt)
        {
            return fmt switch
            {
                TexelLayout.RGBA8 => PushPixelKind.UnsignedByte,
                TexelLayout.RGB8 => PushPixelKind.UnsignedByte,
                TexelLayout.A8 => PushPixelKind.UnsignedByte,
                TexelLayout.Rgba32f => PushPixelKind.Float,
                _ => throw new NotSupportedException($"Texture format {fmt} isn't supported"),
            };
        }
    }
}

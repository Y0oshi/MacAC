namespace MacAC.Mechanics.Surfaces;

/// <summary>Straight RGBA8 pixels, top-down, tightly packed.</summary>
public sealed record UnpackedTexture(byte[] Rgba8, int Width, int Height)
{
    /// <summary>The 1x1 stand-in for anything that could not be decoded.</summary>
    public static readonly UnpackedTexture Magenta = new([0xFF, 0x00, 0xFF, 0xFF], 1, 1);

    public int ByteTally => Rgba8.Length;
}

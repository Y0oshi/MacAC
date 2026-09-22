namespace MacAC.Assets;

/// <summary>Pixel layouts a decoded texture may be handed to the renderer in (GL enum values).</summary>
public enum PushPixelFmt
{
    Rgba = 0x1908,
    /// <summary>GL_RGB.</summary>
    Rgb = 0x1907,
    /// <summary>GL_RED.</summary>
    Red = 0x1903,
}

/// <summary>Component types a decoded texture may be handed to the renderer in (GL enum values).</summary>
public enum PushPixelKind
{
    UnsignedByte = 0x1401,
    /// <summary>GL_FLOAT.</summary>
    Float = 0x1406,
}

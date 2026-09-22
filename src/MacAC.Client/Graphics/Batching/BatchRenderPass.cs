namespace MacAC.Client.Graphics.Batching;

public enum BatchRenderPass
{
    /// <summary>The opaque pass. Only non-transparent objects are rendered.</summary>
    Opaque = 0,

    Transparent = 1,

    SinglePass = 2,
}

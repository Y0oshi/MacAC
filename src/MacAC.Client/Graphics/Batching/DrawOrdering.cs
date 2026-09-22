using MacAC.Dat;

namespace MacAC.Client.Graphics.Batching;

/// <summary>
/// The order a frame issues its draws in. Both directions matter and neither fails loudly: opaque
/// drawn far-to-near shades every pixel several times over and looks identical, transparent drawn
/// near-to-far composites blended surfaces in the wrong order.
///
/// Cull mode leads both orderings because it is raster state, so each change is a pipeline switch.
/// </summary>
internal static class DrawOrdering
{
    /// <summary>
    /// Opaque draws: cull mode, then nearest first, so the depth test rejects the far surfaces
    /// before they are shaded.
    /// </summary>
    public static int ByCullThenNearest(
        FaceCulling leftCull,
        float leftDistance,
        FaceCulling rightCull,
        float rightDistance)
    {
        int cull = ((int)leftCull).CompareTo((int)rightCull);
        return cull is not 0 ? cull : leftDistance.CompareTo(rightDistance);
    }

    /// <summary>
    /// Transparent draws: cull mode, then farthest first, because each blend reads what is already
    /// there and so has to land on everything behind it.
    /// </summary>
    public static int ByCullThenFarthest(
        FaceCulling leftCull,
        float leftDistance,
        FaceCulling rightCull,
        float rightDistance)
    {
        int cull = ((int)leftCull).CompareTo((int)rightCull);
        return cull is not 0 ? cull : rightDistance.CompareTo(leftDistance);
    }
}

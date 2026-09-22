using MacAC.Dat;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics;

public static class CanonMoteGeometryClassifier
{
    public static CanonMoteGeometryKind Classify(uint? leadDowngradeManner)
    {
        return leadDowngradeManner is uint manner && manner is not 1u
                ? CanonMoteGeometryKind.Billboard
                : CanonMoteGeometryKind.FullMesh;
    }
}

public enum CanonMoteGeometryKind
{
    FullMesh,
    Billboard,
}

// Resolves authored particle-surface blending without allowing one malformed Surface entry to
// abort the render frame
internal static class CanonMoteBlendPicker
{
    public static SeeThroughKind Resolve(
        uint canvasIdent,
        bool lotIsAdditive,
        Func<uint, Skin?>? pullCanvas,
        Action<string>? probe = null)
    {
        SeeThroughKind backup = lotIsAdditive
            ? SeeThroughKind.Additive
            : SeeThroughKind.AlphaBlend;
        if (canvasIdent is 0 || pullCanvas is null)
            return backup;

        try
        {
            Skin? canvas = pullCanvas(canvasIdent);
            return canvas is null
                ? backup
                : SeeThroughKindExtensions.FromCanvasKind(canvas.Bits);
        }
        catch (Exception exc)
        {
            probe?.Invoke(
                $"[particle-material] Failed to decode Surface 0x{canvasIdent:X8}: {exc.Message}");
            return backup;
        }
    }
}

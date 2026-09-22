using System.Numerics;

namespace MacAC.Client.Shell;

internal static class WidgetCheckLamp
{
    // The lamp glyph's fixed on-screen size in px (both axes)
    public const float LampDims = 11f;

    public static readonly Vector4 CheckedOuter = new(0.36f, 0.58f, 0.12f, 1f);
    public static readonly Vector4 CheckedInterior = new(0.52f, 1f, 0.08f, 1f);
    public static readonly Vector4 UncheckedOuter = new(0.26f, 0.22f, 0.13f, 1f);
    public static readonly Vector4 UncheckedInterior = new(0.38f, 0.34f, 0.23f, 1f);

    public static void Draw(WidgetRenderScope cx, float x, float y, bool isChecked)
    {
        Vector4 outer = isChecked ? CheckedOuter : UncheckedOuter;
        Vector4 interior = isChecked ? CheckedInterior : UncheckedInterior;
        cx.SketchPopulate(x + 3f, y, 5f, 1f, outer);
        cx.SketchPopulate(x + 1f, y + 1f, 9f, 2f, outer);
        DrawRest(outer, interior, cx, x, y);
    }

    private static void DrawRest(Vector4 outer, Vector4 interior, WidgetRenderScope cx, float x, float y)
    {
        cx.SketchPopulate(x, y + 3f, 11f, 5f, outer);
        cx.SketchPopulate(x + 1f, y + 8f, 9f, 2f, outer);
        cx.SketchPopulate(x + 3f, y + 10f, 5f, 1f, outer);
        cx.SketchPopulate(x + 3f, y + 3f, 5f, 5f, interior);
    }
}

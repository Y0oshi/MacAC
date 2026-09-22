using System.Numerics;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Shell;

public sealed class WidgetViewport : WidgetElem
{
    public override bool ConsumesDatChildren => true;

    public System.Action? Clicked { get; set; }
    public System.Action<int, int>? ClickedAt { get; set; }

    public override bool HndsPress => Clicked is not null || ClickedAt is not null;

    public override bool OnSignal(in WidgetSignal e)
    {
        if (Clicked is null && ClickedAt is null) return base.OnSignal(e);
        switch (e.Type)
        {
            case WidgetEventType.PointerDown: return true;
            case WidgetEventType.Click:
                Clicked?.Invoke();
                ClickedAt?.Invoke(e.Data1, e.Data2);
                return true;
        }
        return base.OnSignal(e);
    }

    public IWidgetViewportPainter? Painter { get; set; }

    internal GpuTextureSlot TextureSlot { get; set; } = GpuTextureSlot.Unassigned;

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (!Visible || !TextureSlot.IsAssigned) return;
        uint textureHnd = MacAC.Client.Graphics.PhrasePainter.LocateExternalTextureSocket(TextureSlot);
        if (textureHnd is 0) return;
        bool bottomUp = Painter?.TextureIsBottomUp ?? true;
        float v0 = bottomUp ? 1f : 0f;
        float v1 = bottomUp ? 0f : 1f;
        cx.SketchSprite(textureHnd, 0f, 0f, Width, Height, 0f, v0, 1f, v1, Vector4.One);
    }
}

using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetPopupTrunk : WidgetBoard
{
    public Action? Cancel { get; set; }

    public WidgetPopupTrunk()
    {
        BackgroundColor = Vector4.Zero;
        BorderTint = Vector4.Zero;
        ClickThrough = false;
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.TagDown
            && e.Data0 == (int)Silk.NET.Input.Key.Escape)

            Cancel?.Invoke();

        return true;
    }
}

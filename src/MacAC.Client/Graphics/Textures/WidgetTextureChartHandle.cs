using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal static class WidgetTextureChartHandle
{
    // No texture. What every widget's tex == 0 guard tests for.
    public const uint None = 0;

    public static uint FromSocket(GpuTextureSlot socket) =>
        socket.IsAssigned ? socket.Index + 1 : None;

    public static GpuTextureSlot ToSocket(uint hnd) =>
        hnd == None ? GpuTextureSlot.Unassigned : new GpuTextureSlot(hnd - 1);
}

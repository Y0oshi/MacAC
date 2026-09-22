namespace MacAC.Client.Graphics.Gpu;

internal interface IGpuPipeFmtVariantHub
{
    IDisposable ObtainPipeTintFmt(GpuBitmapFmt fmt);
}

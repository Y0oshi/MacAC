using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static class AtmosphericGpuTickerSampling
{
    internal const int LoIntervalCycles = 4;

    internal static bool ShouldMeasure(
        RenderQualityRole fidelity,
        long cycleSerialNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycleSerialNo);
        return fidelity is not RenderQualityRole.Low
            || cycleSerialNo % LoIntervalCycles is 0;
    }
}

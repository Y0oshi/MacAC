namespace MacAC.Client.Graphics;

internal static class LandBitmapTilingChart
{
    internal const int StratumCap = 36;

    internal const int UniformElemStrideOctets = 16;

    internal const int UniformBufOctets = StratumCap * UniformElemStrideOctets;

    internal static float[] Build(IEnumerable<(uint Layer, uint RepeatCount)> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);

        float[] chart = new float[StratumCap];
        Array.Fill(chart, 1f);

        foreach (var (stratum, repeatTally) in listings)
        {
            if (stratum >= StratumCap)
            {
                throw new InvalidOperationException(
                    $"Terrain atlas layer {stratum} exceeds the shader capacity of {StratumCap} layers");
            }

            chart[stratum] = repeatTally;
        }

        return chart;
    }
}

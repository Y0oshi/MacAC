namespace MacAC.Mechanics.Sound;

/// <summary>The two random streams the retail sound code draws from.</summary>
public interface IAudioDice
{
    float UpcomingVariantRoll();

    float UpcomingProbabilityRoll();
}

public sealed class AudioDice(Random? rng = null) : IAudioDice
{
    internal const float UpperVariantRoll = 0.99999988f;

    internal const int RandUpper = 32767;

    private readonly Random _rng = rng ?? Random.Shared;

    public float UpcomingVariantRoll() => MathF.Min(UpperVariantRoll, (float)_rng.NextDouble());

    /// <summary>rand() / RAND_MAX, with RAND_MAX itself reachable so the roll can hit exactly 1.0.</summary>
    public float UpcomingProbabilityRoll() => _rng.Next(0, RandUpper + 1) * (1f / RandUpper);
}

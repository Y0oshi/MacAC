using MacAC.Dat;
using DRWSound =  MacAC.Dat.SoundTag;
using DRWSoundEntry =  MacAC.Dat.SoundChoice;

namespace MacAC.Mechanics.Sound;

/// <summary>The retail variant pick and probability gate for SoundTable entries.</summary>
public static class SoundRecipes
{
    public static DRWSoundEntry? ChooseVariant(IReadOnlyList<DRWSoundEntry> listings, IAudioDice rng)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(rng);
        if (listings.Count is 0)
            return null;
        // The retail scale is (count − 1), so the last variant is only reached at the roll's ceiling.
        int choose = (int)(rng.UpcomingVariantRoll() * (listings.Count - 1));
        return choose < listings.Count ? listings[choose] : null;
    }

    public static bool PlayProbability(float probability, IAudioDice rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.UpcomingProbabilityRoll() < probability;
    }

    public static DRWSoundEntry? Select(IReadOnlyList<DRWSoundEntry> listings, IAudioDice rng)
    {
        return ChooseVariant(listings, rng) is { } picked && PlayProbability(picked.Probability, rng) ? picked : null;
    }

    public static DRWSoundEntry? Select(SoundBook chart, DRWSound sfx, IAudioDice rng)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Sounds.TryGetValue(sfx, out var blob) ? Select(blob.Choices, rng) : null;
    }
}

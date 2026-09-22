using System.Numerics;

namespace MacAC.Mechanics.Sound;

/// <summary>Compass sector an ambient source sits in, relative to the listener.</summary>
public enum AmbienceBearing
{
    InViewerBlock = 0,
    North = 1,
    South = 2,
    East = 3,
    West = 4,
    Northwest = 5,
    Southwest = 6,
    Northeast = 7,
    Southeast = 8,
}

/// <summary>One ambient sound as authored in the region's STB table.</summary>
public readonly record struct AmbienceCue(
    SfxId Sound,
    float Volume,
    float BaseChance,
    float MinRate,
    float MaxRate)
{
    /// <summary>A zero base chance means a looping bed, not a random one-shot.</summary>
    public bool IsContinuous => BaseChance == 0f;
}

public readonly record struct AmbienceBearingShell(
    AmbienceBearing Direction,
    float MinDistance,
    float MaxDistance);

/// <summary>The retail ambient-sound constants and the two geometric rules built on them.</summary>
public static class AmbienceTuning
{
    public const float LowerDistance = 20.0f;
    public const float LowerGapSq = 400.0f;
    public const float UpperGap = 120.0f;
    public const float UpperGapSq = 14400.0f;
    public const float LowerVolume = 0.03f;
    public const float BearingSpread = 0.392699093f;
    public const float InBeholderChunkGapSq = LowerGapSq * 0.5f;
    public const float ShellHalfThickness = LowerDistance * 0.5f;

    /// <summary>Near bound of the omnidirectional spread, metres (5.0 − 1.0).</summary>
    public const float InChunkNearbyGap = 4.0f;

    public const float LandChamberLen = 24.0f;

    private const float DiagonalRatio = 2.0f;
    private const float Epsilon = 0.0002f;

    public static float Bearing(AmbienceBearing dir)
    {
        return dir switch
        {
            AmbienceBearing.North => 0.0f,
            AmbienceBearing.South => 3.14159274f,
            AmbienceBearing.East => 1.57079637f,
            AmbienceBearing.West => 4.71238899f,
            AmbienceBearing.Northwest => 5.49778700f,
            AmbienceBearing.Southwest => 3.92699075f,
            AmbienceBearing.Northeast => 0.78539819f,
            AmbienceBearing.Southeast => 2.35619450f,
            _ => 0.0f,   // InViewerBlock and anything unknown
        };
    }

    /// <summary>Full weight inside 20 m, inverse-square out to 120 m, nothing beyond.</summary>
    public static float CalcWeight(Vector3 shift)
    {
        float distanceSq = shift.LengthSquared();
        if (distanceSq > UpperGapSq)
            return 0f;
        return distanceSq < LowerGapSq ? 1f : LowerGapSq / distanceSq;
    }

    public static AmbienceBearing CalcDir(Vector3 shift)
    {
        float x = shift.X;
        float y = shift.Y;
        if (x * x + y * y < InBeholderChunkGapSq)
            return AmbienceBearing.InViewerBlock;

        float ax = MathF.Abs(x);
        float ay = MathF.Abs(y);
        bool diagonal = ax > Epsilon && ay > Epsilon && ay / ax <= DiagonalRatio && ax / ay <= DiagonalRatio;

        if (diagonal)
        {
            return (y >= 0f, x >= 0f) switch
            {
                (true, true) => AmbienceBearing.Northeast,
                (true, false) => AmbienceBearing.Northwest,
                (false, true) => AmbienceBearing.Southeast,
                _ => AmbienceBearing.Southwest,
            };
        }
        if (ay >= ax)
            return y >= 0f ? AmbienceBearing.North : AmbienceBearing.South;
        return x >= 0f ? AmbienceBearing.East : AmbienceBearing.West;
    }
}

public sealed class AmbienceVoice(AmbienceCue descriptor, uint sfxChartDid)
{
    private const int UpperShells = 8;

    private static readonly AmbienceBearing[] AllCompassBearings =
    [
        AmbienceBearing.North, AmbienceBearing.South, AmbienceBearing.East, AmbienceBearing.West,
        AmbienceBearing.Northwest, AmbienceBearing.Southwest, AmbienceBearing.Northeast, AmbienceBearing.Southeast,
    ];

    private readonly List<AmbienceBearingShell> _shells = [];

    public AmbienceCue Descriptor { get; } = descriptor;

    /// <summary>The SoundTable the cue's slot is looked up in.</summary>
    public uint SfxChartDid { get; } = sfxChartDid;

    public float SfxTally { get; private set; }

    public float LatestVolume { get; private set; }

    /// <summary>Per-fire probability; one-shots only.</summary>
    public float PlayChance { get; private set; }

    /// <summary>True while the voice holds a slot in the deadline queue.</summary>
    public bool OnFifo { get; set; }

    public IReadOnlyList<AmbienceBearingShell> Directions => _shells;

    public void RestartTally()
    {
        SfxTally = 0f;
        _shells.Clear();
        if (!Descriptor.IsContinuous)
            PlayChance = 0f;
    }

    public void AppendTo(float weight, Vector3 shift, AmbienceBearing dir)
    {
        SfxTally += weight;
        if (Descriptor.IsContinuous)
            return;

        float half = AmbienceTuning.ShellHalfThickness;
        if (dir != AmbienceBearing.InViewerBlock)
        {
            float gap = MathF.Sqrt(shift.LengthSquared());
            Widen(dir, gap - half, gap + half);
            return;
        }

        // A source in the listener's own block can come from anywhere nearby
        foreach (AmbienceBearing bearing in AllCompassBearings)
            Widen(bearing, AmbienceTuning.InChunkNearbyGap, half);
    }

    public void RefreshSfx(float sumSfxTally)
    {
        if (Descriptor.IsContinuous)
        {
            LatestVolume = SfxTally == 0f ? 0f : Descriptor.Volume / sumSfxTally * SfxTally;
            return;
        }
        if (SfxTally > 0f)
            PlayChance = Descriptor.BaseChance / sumSfxTally * SfxTally;
    }

    public bool CanHear()
    {
        return Descriptor.IsContinuous ? LatestVolume >= AmbienceTuning.LowerVolume : PlayChance > 0f;
    }

    public bool PlayInstant(IAudioDice rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Descriptor.IsContinuous || rng.UpcomingVariantRoll() <= PlayChance;
    }

    public float FetchVolume() => Descriptor.IsContinuous ? LatestVolume : Descriptor.Volume;

    public float FetchPlayInterval(IAudioDice rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Descriptor.IsContinuous ? Descriptor.MinRate : RollDice(Descriptor.MinRate, Descriptor.MaxRate, rng);
    }

    /// <summary>Picks a shell at random, then a bearing and a distance (biased near) inside it.</summary>
    public bool TryFetchSfxLocus(Vector3 listenerLocus, IAudioDice rng, out Vector3 locus)
    {
        ArgumentNullException.ThrowIfNull(rng);
        locus = listenerLocus;
        if (Descriptor.IsContinuous || _shells.Count is 0)
            return false;

        int choose = Math.Min((int)MathF.Floor(rng.UpcomingVariantRoll() * _shells.Count), _shells.Count - 1);
        var shell = _shells[choose];

        float spread = AmbienceTuning.BearingSpread;
        float angle = AmbienceTuning.Bearing(shell.Direction) + rng.UpcomingVariantRoll() * spread - spread * 0.5f;
        float t = rng.UpcomingVariantRoll();
        float gap = shell.MinDistance + (shell.MaxDistance - shell.MinDistance) * t * t;

        locus = new Vector3(
            listenerLocus.X + MathF.Sin(angle) * gap,
            listenerLocus.Y + MathF.Cos(angle) * gap,
            listenerLocus.Z);
        return true;
    }

    internal static float RollDice(float lower, float upper, IAudioDice rng)
    {
        if (lower == upper)
            return lower;
        (float lo, float hi) = upper < lower ? (upper, lower) : (lower, upper);
        return lo + (hi - lo) * rng.UpcomingVariantRoll();
    }

    private void Widen(AmbienceBearing dir, float lower, float upper)
    {
        for (int idx = 0; idx < _shells.Count; ++idx)
        {
            if (_shells[idx].Direction != dir)
                continue;
            var recognized = _shells[idx];
            _shells[idx] = recognized with
            {
                MinDistance = MathF.Min(recognized.MinDistance, lower),
                MaxDistance = MathF.Max(recognized.MaxDistance, upper),
            };
            return;
        }
        if (_shells.Count < UpperShells)
            _shells.Add(new AmbienceBearingShell(dir, lower, upper));
    }
}

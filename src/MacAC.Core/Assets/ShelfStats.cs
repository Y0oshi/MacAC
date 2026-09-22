namespace MacAC.Assets;

public readonly record struct ShelfStats(long Hits, long Misses, long Evictions)
{
    public static ShelfStats operator +(ShelfStats a, ShelfStats b) =>
        new(a.Hits + b.Hits, a.Misses + b.Misses, a.Evictions + b.Evictions);
}

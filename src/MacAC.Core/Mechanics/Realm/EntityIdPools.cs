namespace MacAC.Mechanics.Realm;

internal static class LandblockIdPool
{
    public const uint UpperCounter = 0xFFFu;

    public static uint Base(uint nibble, uint lbX, uint lbY) =>
        nibble | ((lbX & 0xFFu) << 20) | ((lbY & 0xFFu) << 12);

    public static uint Allocate(uint nibble, string clan, uint lbX, uint lbY, ref uint counter)
    {
        if (counter > UpperCounter)
        {
            throw new InvalidDataException(
                $"Landblock ({lbX & 0xFFu:X2},{lbY & 0xFFu:X2}) exceeds the 4096-entry {clan} id namespace");
        }
        return Base(nibble, lbX, lbY) + counter++;
    }
}

public static class InteriorIdPool
{
    private const uint Nibble = 0x40000000u;

    public const uint MaxCounter = LandblockIdPool.UpperCounter;

    public static uint Base(uint lbX, uint lbY) => LandblockIdPool.Base(Nibble, lbX, lbY);

    public static uint Allocate(uint lbX, uint lbY, ref uint counter)
    {
        return LandblockIdPool.Allocate(Nibble, "interior entity", lbX, lbY, ref counter);
    }
}

public static class SceneryIdPool
{
    private const uint Nibble = 0x80000000u;

    public const uint MaxCounter = LandblockIdPool.UpperCounter;

    public static uint Base(uint lbX, uint lbY) => LandblockIdPool.Base(Nibble, lbX, lbY);

    public static uint Allocate(uint lbX, uint lbY, ref uint counter)
    {
        return LandblockIdPool.Allocate(Nibble, "procedural scenery", lbX, lbY, ref counter);
    }

    public static bool IsInNamespace(uint actorIdent) => (actorIdent & 0xF0000000u) == Nibble;
}

public static class LandblockStaticIdPool
{
    private const uint Nibble = 0xC0000000u;

    public const uint MaxCounter = LandblockIdPool.UpperCounter;

    public static uint Base(uint lbX, uint lbY) => LandblockIdPool.Base(Nibble, lbX, lbY);

    public static uint Allocate(uint lbX, uint lbY, ref uint counter)
    {
        return LandblockIdPool.Allocate(Nibble, "static entity", lbX, lbY, ref counter);
    }

    public static bool IsInNamespace(uint actorIdent) => (actorIdent & 0xF0000000u) == Nibble;
}

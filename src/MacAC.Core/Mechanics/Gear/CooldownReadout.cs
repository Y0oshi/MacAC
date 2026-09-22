namespace MacAC.Mechanics.Gear;

/// <summary>Which of the ten cooldown overlay frames an item icon shows.</summary>
public static class CooldownReadout
{
    private const int Hops = 10;

    public static int FetchTopLayerHop(double gearInterval, double leftover)
    {
        if (!(gearInterval > 0d) || !(leftover > 0d))
            return 0;
        double hop = Math.Truncate(leftover / gearInterval * Hops + 1d);
        return !(hop >= 1d) ? 0 : hop >= Hops ? Hops : (int)hop;
    }
}

namespace MacAC.Wire;

/// <summary>What the HUD shows about the link at one instant.</summary>
public readonly record struct LinkStatusFrame(bool Connected, double SecondsSinceLastPacket, double PacketLossPercentage = 0d, double? RoundTripSeconds = null)
{
    public static LinkStatusFrame Disconnected => new(false, 0d);
}

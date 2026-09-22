namespace MacAC.Mechanics.Comms;

public enum ChannelSource
{
    Legacy,
    Turbine,
}

/// <summary>A chat channel the player can speak on, from either chat system.</summary>
public abstract record ChannelFacts(string DisplayName, ChannelSource Source)
{
    /// <summary>Whether the server echoes our own sends back on this channel.</summary>
    public abstract bool IsSelfEchoLane();

    public sealed record Classic(uint ChannelId, string DisplayName)
        : ChannelFacts(DisplayName, ChannelSource.Legacy)
    {
        private const uint Fellow = 0x00000800u;
        private const uint Vassals = 0x00001000u;
        private const uint Patron = 0x00002000u;
        private const uint Monarch = 0x00004000u;
        private const uint CoVassals = 0x01000000u;
        private const uint AllegianceBroadcast = 0x02000000u;

        public override bool IsSelfEchoLane()
        {
            return ChannelId is Fellow or Vassals or Patron or Monarch or CoVassals or AllegianceBroadcast;
        }
    }

    public sealed record MechTurbine(uint RoomId, uint ChatType, uint DispatchType, string DisplayName)
        : ChannelFacts(DisplayName, ChannelSource.Turbine)
    {
        public override bool IsSelfEchoLane() => false;
    }
}

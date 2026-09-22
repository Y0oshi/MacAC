namespace MacAC.Sim.Comms;

public static class ChannelPicker
{
    public readonly record struct Settled(uint ChannelId, string DisplayName);

    public static Settled? Resolve(CommsChannelKind sort)
    {
        return sort switch
        {
            CommsChannelKind.Fellowship => new Settled(0x00000800u, "Fellowship"),
            CommsChannelKind.AllegianceBroadcast => new Settled(0x02000000u, "Allegiance"),
            CommsChannelKind.Vassals => new Settled(0x00001000u, "Vassals"),
            CommsChannelKind.Patron => new Settled(0x00002000u, "Patron"),
            CommsChannelKind.Monarch => new Settled(0x00004000u, "Monarch"),
            CommsChannelKind.CoVassals => new Settled(0x01000000u, "CoVassals"),
            _ => null,
        };
    }
}

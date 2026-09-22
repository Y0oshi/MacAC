namespace MacAC.Mechanics.Comms;

public static class LegacyChannelType
{
    private const uint Abuse = 0x0001u;
    private const uint Help = 0x0400u;
    private const uint Fellowship = 0x0800u;
    private const uint Vassals = 0x1000u;
    private const uint Patron = 0x2000u;
    private const uint Monarch = 0x4000u;
    private const uint CoVassals = 0x1000000u;
    private const uint AllegianceBroadcast = 0x2000000u;
    private const uint FellowshipTransmitAlias = 0x4000000u;

    public static uint Resolve(uint laneIdent, bool ownTransmit)
    {
        CanonLogTextType kind = laneIdent switch
        {
            Abuse => CanonLogTextType.Abuse,
            Help => CanonLogTextType.Help,
            // Same type whether heard or sent.
            Fellowship => CanonLogTextType.Fellowship,
            // "Your patron tells you..." on receipt; "You say to your patron..." on send.
            Vassals or Patron or Monarch => ownTransmit ? CanonLogTextType.SocialSend : CanonLogTextType.Social,
            CoVassals or AllegianceBroadcast => CanonLogTextType.Social,
            FellowshipTransmitAlias => ownTransmit ? CanonLogTextType.Fellowship : CanonLogTextType.Channel,
            _ => ownTransmit ? CanonLogTextType.ChannelSend : CanonLogTextType.Channel,
        };
        return (uint)kind;
    }
}

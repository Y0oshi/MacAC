namespace MacAC.Mechanics.Comms;

public enum ChannelKindLite
{
    Allegiance,
    General,
    Trade,
    Lfg,
    Roleplay,
    Society,
    SocietyCelestialHand,
    SocietyEldrytchWeb,
    SocietyRadiantBlood,
    Olthoi,
}

/// <summary>The Turbine Chat room ids the server assigned us, plus the request cookie counter.</summary>
public sealed class TurbineChatPhase
{
    private readonly uint[] _halls = new uint[Enum.GetValues<ChannelKindLite>().Length];
    private uint _upcomingCookie = 1;

    /// <summary>True once the first SetTurbineChatChannels has arrived.</summary>
    public bool Enabled { get; private set; }

    public uint AllegianceHall => _halls[(int)ChannelKindLite.Allegiance];

    public uint GeneralHall => _halls[(int)ChannelKindLite.General];

    public uint BarterHall => _halls[(int)ChannelKindLite.Trade];

    public uint LfgHall => _halls[(int)ChannelKindLite.Lfg];

    public uint RoleplayHall => _halls[(int)ChannelKindLite.Roleplay];

    public uint OlthoiHall => _halls[(int)ChannelKindLite.Olthoi];

    /// <summary>The top-level Society room; zero when the player has no society.</summary>
    public uint SocietyHall => _halls[(int)ChannelKindLite.Society];

    public uint SocietyCelestialHandHall => _halls[(int)ChannelKindLite.SocietyCelestialHand];

    public uint SocietyEldrytchWebHall => _halls[(int)ChannelKindLite.SocietyEldrytchWeb];

    public uint SocietyRadiantBloodHall => _halls[(int)ChannelKindLite.SocietyRadiantBlood];

    /// <summary>Cookies run 1..uint.Max and wrap back to 1, never 0.</summary>
    public uint UpcomingCtxIdent()
    {
        uint cookie = _upcomingCookie;
        _upcomingCookie = unchecked(_upcomingCookie + 1) is 0 ? 1 : unchecked(_upcomingCookie + 1);
        return cookie;
    }

    public void OnLanesReceived(
        uint allegianceHall,
        uint generalHall,
        uint barterHall,
        uint lfgHall,
        uint roleplayHall,
        uint olthoiHall,
        uint societyHall,
        uint societyCelestialHandHall,
        uint societyEldrytchWebHall,
        uint societyRadiantBloodHall)
    {
        Enabled = true;
        _halls[(int)ChannelKindLite.Allegiance] = allegianceHall;
        _halls[(int)ChannelKindLite.General] = generalHall;
        _halls[(int)ChannelKindLite.Trade] = barterHall;
        _halls[(int)ChannelKindLite.Lfg] = lfgHall;
        _halls[(int)ChannelKindLite.Roleplay] = roleplayHall;
        _halls[(int)ChannelKindLite.Olthoi] = olthoiHall;
        _halls[(int)ChannelKindLite.Society] = societyHall;
        _halls[(int)ChannelKindLite.SocietyCelestialHand] = societyCelestialHandHall;
        _halls[(int)ChannelKindLite.SocietyEldrytchWeb] = societyEldrytchWebHall;
        _halls[(int)ChannelKindLite.SocietyRadiantBlood] = societyRadiantBloodHall;
    }

    public void Reset()
    {
        Enabled = false;
        Array.Clear(_halls);
        _upcomingCookie = 1u;
    }

    public uint HallFor(ChannelKindLite sort) =>
        (uint)sort < (uint)_halls.Length ? _halls[(int)sort] : 0u;
}

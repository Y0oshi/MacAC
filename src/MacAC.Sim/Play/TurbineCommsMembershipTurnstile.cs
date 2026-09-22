using MacAC.Mechanics.Comms;
using MacAC.Wire.Messages;
using MacAC.Sim.Presence;

namespace MacAC.Sim.Play;

public enum TurbineCommsTurnstileStatus
{
    Allowed,

    Unavailable,

    NotListening,
}

public readonly record struct TurbineCommsTurnstileResult(TurbineCommsTurnstileStatus Status, uint RoomId, uint ChatType, string DisplayName);

public static class TurbineCommsMembershipTurnstile
{

    public static TurbineCommsTurnstileResult Evaluate(ChannelKindLite sort, TurbineChatPhase turbineComms, SimToonOptionsLedger knobs, bool isOlthoiAvatar)
    {
        ArgumentNullException.ThrowIfNull(turbineComms);
        ArgumentNullException.ThrowIfNull(knobs);

        (uint hall, uint commsKind) = HallFor(sort, turbineComms);
        string label = TurbineCommsLabels.Resolve(hall, commsKind);

        TurbineCommsTurnstileStatus condition =
            !turbineComms.Enabled || hall is 0u ? TurbineCommsTurnstileStatus.Unavailable
            : Listening(sort, knobs, isOlthoiAvatar) ? TurbineCommsTurnstileStatus.Allowed
            : TurbineCommsTurnstileStatus.NotListening;
        return new TurbineCommsTurnstileResult(condition, hall, commsKind, label);
    }

    /// <summary>The interface text retail shows for a refused send, or null when nothing is shown.</summary>
    public static (string Text, CanonLogTextType Type)? LocateRefusalPhrase(TurbineCommsTurnstileResult latch)
    {
        switch (latch.Status)
        {
            case TurbineCommsTurnstileStatus.Unavailable:
                return (TextRefusals.TurbineCommsUnavailable, CanonLogTextType.Default);
            case TurbineCommsTurnstileStatus.NotListening:
                (string? phrase, CanonLogTextType kind) = WeenieErrorText.Resolve(0x0551u, latch.DisplayName);
                return phrase is not null ? (phrase, kind) : null;
            default:
                return null;
        }
    }
    private static (uint Room, uint ChatType) HallFor(ChannelKindLite sort, TurbineChatPhase comms)
    {
        return sort switch
        {
            ChannelKindLite.Allegiance => (comms.AllegianceHall, (uint)TurbineComms.CommsKind.Allegiance),
            ChannelKindLite.General => (comms.GeneralHall, (uint)TurbineComms.CommsKind.General),
            ChannelKindLite.Trade => (comms.BarterHall, (uint)TurbineComms.CommsKind.Trade),
            ChannelKindLite.Lfg => (comms.LfgHall, (uint)TurbineComms.CommsKind.Lfg),
            ChannelKindLite.Roleplay => (comms.RoleplayHall, (uint)TurbineComms.CommsKind.Roleplay),
            ChannelKindLite.Society => (comms.SocietyHall, (uint)TurbineComms.CommsKind.Society),
            ChannelKindLite.Olthoi => (comms.OlthoiHall, (uint)TurbineComms.CommsKind.Olthoi),
            _ => (0u, 0u),
        };
    }

    private static bool Listening(ChannelKindLite sort, SimToonOptionsLedger knobs, bool isOlthoiAvatar)
    {
        bool One(PlayerDescReader.ToonOptions1 bit) => (knobs.Options1 & (uint)bit) is not 0u;
        bool Two(PlayerDescReader.ToonOptions2 bit) => (knobs.Options2 & (uint)bit) is not 0u;
        return sort switch
        {
            ChannelKindLite.Allegiance => One(PlayerDescReader.ToonOptions1.HearAllegianceChat),
            ChannelKindLite.General => Two(PlayerDescReader.ToonOptions2.HearGeneralChat),
            ChannelKindLite.Trade => Two(PlayerDescReader.ToonOptions2.HearTradeChat),
            ChannelKindLite.Lfg => Two(PlayerDescReader.ToonOptions2.HearLFGChat),
            ChannelKindLite.Roleplay => Two(PlayerDescReader.ToonOptions2.HearRoleplayChat),
            ChannelKindLite.Society => Two(PlayerDescReader.ToonOptions2.HearSocietyChat),
            ChannelKindLite.Olthoi => isOlthoiAvatar,
            _ => true,
        };
    }
}

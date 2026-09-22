namespace MacAC.Cockpit.Panels.Settings;

public sealed record CommsPrefs(
    bool HearGeneralChat,
    bool HearTradeChat,
    bool HearLFGChat,
    bool HearRoleplayChat,
    bool HearSocietyChat,
    bool AppearOffline,
    bool ShowTimestamps,
    bool FilterProfanity,
    float FontSize,
    ulong ChatWindow1Filter = 0x0000101Cu,
    ulong ChatWindow2Filter = 0x00040C00u,
    ulong ChatWindow3Filter = 0x00080000u,
    ulong ChatWindow4Filter = 0x78000000u,
    ulong ChatWindowMainFilter = 0xFBFFFFFFu,
    float DefaultOpacity = 1.0f,
    float ActiveOpacity = 1.0f,
    int ChatFontFace = 2,
    int ChatFontSizeIndex = 1)
{
    public static CommsPrefs Default { get; } = new(
        HearGeneralChat: true,
        HearTradeChat: true,
        HearLFGChat: true,
        HearRoleplayChat: false,
        HearSocietyChat: false,
        AppearOffline: false,
        ShowTimestamps: true,
        FilterProfanity: true,
        FontSize: 12f);
}

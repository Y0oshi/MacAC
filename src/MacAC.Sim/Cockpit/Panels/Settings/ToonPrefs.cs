namespace MacAC.Cockpit.Panels.Settings;

public sealed record ToonPrefs(string DefaultChatChannel, bool AutoAttack, bool ConfirmSalvage, bool ShowPickupMessages)
{
    public static ToonPrefs Default { get; } = new(
        DefaultChatChannel: "Local",
        AutoAttack: false,
        ConfirmSalvage: true,
        ShowPickupMessages: true);

    public static IReadOnlyList<string> OnHandLanes { get; } = ["Local", "Allegiance", "Fellowship", "General", "Trade", "LFG", "Roleplay"];
}

namespace MacAC.Client.Shell;

public static class CanonPaneRegistry
{
    public const uint ToonInformation = 3u;
    public const uint PositiveFxList = 4u;
    public const uint NegativeFxList = 5u;
    public const uint Inventory = 7u;
    public const uint ConnectStatus = 8u;
    public const uint MiniPlay = 9u;
    public const uint Character = 11u;
    public const uint Magic = 13u;
    public const uint Vitae = 15u;

    public const uint Options = 10u;

    public const uint SocialBoard = 12u;

    public const uint LookupHouse = 16u;

    public const uint Journal = 25u;

    private static readonly (uint PanelId, string WindowName)[] Mounted =
    [
        (ToonInformation, PaneLabels.CharacterInformation),
        (PositiveFxList, PaneLabels.PositiveEffects),
        (NegativeFxList, PaneLabels.NegativeEffects),
        (Inventory, PaneLabels.Inventory),
        (ConnectStatus, PaneLabels.LinkCondition),
        (MiniPlay, PaneLabels.MiniGame),
        (Character, PaneLabels.Character),
        (Magic, PaneLabels.Spellbook),
        (Vitae, PaneLabels.Vitae),
        (Options, PaneLabels.Options),
        (SocialBoard, PaneLabels.SocialPanel),
        (LookupHouse, PaneLabels.MapHouse),
        (Journal, PaneLabels.Journal),
    ];

    private static readonly (uint PanelId, string WindowName)[] Toolbar =
    [
        (Inventory, PaneLabels.Inventory),
        (Character, PaneLabels.Character),
        (Magic, PaneLabels.Spellbook),
        (Options, PaneLabels.Options),
        (LookupHouse, PaneLabels.MapHouse),
        (Journal, PaneLabels.Journal),
    ];

    public static IReadOnlyList<(uint PanelId, string WindowName)> MountedBoards => Mounted;
    public static IReadOnlyList<(uint PanelId, string WindowName)> ToolbarBoards => Toolbar;

    public static bool TryFetchPaneLabel(uint boardIdent, out string paneLabel)
    {
        foreach (var listing in Mounted)
        {
            if (listing.PanelId != boardIdent) continue;
            paneLabel = listing.WindowName;
            return true;
        }

        paneLabel = string.Empty;
        return false;
    }

    public static bool TryFetchBoardIdent(string paneLabel, out uint boardIdent)
    {
        foreach (var listing in Mounted)
        {
            if (!string.Equals(listing.WindowName, paneLabel, StringComparison.Ordinal)) continue;
            boardIdent = listing.PanelId;
            return true;
        }

        boardIdent = 0;
        return false;
    }
}

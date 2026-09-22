namespace MacAC.Client.Shell;

public interface IWidgetDatStateful
{
    uint EngagedCanonPhaseIdent { get; }
    bool TrySetCanonPhase(uint phaseIdent);
}

public static class CanonWidgetStateIds
{
    public const uint Closed = 11u;
    public const uint Open = 12u;
    public const uint HideDetail = 0x10000006u;
    public const uint ShowDetail = 0x10000007u;
    public const uint ObjectSelected = 0x1000000Bu;
    public const uint StackedItemSelected = 0x1000000Cu;
    public const uint GearSocketVacant = 0x1000001Cu;
    public const uint Maximized = 0x10000047u;
    public const uint Minimized = 0x10000048u;
    public const uint BoltedWidget = 0x10000063u;
    public const uint UnlockedWidget = 0x10000064u;

    public const uint Unselected = 0x10000016u;
    public const uint Selected = 0x10000017u;

    public static string PhaseLabel(uint phaseIdent)
    {
        return phaseIdent switch
        {
            Closed => "Closed",
            Open => "Open",
            HideDetail => "HideDetail",
            ShowDetail => "ShowDetail",
            ObjectSelected => "ObjectSelected",
            StackedItemSelected => "StackedItemSelected",
            GearSocketVacant => "ItemSlot_Empty",
            Maximized => "Maximized",
            Minimized => "Minimized",
            BoltedWidget => "LockedUI",
            UnlockedWidget => "UnlockedUI",
            Unselected => "Unselected",
            Selected => "Selected",
            _ => "",
        };
    }

    public static bool TryPhaseIdent(string phaseLabel, out uint phaseIdent)
    {
        phaseIdent = phaseLabel switch
        {
            "Closed" => Closed,
            "Open" => Open,
            "HideDetail" => HideDetail,
            "ShowDetail" => ShowDetail,
            "ObjectSelected" => ObjectSelected,
            "StackedItemSelected" => StackedItemSelected,
            "ItemSlot_Empty" => GearSocketVacant,
            "Maximized" => Maximized,
            "Minimized" => Minimized,
            "LockedUI" => BoltedWidget,
            "UnlockedUI" => UnlockedWidget,
            "Unselected" => Unselected,
            "Selected" => Selected,
            _ => 0u,
        };
        return phaseIdent is not 0;
    }
}

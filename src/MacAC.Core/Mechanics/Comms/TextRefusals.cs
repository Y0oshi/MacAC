namespace MacAC.Mechanics.Comms;

/// <summary>Refusals the client prints on its own, without asking the server.</summary>
public static class TextRefusals
{
    public const string CantLeapLocus = "You can't jump from this position";
    public const string CantLeapInAir = "You can't jump while in the air";
    public const string CantLeapPull = "You're too loaded down to jump";
    public const string CantLeapStamina = "You're too tired to jump!";
    public const string CantLeapRecent = "You've jumped too recently!";
    public const string BarterCancelled = "The trade has been cancelled.";
    public const string TooTired = "You are too tired to move!";
    public const string CantSitFighting = "You can't sit down while in combat mode";
    public const string CantLieDownFighting = "You can't lie down while in combat mode";
    public const string CantCrouchFighting = "You can't crouch while in combat mode";
    public const string CantEmoteFighting = "You can't use chat emotes in combat mode";
    public const string CantEmoteLocus = "You can't use chat emotes from this position";
    public const string TurbineCommsUnavailable = "Turbine chat is not available.";
    public const string CantLogOffMidAir = "Cannot log off while in mid-air.";
    public const string MustPickFightingMark = "You must select a valid combat target before attacking";
}

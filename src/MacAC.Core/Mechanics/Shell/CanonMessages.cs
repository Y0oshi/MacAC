namespace MacAC.Mechanics.Shell;

/// <summary>Client-side refusal lines, worded exactly as the retail client did.</summary>
public static class CanonMessages
{
    public const string CannotChooseUpBeasts = "You cannot pick up creatures!";

    public static string CannotBeConsumed(string actorLabel) => $"The {actorLabel} cannot be used";

    public static string CantBePickedUp(string actorLabel) => $"The {actorLabel} can't be picked up!";

    public static string BeingWieldedBySomeoneElse(string actorLabel) =>
        $"The {actorLabel} is being wielded by someone else!";

    public static string CannotBeConsumedWith(string markLabel) => $"Cannot be used with {markLabel}";

    public static string CannotBePickedUp(string actorLabel) => $"The {actorLabel} cannot be picked up!";

    public static string CannotBeUsedWhileOnHook_HooksOff(string actorLabel)
    {
        return $"The {actorLabel} cannot be used while on a hook, use the '@house hooks on' command to make the hook openable.\n";
    }

    public static string CannotBeUsedWhileOnHook_NotOwner(string actorLabel)
    {
        return $"The {actorLabel} cannot be used while on a hook and only the owner may open the hook.\n";
    }
}

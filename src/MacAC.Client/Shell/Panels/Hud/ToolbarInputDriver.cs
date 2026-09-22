using MacAC.Cockpit.Input;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed class ToolbarInputDriver(ToolbarDriver toolbar, PickPhase selection)
{
    private readonly ToolbarDriver _toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));

    /// <summary>Returns true when <paramref name="act"/> belongs to the toolbar.</summary>
    public bool Handle(FeedAct act)
    {
        if (TryLookupShortcut(act, out int socket, out bool use))
        {
            _toolbar.EmployShortcut(socket, use);
            return true;
        }

        if (act == FeedAct.CreateShortcut)
        {
            _toolbar.BuildShortcutToGear(_pick.ChosenObjectTag ?? 0u);
            return true;
        }

        return false;
    }

    internal static bool TryLookupShortcut(FeedAct act, out int socket, out bool use)
    {
        int val = (int)act;
        if (val is >= ((int)FeedAct.UseQuickSlot_1)
            and <= ((int)FeedAct.UseQuickSlot_9))
        {
            socket = val - (int)FeedAct.UseQuickSlot_1;
            use = true;
            return true;
        }

        if (val is >= ((int)FeedAct.SelectQuickSlot_1)
            and <= ((int)FeedAct.SelectQuickSlot_9))
        {
            socket = val - (int)FeedAct.SelectQuickSlot_1;
            use = false;
            return true;
        }

        if (act is FeedAct.UseQuickSlot_10
            or FeedAct.UseQuickSlot_11
            or FeedAct.UseQuickSlot_12
            or FeedAct.UseQuickSlot_13)
        {
            socket = act switch
            {
                FeedAct.UseQuickSlot_10 => 9,
                FeedAct.UseQuickSlot_11 => 10,
                FeedAct.UseQuickSlot_12 => 11,
                FeedAct.UseQuickSlot_13 => 12,
                _ => -1,
            };
            use = true;
            return true;
        }

        if (val is >= ((int)FeedAct.UseQuickSlot_14)
            and <= ((int)FeedAct.UseQuickSlot_18))
        {
            socket = 13 + val - (int)FeedAct.UseQuickSlot_14;
            use = true;
            return true;
        }

        socket = -1;
        use = false;
        return false;
    }
}

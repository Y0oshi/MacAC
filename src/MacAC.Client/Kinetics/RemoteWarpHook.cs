using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Kinetics;

public static class RemoteWarpHook
{
    private const WeenieProblem WarpAbortCtx = WeenieProblem.ITeleported;

    public static bool Run(
        RemoteWarpHookActions acts,
        Func<bool>? isLatest = null)
    {
        ArgumentNullException.ThrowIfNull(acts.CancelMoveTo);
        ArgumentNullException.ThrowIfNull(acts.UnStick);
        ArgumentNullException.ThrowIfNull(acts.StopInterpolating);
        ArgumentNullException.ThrowIfNull(acts.UnConstrain);
        ArgumentNullException.ThrowIfNull(acts.NotifyTeleported);
        ArgumentNullException.ThrowIfNull(acts.ReportCollisionEnd);

        bool Latest() => isLatest?.Invoke() ?? true;
        if (!Latest())
            return false;
        acts.CancelMoveTo(WarpAbortCtx);
        if (!Latest())
            return false;
        acts.UnStick();
        if (!Latest())
            return false;
        acts.StopInterpolating();
        if (!Latest())
            return false;
        acts.UnConstrain();
        if (!Latest())
            return false;
        acts.NotifyTeleported();
        if (!Latest())
            return false;
        acts.ReportCollisionEnd();
        return Latest();
    }
}

public sealed record RemoteWarpHookActions(
    Action<WeenieProblem> CancelMoveTo,
    Action UnStick,
    Action StopInterpolating,
    Action UnConstrain,
    Action NotifyTeleported,
    Action ReportCollisionEnd);

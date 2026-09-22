namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger
{
    public SimLogoutStage SignoutJuncture => _signout.Stage;
    public bool IsSignoutEngaged => _signout.Stage != SimLogoutStage.None;

    public bool TryCommenceSignoutReq(bool isAvatarKiller)
    {
        if (_signout.Stage != SimLogoutStage.None || _teleport.Active || _teleport.Pending)
        {
            TraceRejected(
                "logout-request-refused",
                $"stage={_signout.Stage} teleportActive={_teleport.Active} "
                + $"pendingStart={_teleport.Pending}");
            return false;
        }

        _signout = new Hold
        {
            Stage = SimLogoutStage.Requested,
            Elapsed = 0d,
            Required = CanonSignoutGripSecs
                + (isAvatarKiller ? CanonAvatarKillerAdditionalGripSecs : 0d),
        };
        SafeTrace(
            $"{Tag}logout-requested "
            + $"holdSeconds={_signout.Required:F1} "
            + $"pk={(isAvatarKiller ? 1 : 0)}");
        return true;
    }

    public bool AbortSignoutReq()
    {
        if (_signout.Stage != SimLogoutStage.Requested)
            return false;

        _signout = default;
        SafeTrace($"{Tag}logout-request-cancelled");
        return true;
    }

    public bool ProgressSignoutGrip(double diffSecs)
    {
        if (_signout.Stage != SimLogoutStage.Requested || diffSecs < 0d)
            return false;

        _signout.Elapsed += diffSecs;
        if (_signout.Elapsed < _signout.Required)
            return false;

        _signout.Stage = SimLogoutStage.PresentationActive;
        SafeTrace($"{Tag}logout-presentation-begin");
        return true;
    }

    public bool AcknowledgeSignoutConfirmed()
    {
        if (_signout.Stage is not (SimLogoutStage.Requested or SimLogoutStage.PresentationActive))
            return false;

        _signout.Stage = SimLogoutStage.Confirmed;
        SafeTrace($"{Tag}logout-confirmed");
        return true;
    }

    public bool ConcludeSignout()
    {
        if (_signout.Stage != SimLogoutStage.Confirmed)
            return false;

        _signout = default;
        SafeTrace($"{Tag}logout-complete");
        return true;
    }
}

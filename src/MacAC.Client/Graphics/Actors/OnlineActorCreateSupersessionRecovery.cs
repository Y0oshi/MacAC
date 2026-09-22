using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

internal static class OnlineActorCreateSupersessionRecovery
{
    public static bool TryApply(
        OnlineActorCore core,
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer,
        Func<OnlineActorAppearancePulseLedger?> grabLooks,
        Func<OnlineActorAppearancePulseLedger, bool> broadcastLooks,
        Action<OnlineActorAppearancePulseLedger> broadcastLatestCapture,
        Action<OnlineActorAppearancePulseLedger> synchronizeAnim)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        ArgumentNullException.ThrowIfNull(grabLooks);
        ArgumentNullException.ThrowIfNull(broadcastLooks);
        ArgumentNullException.ThrowIfNull(broadcastLatestCapture);
        ArgumentNullException.ThrowIfNull(synchronizeAnim);

        if (!core.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || grabLooks() is not { } visualRefresh
            || !core.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !broadcastLooks(visualRefresh)
            || !core.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer))

            return false;

        broadcastLatestCapture(visualRefresh);
        if (!core.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer))

            return false;

        synchronizeAnim(visualRefresh);
        return core.IsLatestBuildIntegration(
            anticipatedCapture,
            anticipatedBuildIntegrationVer);
    }
}

internal static class OnlineActorCreateMotionSynchronization
{
    public static bool TrySynchronizeInterruptedStartingHolder(
        OnlineActorRecord capture,
        OnlineActorMotionLedger anim,
        MotionClip? canonAnim,
        int canonLoCycle,
        int canonHiCycle,
        float canonFramerate,
        MotionBook? locomotionChart,
        ObjectCreation.RemoteMotionState? wirePhase)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(anim);
        if (capture.StartingHydrationFinished)
            return false;

        if (canonAnim is not null)
        {
            TrySynchronizeInterruptedStartingHolderBranch(anim, canonAnim, canonLoCycle, canonHiCycle, canonFramerate);
        }

        if (anim.Sequencer is { } scheduler && locomotionChart is not null)
        {
            SummonLocomotionInitializer.Reinitialize(
                scheduler,
                locomotionChart,
                wirePhase);
        }
        return true;
    }

    private static void TrySynchronizeInterruptedStartingHolderBranch(OnlineActorMotionLedger anim, MotionClip canonAnim, int canonLoCycle, int canonHiCycle, float canonFramerate)
    {
        anim.Animation = canonAnim;
        anim.LoCycle = canonLoCycle;
        anim.HighFrame = canonHiCycle;
        anim.Framerate = canonFramerate;
        anim.CurrCycle = canonLoCycle;
    }
}

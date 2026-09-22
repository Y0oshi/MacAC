namespace MacAC.Client.Graphics;

internal readonly record struct FramePacingRule(
    bool UseVSync,
    double? SoftwareLimitHz)
{
    internal const double BackupRenewHz = 60d;

    public static FramePacingRule Resolve(
        bool askedVSynchronize,
        bool uncappedRendering,
        int? observeRenewHz)
    {
        if (uncappedRendering)
            return new FramePacingRule(UseVSync: false, SoftwareLimitHz: null);

        if (askedVSynchronize)
            return new FramePacingRule(UseVSync: true, SoftwareLimitHz: null);

        double renewHz = observeRenewHz is > 0
            ? observeRenewHz.Value
            : BackupRenewHz;
        return new FramePacingRule(UseVSync: false, SoftwareLimitHz: renewHz);
    }
}

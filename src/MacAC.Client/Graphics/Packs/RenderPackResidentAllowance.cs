namespace MacAC.Client.Graphics.Packs;

internal static class RenderPackResidentAllowance
{
    internal const long ReferencePx = 1920L * 1080L;

    internal static long Net(
        long declaredOctetsAt1080p,
        int viewRectWidth,
        int viewRectHeight,
        long hardwareCapOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(declaredOctetsAt1080p);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewRectWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewRectHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(hardwareCapOctets);

        long px = checked((long)viewRectWidth * viewRectHeight);
        long scaled = px <= ReferencePx
            ? declaredOctetsAt1080p
            : checked((long)Math.Ceiling(
                (double)declaredOctetsAt1080p * px / ReferencePx));
        return Math.Min(scaled, hardwareCapOctets);
    }
}

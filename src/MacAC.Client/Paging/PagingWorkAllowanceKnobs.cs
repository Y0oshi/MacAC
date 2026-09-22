using System.Globalization;

namespace MacAC.Client.Paging;

public sealed record PagingWorkAllowanceKnobs(
    double MaxUpdateMilliseconds,
    int MaxCompletionAdmissions,
    long MaxAdoptedCpuBytes,
    int MaxEntityOperations,
    long MaxGpuUploadBytes,
    int MaxGlRetireOperations,
    float DestinationReserveFraction,
    double HoldDestinationCeilingMilliseconds = 8.0)
{
    public const long MiB = 1024L * 1024L;

    public static PagingWorkAllowanceKnobs Default { get; } = new(
        MaxUpdateMilliseconds: 2.0,
        MaxCompletionAdmissions: 64,
        MaxAdoptedCpuBytes: 8 * MiB,
        MaxEntityOperations: 4_096,
        MaxGpuUploadBytes: 8 * MiB,
        MaxGlRetireOperations: 64,
        DestinationReserveFraction: 0.75f,
        HoldDestinationCeilingMilliseconds: 8.0);

    public PagingWorkAllowance ToAllowance()
    {
        return new(
        TimeSpan.FromMilliseconds(MaxUpdateMilliseconds),
        MaxCompletionAdmissions,
        MaxAdoptedCpuBytes,
        MaxEntityOperations,
        MaxGpuUploadBytes,
        MaxGlRetireOperations,
        DestinationReserveFraction);
    }

    public PagingWorkAllowanceKnobs ResizeForLegacyWrapUpTally(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        double scaling = count / 4.0;
        return this with
        {
            MaxUpdateMilliseconds = Math.Max(
                0.25,
                MaxUpdateMilliseconds * scaling),
            MaxCompletionAdmissions = Scale(MaxCompletionAdmissions, scaling),
            MaxAdoptedCpuBytes = Scale(MaxAdoptedCpuBytes, scaling),
            MaxEntityOperations = Scale(MaxEntityOperations, scaling),
            MaxGpuUploadBytes = Scale(MaxGpuUploadBytes, scaling),
            MaxGlRetireOperations = Scale(MaxGlRetireOperations, scaling),
        };
    }

    internal static PagingWorkAllowanceKnobs Decode(
        Func<string, string?> environ)
    {
        ArgumentNullException.ThrowIfNull(environ);
        var defaults = Default;
        return new PagingWorkAllowanceKnobs(
            MaxUpdateMilliseconds: DecodePositiveDouble(
                environ("MACAC_STREAM_WORK_MS"),
                defaults.MaxUpdateMilliseconds),
            MaxCompletionAdmissions: DecodePositiveInt(
                environ("MACAC_STREAM_WORK_COMPLETIONS"),
                defaults.MaxCompletionAdmissions),
            MaxAdoptedCpuBytes: DecodeMiB(
                environ("MACAC_STREAM_WORK_CPU_MIB"),
                defaults.MaxAdoptedCpuBytes),
            MaxEntityOperations: DecodePositiveInt(
                environ("MACAC_STREAM_WORK_ENTITY_OPS"),
                defaults.MaxEntityOperations),
            MaxGpuUploadBytes: DecodeMiB(
                environ("MACAC_STREAM_WORK_GPU_MIB"),
                defaults.MaxGpuUploadBytes),
            MaxGlRetireOperations: DecodePositiveInt(
                environ("MACAC_STREAM_WORK_GL_RETIRE_OPS"),
                defaults.MaxGlRetireOperations),
            DestinationReserveFraction: DecodeAllocatePct(
                environ("MACAC_STREAM_WORK_DEST_RESERVE_PERCENT"),
                defaults.DestinationReserveFraction),
            HoldDestinationCeilingMilliseconds: DecodePositiveDouble(
                environ("MACAC_STREAM_WORK_HOLD_DEST_MS"),
                defaults.HoldDestinationCeilingMilliseconds));
    }

    internal static int Scale(int val, double scaling)
    {
        return scaling >= int.MaxValue / (double)val
            ? int.MaxValue
            : Math.Max(
            1,
            (int)Math.Round(val * scaling, MidpointRounding.AwayFromZero));
    }

    internal static long Scale(long val, double scaling)
    {
        return scaling >= long.MaxValue / (double)val
            ? long.MaxValue
            : Math.Max(
            1L,
            (long)Math.Round(val * scaling, MidpointRounding.AwayFromZero));
    }

    private static double DecodePositiveDouble(string? val, double backup)
    {
        return double.TryParse(
            val,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double decoded)
        && double.IsFinite(decoded)
        && decoded > 0
            ? decoded
            : backup;
    }

    private static int DecodePositiveInt(string? val, int backup)
    {
        return int.TryParse(
            val,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int decoded)
        && decoded > 0
            ? decoded
            : backup;
    }

    private static long DecodeMiB(string? val, long backup)
    {
        return !long.TryParse(
                val,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long mebibytes)
            || mebibytes <= 0
            || mebibytes > long.MaxValue / MiB
            ? backup
            : checked(mebibytes * MiB);
    }

    private static float DecodeAllocatePct(string? val, float backup)
    {
        return !float.TryParse(
                val,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float pct)
            || !float.IsFinite(pct)
            || pct <= 0f
            || pct >= 100f
            ? backup
            : pct / 100f;
    }
}

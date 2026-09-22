using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics;

// macOS has no clock_nanosleep, so the frame deadline is held with mach's absolute-time timer
internal sealed partial class MacMonotonicCyclePacingPauser
    : ICyclePacingPauser,
      IDisposable
{
    private const int KernSuccess = 0;
    private const int KernAborted = 14;

    private readonly uint _timebaseNumerator;
    private readonly uint _timebaseDenominator;
    private bool _destroyed;

    private MacMonotonicCyclePacingPauser(
        uint timebaseNumerator,
        uint timebaseDenominator)
    {
        _timebaseNumerator = timebaseNumerator;
        _timebaseDenominator = timebaseDenominator;
    }

    public void Wait(long intervalBeats, long timerFrequency)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (intervalBeats <= 0 || timerFrequency <= 0)
            return;

        long intervalNanoseconds =
            CyclePacingInterval.TranslateBeatsToNanoseconds(
                intervalBeats,
                timerFrequency);
        ulong deadline = AppendMachUnits(
            MachAbsoluteTime(),
            TranslateNanosecondsToMachUnits(
                intervalNanoseconds,
                _timebaseNumerator,
                _timebaseDenominator));

        WaitRest(deadline);
    }

    private void WaitRest(ulong deadline)
    {
        int outcome;
        do
        {
            outcome = MachWaitUntil(deadline);
        }
        while (outcome == KernAborted);
        if (outcome != KernSuccess)
        {
            throw new InvalidOperationException(
                "Waiting on the mach frame deadline failed with " +
                $"kern_return_t {outcome}.");
        }
    }

    public void Dispose() => _destroyed = true;

    internal static MacMonotonicCyclePacingPauser Create()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The monotonic mach frame waiter needs macOS");
        }

        return MachTimebaseInfo(out ClientMachTimebase timebase) != KernSuccess
            || timebase.Numerator is 0
            || timebase.Denominator is 0
            ? throw new InvalidOperationException(
                "Could not read the mach clock timebase")
            : new MacMonotonicCyclePacingPauser(
            timebase.Numerator,
            timebase.Denominator);
    }

    internal static ulong TranslateNanosecondsToMachUnits(
        long nanoseconds,
        uint timebaseNumerator,
        uint timebaseDenominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(timebaseNumerator);
        ArgumentOutOfRangeException.ThrowIfZero(timebaseDenominator);
        if (nanoseconds <= 0)
            return 0UL;

        UInt128 scaledNanoseconds =
            (UInt128)(ulong)nanoseconds
            * timebaseDenominator;
        UInt128 machUnits =
            (scaledNanoseconds + timebaseNumerator - 1u)
            / timebaseNumerator;
        if (machUnits > ulong.MaxValue)
            return ulong.MaxValue;

        return Math.Max(1UL, (ulong)machUnits);
    }

    private static ulong AppendMachUnits(ulong stamp, ulong diff)
    {
        return stamp > ulong.MaxValue - diff
            ? ulong.MaxValue
            : stamp + diff;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ClientMachTimebase(
        uint Numerator,
        uint Denominator);

    [LibraryImport("libSystem.dylib", EntryPoint = "mach_timebase_info")]
    private static partial int MachTimebaseInfo(out ClientMachTimebase timebase);

    [LibraryImport("libSystem.dylib", EntryPoint = "mach_absolute_time")]
    private static partial ulong MachAbsoluteTime();

    [LibraryImport("libSystem.dylib", EntryPoint = "mach_wait_until")]
    private static partial int MachWaitUntil(ulong deadline);
}

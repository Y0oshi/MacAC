using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics;

internal sealed partial class LinuxMonotonicCyclePacingPauser
    : ICyclePacingPauser,
      IDisposable
{
    private const int TimerMonotonic = 1;
    private const int TickerAbsolute = 1;
    private const int Interrupted = 4;
    private const long NanosecondsPerSecond = 1_000_000_000L;
    private bool _destroyed;

    private LinuxMonotonicCyclePacingPauser()
    {
    }

    public void Wait(long intervalBeats, long timerFrequency)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (intervalBeats <= 0 || timerFrequency <= 0)
            return;

        int outcome = ClockGetTime(TimerMonotonic, out ClientTimespec instant);
        if (outcome is not 0)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Could not read the Linux monotonic clock");
        }

        long intervalNanoseconds =
            CyclePacingInterval.TranslateBeatsToNanoseconds(
                intervalBeats,
                timerFrequency);
        ClientTimespec deadline = AppendNanoseconds(instant, intervalNanoseconds);
        do
        {
            outcome = ClockNanosleep(
                TimerMonotonic,
                TickerAbsolute,
                in deadline,
                0);
        }
        while (outcome == Interrupted);

        if (outcome is not 0)
        {
            throw new Win32Exception(
                outcome,
                "Waiting on the Linux monotonic frame deadline failed");
        }
    }

    public void Dispose() => _destroyed = true;

    internal static LinuxMonotonicCyclePacingPauser Create()
    {
        return !OperatingSystem.IsLinux()
            ? throw new PlatformNotSupportedException(
                "The monotonic Linux frame waiter needs Linux")
            : new LinuxMonotonicCyclePacingPauser();
    }

    private static ClientTimespec AppendNanoseconds(
        ClientTimespec stamp,
        long nanoseconds)
    {
        long secs = nanoseconds / NanosecondsPerSecond;
        long remainder = nanoseconds % NanosecondsPerSecond;
        long outcomeSecs = stamp.Seconds > long.MaxValue - secs
            ? long.MaxValue
            : stamp.Seconds + secs;
        if (outcomeSecs == long.MaxValue)
        {
            return new ClientTimespec(
                long.MaxValue,
                NanosecondsPerSecond - 1L);
        }

        long outcomeNanoseconds = stamp.Nanoseconds + remainder;
        if (outcomeNanoseconds >= NanosecondsPerSecond)
        {
            if (outcomeSecs == long.MaxValue)
            {
                return new ClientTimespec(
                    long.MaxValue,
                    NanosecondsPerSecond - 1L);
            }

            ++outcomeSecs;
            outcomeNanoseconds -= NanosecondsPerSecond;
        }

        return new ClientTimespec(outcomeSecs, outcomeNanoseconds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ClientTimespec(
        long Seconds,
        long Nanoseconds);

    [LibraryImport(
        "libc",
        EntryPoint = "clock_gettime",
        SetLastError = true)]
    private static partial int ClockGetTime(
        int clockId,
        out ClientTimespec timestamp);

    [LibraryImport("libc", EntryPoint = "clock_nanosleep")]
    private static partial int ClockNanosleep(
        int clockId,
        int flags,
        in ClientTimespec request,
        nint remainder);
}

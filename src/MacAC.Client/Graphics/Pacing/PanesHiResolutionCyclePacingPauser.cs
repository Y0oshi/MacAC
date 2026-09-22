using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacAC.Client.Graphics;

internal sealed partial class PanesHiResolutionCyclePacingPauser :
    ICyclePacingPauser,
    IDisposable
{
    private const uint BuildWaitableTickerHiResolution = 0x00000002;
    private const uint TickerModifyPhase = 0x00000002;
    private const uint Synchronize = 0x00100000;
    private const uint PauseObject0 = 0x00000000;
    private const uint PauseTimeout = 0x00000102;
    private const uint PauseFailed = 0xFFFFFFFF;
    private const long HundredNanosecondsPerSecond = 10_000_000;

    private readonly SafeWaitHandle _ticker;
    private bool _destroyed;

    private PanesHiResolutionCyclePacingPauser(SafeWaitHandle ticker)
        => _ticker = ticker;

    public static PanesHiResolutionCyclePacingPauser Create()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            throw new PlatformNotSupportedException(
                "macac software frame pacing needs Windows 10 version 1803 or newer");
        }

        SafeWaitHandle ticker = CreateWaitableTimerExW(
            0,
            0,
            BuildWaitableTickerHiResolution,
            TickerModifyPhase | Synchronize);
        if (ticker.IsInvalid)
        {
            int problem = Marshal.GetLastPInvokeError();
            ticker.Dispose();
            throw new Win32Exception(problem, "Could not create the high-resolution frame timer");
        }

        return new PanesHiResolutionCyclePacingPauser(ticker);
    }

    public void Wait(long intervalBeats, long timerFrequency)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (intervalBeats <= 0 || timerFrequency <= 0)
            return;

        long hundredNanoseconds = TranslateBeatsToHundredNanoseconds(
            intervalBeats,
            timerFrequency);
        long relativeDueMoment = -hundredNanoseconds;

        if (!SetWaitableTimerEx(
                _ticker,
                in relativeDueMoment,
                periodMilliseconds: 0,
                completionRoutine: 0,
                completionArgument: 0,
                wakeContext: 0,
                tolerableDelayMilliseconds: 0))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Could not arm the high-resolution frame timer");
        }

        uint outcome = WaitForSingleObject(
            _ticker,
            CalculateMissTimeoutMillis(hundredNanoseconds));
        if (outcome == PauseObject0)
            return;
        if (outcome == PauseTimeout)
        {
            throw new TimeoutException(
                "The high-resolution frame timer didn't signal prior to its safety timeout");
        }

        int pauseProblem = outcome == PauseFailed
            ? Marshal.GetLastPInvokeError()
            : unchecked((int)outcome);
        throw new Win32Exception(pauseProblem, "Waiting on the high-resolution frame timer failed");
    }

    internal static long TranslateBeatsToHundredNanoseconds(
        long durationTicks,
        long clockFrequency)
    {
        if (durationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (clockFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(clockFrequency));

        long wholeSecs = Math.DivRem(durationTicks, clockFrequency, out long remainder);
        if (wholeSecs >= long.MaxValue / HundredNanosecondsPerSecond)
            return long.MaxValue;

        long wholeIntervals = wholeSecs * HundredNanosecondsPerSecond;
        long fractionalIntervals = (long)Math.Ceiling(
            remainder * (double)HundredNanosecondsPerSecond / clockFrequency);
        return wholeIntervals > long.MaxValue - fractionalIntervals ? long.MaxValue : Math.Max(1, wholeIntervals + fractionalIntervals);
    }

    private static uint CalculateMissTimeoutMillis(long hundredNanoseconds)
    {
        double dueMillis = hundredNanoseconds / 10_000d;
        double timeoutMillis = Math.Ceiling(dueMillis) + 1_000d;
        return timeoutMillis >= uint.MaxValue - 1d
            ? uint.MaxValue - 1
            : Math.Max(1u, (uint)timeoutMillis);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        _destroyed = true;
        _ticker.Dispose();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(
        nint timerAttributes,
        nint timerName,
        uint flags,
        uint desiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimerEx(
        SafeWaitHandle timer,
        in long dueTime,
        int periodMilliseconds,
        nint completionRoutine,
        nint completionArgument,
        nint wakeContext,
        uint tolerableDelayMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(
        SafeWaitHandle handle,
        uint milliseconds);
}

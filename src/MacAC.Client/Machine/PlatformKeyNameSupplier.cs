using System.Runtime.InteropServices;

namespace MacAC.Client.Machine;

public static class PlatformKeyNameSupplier
{
    public static Func<byte, bool, string?>? ForCurrentProcess()
        => OperatingSystem.IsWindows() ? PanesTagLabel : null;

    private static string? PanesTagLabel(byte scan, bool extended)
    {
        Span<char> buf = stackalloc char[64];
        int lParameter = (scan << 16) | (extended ? 1 << 24 : 0);
        int len;
        unsafe
        {
            fixed (char* p = buf)
                len = GetKeyNameTextW(lParameter, p, buf.Length);
        }
        return len > 0 ? new string(buf[..len]) : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern unsafe int GetKeyNameTextW(int lParam, char* lpString, int cchSize);
}

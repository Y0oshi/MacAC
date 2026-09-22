using System.Runtime.InteropServices;

namespace MacAC.Client.Machine;

// Sets the Dock icon on macOS
internal static unsafe class MacDockIcon
{
    public static bool IsSupported => OperatingSystem.IsMacOS();

    public static void Apply(ReadOnlySpan<byte> pngOctets)
    {
        if (!IsSupported || pngOctets.IsEmpty)
            return;

        try
        {
            nint nsApp = Send(Class("NSApplication"), Sel("sharedApplication"));
            if (nsApp == 0)
                return;

            fixed (byte* p = pngOctets)
            {
                nint blob = Send(Class("NSData"), Sel("dataWithBytes:length:"), (nint)p, (nint)pngOctets.Length);
                nint image = Send(Send(Class("NSImage"), Sel("alloc")), Sel("initWithData:"), blob);
                if (image == 0)
                    return;
                Send(nsApp, Sel("setApplicationIconImage:"), image);
            }
        }
        catch (Exception miss)
        {
            Console.Error.WriteLine($"dock icon: could not set the application icon - {miss.Message}");
        }
    }

    private const string ObjRefC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjRefC, EntryPoint = "objc_getClass")]
    private static extern nint Class([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjRefC, EntryPoint = "sel_registerName")]
    private static extern nint Sel([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjRefC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjRefC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint arg1);

    [DllImport(ObjRefC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2);
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MacAC.Client.Machine;

internal static unsafe class Win32GlfwActiveWindowWarden
{
    private const string GlfwModuleLabel = "glfw3.dll";
    private const string User32ModuleLabel = "USER32.dll";
    private const string FetchEngagedPaneImport = "GetActiveWindow";
    private const uint SheetScanEmit = 0x04;
    private const ushort DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;
    private const int ImportDescriptorDims = 20;

    private static readonly uint LatestProcIdent =
        checked((uint)Environment.ProcessId);
    private static int _installPhase;

    internal static bool IsInstalled => Volatile.Read(ref _installPhase) is 1;

    internal static void Install()
    {
        if (!OperatingSystem.IsWindows()
            || Interlocked.CompareExchange(ref _installPhase, 2, 0) is not 0)

            return;

        try
        {
            nint module = GetModuleHandleW(GlfwModuleLabel);
            if (module == 0
                || !TrySeekImportSocket(
                    module,
                    User32ModuleLabel,
                    FetchEngagedPaneImport,
                    out nint socket))
            {
                Volatile.Write(ref _installPhase, -1);
                Console.Error.WriteLine(
                    "windowing: could not install the GLFW foreign-active-window guard");
                return;
            }

            nint substitute = (nint)(delegate* unmanaged[Stdcall]<nint>)
                &GetCurrentProcessActiveWindow;
            if (!VirtualProtect(
                    socket,
                    checked((nuint)IntPtr.Size),
                    SheetScanEmit,
                    out uint formerProtection))
            {
                Volatile.Write(ref _installPhase, -1);
                Console.Error.WriteLine(
                    "windowing: GLFW active-window import wasn't writable");
                return;
            }

            try
            {
                *(nint*)socket = substitute;
            }
            finally
            {
                _ = VirtualProtect(
                    socket,
                    checked((nuint)IntPtr.Size),
                    formerProtection,
                    out _);
            }

            Volatile.Write(ref _installPhase, 1);
            Console.WriteLine(
                "windowing: GLFW foreign-active-window guard installed");
        }
        catch (Exception miss)
        {
            Volatile.Write(ref _installPhase, -1);
            Console.Error.WriteLine(
                $"windowing: GLFW active-window guard failed: {miss.Message}");
        }
    }

    internal static nint AdmitPane(
        nint pane,
        uint holderProcIdent,
        uint latestProcIdent)
    {
        return pane != 0
        && holderProcIdent is not 0
        && holderProcIdent == latestProcIdent
            ? pane
            : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint GetCurrentProcessActiveWindow()
    {
        nint pane = GetActiveWindow();
        if (pane == 0)
            return 0;

        _ = GetWindowThreadProcessId(pane, out uint holderProcIdent);
        return AdmitPane(pane, holderProcIdent, LatestProcIdent);
    }

    private static bool TrySeekImportSocket(
        nint module,
        string importedModule,
        string importedFunction,
        out nint socket)
    {
        socket = 0;
        byte* image = (byte*)module;
        if (*(ushort*)image != DosSignature)
            return false;

        int peShift = *(int*)(image + 0x3C);
        if (peShift <= 0 || *(uint*)(image + peShift) != PeSignature)
            return false;

        byte* optionalPreamble = image + peShift + 24;
        ushort magic = *(ushort*)optionalPreamble;
        int blobFolderShift;
        int thunkDims;
        ulong indexBit;
        if (magic == Pe32PlusMagic)
        {
            blobFolderShift = 112;
            thunkDims = 8;
            indexBit = 0x8000000000000000UL;
        }
        else if (magic == Pe32Magic)
        {
            blobFolderShift = 96;
            thunkDims = 4;
            indexBit = 0x80000000UL;
        }
        else
        {
            return false;
        }

        uint dimsOfImage = *(uint*)(optionalPreamble + 56);
        uint importRva = *(uint*)(optionalPreamble + blobFolderShift + 8);
        uint importDims = *(uint*)(optionalPreamble + blobFolderShift + 12);
        if (!Contains(dimsOfImage, importRva, ImportDescriptorDims))
            return false;

        int descriptorThreshold = importDims >= ImportDescriptorDims
            ? checked((int)(importDims / ImportDescriptorDims))
            : checked((int)((dimsOfImage - importRva) / ImportDescriptorDims));
        for (int descriptorOrdinal = 0;
             descriptorOrdinal < descriptorThreshold;
             ++descriptorOrdinal)
        {
            byte* descriptor = image
                + importRva
                + descriptorOrdinal * ImportDescriptorDims;
            uint originalLeadThunk = *(uint*)descriptor;
            uint labelRva = *(uint*)(descriptor + 12);
            uint leadThunk = *(uint*)(descriptor + 16);
            if (originalLeadThunk is 0 && labelRva is 0 && leadThunk is 0)
                break;
            if (!FitsAsciiZ(image, dimsOfImage, labelRva, importedModule, true))
                continue;
            if (originalLeadThunk is 0
                || !Contains(dimsOfImage, originalLeadThunk, thunkDims)
                || !Contains(dimsOfImage, leadThunk, thunkDims))

                return false;

            int thunkThreshold = checked((int)Math.Min(
                (dimsOfImage - originalLeadThunk) / (uint)thunkDims,
                (dimsOfImage - leadThunk) / (uint)thunkDims));
            for (int thunkOrdinal = 0; thunkOrdinal < thunkThreshold; ++thunkOrdinal)
            {
                ulong labelThunk = thunkDims is 8
                    ? *(ulong*)(image + originalLeadThunk + thunkOrdinal * thunkDims)
                    : *(uint*)(image + originalLeadThunk + thunkOrdinal * thunkDims);
                if (labelThunk is 0)
                    break;
                if ((labelThunk & indexBit) is not 0)
                    continue;

                uint importByLabelRva = checked((uint)labelThunk);
                if (!Contains(dimsOfImage, importByLabelRva, 3)
                    || !FitsAsciiZ(
                        image,
                        dimsOfImage,
                        importByLabelRva + 2,
                        importedFunction,
                        false))

                    continue;

                socket = (nint)(image + leadThunk + thunkOrdinal * thunkDims);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool Contains(uint imageDims, uint shift, int len)
    {
        return len >= 0
        && shift < imageDims
        && (ulong)shift + (uint)len <= imageDims;
    }

    private static bool FitsAsciiZ(
        byte* image,
        uint imageDims,
        uint shift,
        string anticipated,
        bool ignoreCase)
    {
        if (!Contains(imageDims, shift, anticipated.Length + 1))
            return false;

        for (int idx = 0; idx < anticipated.Length; ++idx)
        {
            char actual = (char)image[shift + (uint)idx];
            char wanted = anticipated[idx];
            if (ignoreCase)
            {
                actual = char.ToUpperInvariant(actual);
                wanted = char.ToUpperInvariant(wanted);
            }
            if (actual != wanted)
                return false;
        }
        return image[shift + (uint)anticipated.Length] is 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(
        nint address,
        nuint size,
        uint newProtection,
        out uint oldProtection);

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);
}

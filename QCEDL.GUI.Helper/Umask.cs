using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QCEDL.GUI.Helper;

public static class Umask
{
    [SupportedOSPlatform("Linux")]
    [DllImport("libc", EntryPoint = "unask", SetLastError = true)]
    private static extern uint UmaskLinux(uint mask);

    [SupportedOSPlatform("macOS")]
    [DllImport("libSystem.B.dylib", EntryPoint = "umask", SetLastError = true)]
    private static extern uint UmaskMacOS(uint mask);

    public static void Set(uint unixFileMode)
    {
        if (OperatingSystem.IsLinux())
        {
            _ = UmaskLinux(unixFileMode);
        }
        else if (OperatingSystem.IsMacOS())
        {
            _ = UmaskMacOS(unixFileMode);
        }
    }
}
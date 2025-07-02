using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace QCEDL.GUI.Helper;

[SupportedOSPlatform("macOS")]
public sealed class MacOSHelperLauncher(ILogger<MacOSHelperLauncher> logger) : HelperLauncherBase(logger)
{
    private const string HelperProcess = "./QCEDL.GUI.Helper";

    protected override void LaunchHelperProcess()
    {
        var appleScript = $"do shell script \"{HelperProcess.Replace("\"", "\\\"")}\" with administrator privileges";
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            ArgumentList = { "-e", appleScript },
            UseShellExecute = false
        };

        Process.Start(psi);
    }
}
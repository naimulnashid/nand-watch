using System.Security.Principal;
using Microsoft.Win32;

namespace NandWatch.Windows.Probe;

public static class MachineInfo
{
    /// <summary>
    /// True only in an elevated process. Under UAC an administrator's normal
    /// token is filtered, so this is false for them too until "Run as administrator".
    /// </summary>
    public static bool Elevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Edition, version and build. ProductName still says "Windows 10" on 11; the build number is the truth.</summary>
    public static string Windows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null) return Environment.OSVersion.VersionString;
        return $"{key.GetValue("ProductName")} {key.GetValue("DisplayVersion")}, build {key.GetValue("CurrentBuild")}.{key.GetValue("UBR")}";
    }

    /// <summary>Serial numbers identify a physical drive; the report shows the last four characters only.</summary>
    public static string Mask(string serial) =>
        serial.Length <= 4 ? new string('*', serial.Length) : new string('*', serial.Length - 4) + serial[^4..];
}

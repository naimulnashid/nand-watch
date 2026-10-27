using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NandWatch.Windows.Notify;

/// <summary>
/// Windows notifications (toasts) from any of the app's processes: the
/// hourly collector above all, which is how an alert reaches the owner with
/// nothing resident (the tray is optional and off by default).
/// </summary>
/// <remarks>
/// <para><b>An unpackaged app needs an identity for a toast.</b> It is the
/// AppUserModelID <see cref="AppId"/>, registered per user under
/// <c>HKCU\Software\Classes\AppUserModelId</c> with the name and icon Windows
/// shows on the toast. No Start menu shortcut property and no COM activator
/// are needed for a toast that only opens the app.</para>
/// <para><b>A click opens the app through the <c>nandwatch:</c> protocol</b>
/// (registered under <c>HKCU\Software\Classes\nandwatch</c>, pointing at the
/// installed NandWatch.exe). The app treats any launch with it as "open":
/// the URI carries no command, so a web page that invokes it can only open
/// the window.</para>
/// <para>Both keys are rewritten on every toast (cheap, per user, no
/// elevation), so a reinstall elsewhere repairs them; Uninstall.ps1 removes
/// them. A toast that cannot be shown fails silently: the alert is already
/// in the history, and a notification must never fail a collection.</para>
/// </remarks>
public static class Toasts
{
    public const string AppId = "NaimulNashid.NandWatch";
    public const string Protocol = "nandwatch";

    /// <summary>Shows one toast; false when Windows would not.</summary>
    public static bool Show(string title, string body)
    {
        try
        {
            var app = Register();
            var launch = app is null ? "" : $" launch=\"{Protocol}:open\" activationType=\"protocol\"";
            var xml = new XmlDocument();
            xml.LoadXml($"<toast{launch}><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
            return true;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or SecurityException or IOException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The installed app beside this exe (the collector's folder) or one up
    /// (the CLI's <c>cli\</c>); null when there is none, as in a build tree.
    /// </summary>
    /// <remarks>
    /// The app is recognised by its <c>NandWatch.pri</c>, not by its exe's
    /// name: on Windows the CLI's <c>nandwatch.exe</c> answers to
    /// "NandWatch.exe" too, so a toast sent from the CLI pointed its click at
    /// the CLI, which opened no window (2026-10-27).
    /// </remarks>
    private static string? AppExe()
    {
        var here = AppContext.BaseDirectory;
        foreach (var dir in new[] { here, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(here)) })
            if (dir is not null && Path.Combine(dir, "NandWatch.exe") is var exe && File.Exists(exe) && File.Exists(Path.Combine(dir, "NandWatch.pri"))) return exe;
        return null;
    }

    /// <summary>Writes the identity and the protocol; returns the app's path, or null when it was not found.</summary>
    private static string? Register()
    {
        var app = AppExe();
        using (var id = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            id.SetValue("DisplayName", "NAND Watch");
            var icon = app is null ? null : Path.Combine(Path.GetDirectoryName(app)!, "Assets", "app-icon.png");
            if (icon is not null && File.Exists(icon)) id.SetValue("IconUri", icon);
        }
        if (app is null) return null;
        using var protocol = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Protocol}");
        protocol.SetValue("", "URL:NAND Watch");
        protocol.SetValue("URL Protocol", "");
        using var command = protocol.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{app}\" \"%1\"");
        return app;
    }
}

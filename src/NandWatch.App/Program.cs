using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using NandWatch.App.State;
using Windows.ApplicationModel.Activation;

namespace NandWatch.App;

/// <summary>
/// The entry point of one exe that runs as two processes. The collector is
/// its own exe (NandWatchCollector).
/// </summary>
/// <remarks>
/// <para><b>The window</b> (the default, and what a toast's click or a
/// <c>nandwatch:</c> link starts): the WinUI app, gone when it closes. A
/// single instance: a second launch brings the open one forward.</para>
/// <para><b>The tray</b> (<c>--tray</c>): the optional icon, on a plain
/// message loop, with no WinUI in it (see <see cref="TrayProcess"/>). Started
/// by the Run key at sign-in, or by the window when "Keep in the tray when
/// closed" is on (<c>--tray --for-window</c>).</para>
/// <para><c>--exit</c> is the tray's Exit reaching an open window through the
/// single-instance redirect; with no window open it does nothing.</para>
/// </remarks>
public static class Program
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        return args.Contains("--tray", StringComparer.OrdinalIgnoreCase) ? TrayMain(args) : WindowMain(args);
    }

    private static int WindowMain(string[] args)
    {
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey());
        if (!instance.IsCurrent)
        {
            // This process was started by the user, so it may bring a window
            // forward; the one it hands over to may not, unless told it can
            // (-1: any process).
            AllowSetForegroundWindow(-1);
            instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
            return 0;
        }
        // The tray's Exit, with no window to close.
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) return 0;

        instance.Activated += (_, e) => App.OnRedirected(e.Data is ILaunchActivatedEventArgs launch ? launch.Arguments : null);

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static int TrayMain(string[] args)
    {
        // One tray per data folder; a second has nothing to add.
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey() + ".Tray");
        if (!instance.IsCurrent) return 0;

        var ui = DispatcherQueueController.CreateOnCurrentThread().DispatcherQueue;
        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(ui));
        _ = new TrayProcess(ui, forWindow: args.Contains("--for-window", StringComparer.OrdinalIgnoreCase));
        ui.RunEventLoop();
        return 0;
    }

    /// <summary>
    /// One instance per data folder, not per machine: a copy pointed at the demo
    /// by NANDWATCH_DATA_DIR starts beside the real one instead of handing its
    /// launch to it. The tray's key and its pipe's name are built on this.
    /// </summary>
    internal static string InstanceKey()
    {
        if (Environment.GetEnvironmentVariable(Core.AppPaths.DataDirVariable) is not { Length: > 0 } dir) return "NandWatch.Main";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dir).ToUpperInvariant()));
        return "NandWatch." + Convert.ToHexString(hash, 0, 8);
    }
}

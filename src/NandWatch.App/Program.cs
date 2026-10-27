using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace NandWatch.App;

/// <summary>
/// A custom entry point, for a single instance: a second launch brings the
/// running copy forward. The collector is its own exe (NandWatchCollector), so
/// this one only ever starts the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey());
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(activation).AsTask().Wait();
            return 0;
        }

        instance.Activated += (_, _) => App.OnRedirected();

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// One instance per data folder, not per machine: a copy pointed at the demo
    /// by NANDWATCH_DATA_DIR starts beside the real one instead of handing its
    /// launch to it.
    /// </summary>
    private static string InstanceKey()
    {
        if (Environment.GetEnvironmentVariable(Core.AppPaths.DataDirVariable) is not { Length: > 0 } dir) return "NandWatch.Main";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dir).ToUpperInvariant()));
        return "NandWatch." + Convert.ToHexString(hash, 0, 8);
    }
}

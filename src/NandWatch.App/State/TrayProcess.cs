using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using NandWatch.Core.View;

namespace NandWatch.App.State;

/// <summary>
/// The tray process (<c>NandWatch.exe --tray</c>): the optional icon, its
/// tooltip and menu, and the stall check. It is what stays when the window
/// closes, so it never loads WinUI.
/// </summary>
/// <remarks>
/// <para><b>Why its own process:</b> WinUI cannot be unloaded once loaded, so
/// the window hidden to the tray still held 138 MB private (2026-10-27). The
/// window is the default launch, as it always was, and ends when it closes;
/// this process exists only while the owner wants the tray. Don't create a
/// WinUI object (anything under <c>Microsoft.UI.Xaml</c>) from what this
/// class touches. The pattern is AI Usage Native's.</para>
/// <para><b>Two ways it starts.</b> At sign-in, from the Run key
/// (<c>--tray</c>): it stays until Exit. Or from the window, when "Keep in
/// the tray when closed" is on (<c>--tray --for-window</c>): it leaves when
/// that is turned off.</para>
/// <para>It reads the history for itself (<see cref="AppState"/>, which has
/// no WinUI in it), so nothing about the drives crosses
/// <see cref="TrayLink"/>.</para>
/// </remarks>
public sealed class TrayProcess : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly bool _forWindow;
    private readonly AppState _state = new();
    private readonly TrayHost _tray;
    private readonly DispatcherQueueTimer _stallTimer;
    private readonly CancellationTokenSource _stopping = new();
    // WinRT, not WinUI: reading Windows' app mode loads no XAML.
    private readonly global::Windows.UI.ViewManagement.UISettings _systemColors = new();
    private bool _syncing;

    public TrayProcess(DispatcherQueue ui, bool forWindow)
    {
        _ui = ui;
        _forWindow = forWindow;
        // Before the icon exists: its menu window takes the theme as it is made.
        ApplyMenuTheme();
        // "Use Windows setting" follows Windows while the tray runs, not only at its start.
        _systemColors.ColorValuesChanged += (_, _) => ui.TryEnqueue(ApplyMenuTheme);

        // The order and the shared wording are Onyx's (View/TrayMenu); this app
        // supplies its name, its one action and its notification toggle.
        var spec = new TrayMenuSpec("NAND Watch", OpenWindow, () => Exit(closeWindow: true), () => StartupRegistration.IsEnabled, ToggleStartAtLogin)
        {
            MainAction = ("Sync now", SyncNow),
            Toggles = [TrayItem.Check(UiSettings.NotifyLabel, () => UiSettings.Change(s => s.Notify = !s.Notify), () => UiSettings.Load().Notify)],
        };
        _tray = new TrayHost("NandWatch.Tray", spec, action => ui.TryEnqueue(() => action()));
        _state.Changed += () => _ = UpdateToolTipAsync();
        _state.Watch(ui);

        // On a clock only, never on starting: a PC that was simply off for
        // days has not stalled. The collector's own sign-in run comes first.
        _stallTimer = ui.CreateTimer();
        _stallTimer.Interval = TimeSpan.FromMinutes(10);
        _stallTimer.Tick += async (_, _) =>
        {
            await Reload();
            Stall.Check(_state.LastOk);
        };
        _stallTimer.Start();

        TrayLink.Serve(message => ui.TryEnqueue(() => Handle(message)), _stopping.Token);
        _ = Reload();
    }

    private async Task Reload()
    {
        try
        {
            await _state.ReloadAsync();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            // Mid-write, or the drive is gone: the next change tries again.
        }
    }

    private void ApplyMenuTheme() => MenuTheme.Apply(MenuTheme.IsLight(UiSettings.Load().Theme, _systemColors));

    private static void ToggleStartAtLogin()
    {
        try
        {
            StartupRegistration.Set(!StartupRegistration.IsEnabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The tick is read back from the registry, so it shows what is true.
        }
    }

    private async Task UpdateToolTipAsync()
    {
        try
        {
            _tray.ShowStatus([.. await _state.TrayLinesAsync()]);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
        }
    }

    private void Handle(string message)
    {
        switch (message)
        {
            case "settings":
                if (_forWindow && !UiSettings.Load().CloseToTray)
                {
                    Exit(closeWindow: false);
                    return;
                }
                // A toggle or the theme may have been changed in the window.
                _tray.SyncChecks();
                ApplyMenuTheme();
                // The setup screen may have chosen the data folder since.
                _state.Open();
                _state.Watch(_ui);
                _ = Reload();
                break;
            case "exit":
                Exit(closeWindow: false);
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// Opens the window: the app itself, which hands the launch to a window
    /// already open. Windows gives the foreground to the process the user
    /// last acted on, which is this one (a click on the icon), so the right
    /// is passed on (-1: to any process), or the window would open behind
    /// whatever is in front.
    /// </summary>
    private void OpenWindow()
    {
        AllowSetForegroundWindow(-1);
        Start("");
    }

    private static void Start(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Sync now: one collector run in this process, with the backup, as the
    /// window's button does. A run is small (about 37 MB at its peak, measured
    /// 2026-10-27) and nothing of it is kept. A failure waits in Sync Status.
    /// </summary>
    private async void SyncNow()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            await _state.CollectAsync("sync", backup: true);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Exit(bool closeWindow)
    {
        // A launch carrying --exit never becomes a window (see Program).
        if (closeWindow) Start("--exit");
        Dispose();
        Environment.Exit(0);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stallTimer.Stop();
        _state.Dispose();
        _tray.Dispose();
    }
}

using System.Drawing;
using H.NotifyIcon.Core;
using NandWatch.Core.View;

namespace NandWatch.App.State;

/// <summary>
/// The notification-area icon for any app: left-click or double-click opens
/// the window, right-click shows the standard menu from
/// <see cref="TrayMenu.Build"/>. Lifted from Data Usage Native's TrayHost and
/// made generic; the menu's order and wording are no longer the app's to choose.
/// </summary>
/// <remarks>
/// Needs the H.NotifyIcon package. Copy this file and MenuTheme.cs into the
/// app's State folder and change the namespace. Compiled here by Onyx.WinUI.csproj.
/// </remarks>
public sealed class TrayHost : IDisposable
{
    private readonly TrayIconWithContextMenu _tray;
    private readonly Icon _icon;
    private readonly string _appName;
    private readonly List<(PopupMenuItem Item, Func<bool> Checked)> _toggles = [];

    /// <param name="trayId">A short fixed name for this app, for example "DataUsageNative.Tray".</param>
    /// <param name="onUi">Marshals a menu click, which arrives on the icon's thread, onto the UI thread.</param>
    public TrayHost(string trayId, TrayMenuSpec spec, Action<Action> onUi)
    {
        _appName = spec.AppName;
        _icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), 16, 16);

        // The id is per exe path. Windows binds a tray icon's id to the exe
        // that first registered it and silently refuses it from any other, so
        // a Debug build run from the repo would leave the installed app with
        // no tray icon at all (rulebook: traps, "Windows and PowerShell").
        _tray = new TrayIconWithContextMenu($"{trayId}|{Environment.ProcessPath}")
        {
            Icon = _icon.Handle,
            ToolTip = spec.AppName,
        };

        var menu = new PopupMenu();
        foreach (var item in TrayMenu.Build(spec))
        {
            switch (item.Kind)
            {
                case TrayItemKind.Separator:
                    menu.Items.Add(new PopupMenuSeparator());
                    break;
                case TrayItemKind.Toggle:
                    PopupMenuItem? toggle = null;
                    toggle = new PopupMenuItem(item.Text, (_, _) => onUi(() =>
                    {
                        item.Run!();
                        toggle!.Checked = item.Checked!();
                    }))
                    { Checked = item.Checked!() };
                    _toggles.Add((toggle, item.Checked!));
                    menu.Items.Add(toggle);
                    break;
                default:
                    // The first item is what a double-click runs.
                    menu.Items.Add(new PopupMenuItem(item.Text, (_, _) => onUi(item.Run!)));
                    break;
            }
        }
        _tray.ContextMenu = menu;

        _tray.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent is MouseEvent.IconLeftMouseUp or MouseEvent.IconDoubleClick) onUi(spec.Open);
        };
        _tray.Create();
        // Without this the right-click menu ignores the app's light or dark theme.
        MenuTheme.Allow(_tray.MessageWindow.Handle);
    }

    /// <summary>Re-reads every toggle, after a setting changed somewhere else.</summary>
    public void SyncChecks()
    {
        foreach (var (item, isChecked) in _toggles) item.Checked = isChecked();
    }

    /// <summary>The app name, then up to a few lines of live status. The shell cuts a tooltip at 127 characters.</summary>
    public void ShowStatus(params string[] lines)
    {
        var text = string.Join("\n", new[] { _appName }.Concat(lines.Where(l => l.Length > 0)));
        _tray.UpdateToolTip(text.Length > 127 ? text[..127] : text);
    }

    public void Notify(string title, string message, bool warning = false) =>
        _tray.ShowNotification(title, message, warning ? NotificationIcon.Warning : NotificationIcon.Info, null, false, false, false, false, null);

    public void Dispose()
    {
        _tray.Dispose();
        _icon.Dispose();
    }
}

using System.Drawing;
using H.NotifyIcon.Core;

namespace NandWatch.App.State;

/// <summary>
/// The notification-area icon, when the owner turns it on (off by default):
/// each drive's status in its tooltip, a click to open, a menu with Sync now.
/// Notifications do not depend on it; the collector shows its own.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly TrayIconWithContextMenu _tray;
    private readonly Icon _icon;

    public TrayHost(Action<Action> onUi, Action open, Action syncNow, Action exit)
    {
        _icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), 16, 16);
        _tray = new TrayIconWithContextMenu("NandWatch.Tray")
        {
            Icon = _icon.Handle,
            ToolTip = "NAND Watch",
            ContextMenu = new PopupMenu
            {
                Items =
                {
                    new PopupMenuItem("Open NAND Watch", (_, _) => onUi(open)),
                    new PopupMenuItem("Sync now", (_, _) => onUi(syncNow)),
                    new PopupMenuSeparator(),
                    new PopupMenuItem("Exit", (_, _) => onUi(exit)),
                },
            },
        };
        _tray.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent is MouseEvent.IconLeftMouseUp or MouseEvent.IconDoubleClick) onUi(open);
        };
        _tray.Create();
    }

    /// <summary>"NAND Watch", then a line per drive. The shell cuts a tray tooltip at 127 characters.</summary>
    public void SetToolTip(IEnumerable<string> lines)
    {
        var text = string.Join("\n", new[] { "NAND Watch" }.Concat(lines));
        _tray.UpdateToolTip(text.Length > 127 ? text[..127] : text);
    }

    public void Dispose()
    {
        _tray.Dispose();
        _icon.Dispose();
    }
}

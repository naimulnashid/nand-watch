namespace NandWatch.Core.View;

/// <summary>What one row of a tray menu is.</summary>
public enum TrayItemKind { Action, Toggle, Separator }

/// <summary>
/// One row. <c>Checked</c> is read when the menu is built and when it reopens.
/// </summary>
public sealed record TrayItem(TrayItemKind Kind, string Text = "", Action? Run = null, Func<bool>? Checked = null)
{
    public static TrayItem Do(string text, Action run) => new(TrayItemKind.Action, text, run);
    public static TrayItem Check(string text, Action toggle, Func<bool> isChecked) => new(TrayItemKind.Toggle, text, toggle, isChecked);
    public static readonly TrayItem Line = new(TrayItemKind.Separator);
}

/// <summary>What an app supplies. Everything else about the menu is fixed.</summary>
public sealed record TrayMenuSpec(
    string AppName,
    Action Open,
    Action Exit,
    Func<bool> StartsAtLogin,
    Action ToggleStartAtLogin)
{
    /// <summary>The one thing the app does on demand: "Sync now" or "Refresh now". Null if there is none.</summary>
    public (string Text, Action Run)? MainAction { get; init; }

    /// <summary>Items only this app has (display options, a reset). May be empty.</summary>
    public IReadOnlyList<TrayItem> AppItems { get; init; } = [];

    /// <summary>Further toggles after "Start at login": auto-refresh, then notifications.</summary>
    public IReadOnlyList<TrayItem> Toggles { get; init; } = [];
}

/// <summary>
/// The one order every tray menu has (rulebook: platforms, "Tray menu";
/// decided 2026-11-12):
/// <code>
/// Open &lt;App name&gt;            also the double-click action
/// &lt;Main action&gt;              "Sync now" or "Refresh now"
/// -----------------------
/// &lt;App-specific items&gt;
/// -----------------------
/// [x] Start at login
/// [x] further toggles
/// -----------------------
/// Exit
/// </code>
/// A section with no items is left out together with its separator.
/// </summary>
public static class TrayMenu
{
    public const string StartAtLogin = "Start at login";
    public const string ExitText = "Exit";

    public static string OpenText(string appName) => $"Open {appName}";

    public static List<TrayItem> Build(TrayMenuSpec spec)
    {
        var top = new List<TrayItem> { TrayItem.Do(OpenText(spec.AppName), spec.Open) };
        if (spec.MainAction is { } main) top.Add(TrayItem.Do(main.Text, main.Run));

        var app = spec.AppItems.Where(i => i.Kind != TrayItemKind.Separator).ToList();

        var toggles = new List<TrayItem> { TrayItem.Check(StartAtLogin, spec.ToggleStartAtLogin, spec.StartsAtLogin) };
        toggles.AddRange(spec.Toggles.Where(i => i.Kind == TrayItemKind.Toggle));

        var bottom = new List<TrayItem> { TrayItem.Do(ExitText, spec.Exit) };

        var menu = new List<TrayItem>();
        foreach (var section in new[] { top, app, toggles, bottom })
        {
            if (section.Count == 0) continue;
            if (menu.Count > 0) menu.Add(TrayItem.Line);
            menu.AddRange(section);
        }
        return menu;
    }
}

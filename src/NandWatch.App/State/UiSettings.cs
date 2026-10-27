using System.Text.Json;
using NandWatch.Core;

namespace NandWatch.App.State;

/// <summary>
/// The app's own preferences, in the local folder: cheap to lose, so they do
/// not belong with the history on the data drive. An unreadable file means
/// the defaults.
/// </summary>
public sealed class UiSettings
{
    /// <summary>"dark", "light" or "system". Dark by default; system follows Windows' app mode.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Page zoom, 0.5 to 2: Ctrl+Plus / Ctrl+Minus / Ctrl+0, as in a browser.</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>
    /// Alerts as Windows notifications. On by default. The collector reads
    /// this same key (<see cref="Core.Preferences.Notify"/>), so the name is a
    /// contract between the two.
    /// </summary>
    public bool Notify { get; set; } = true;

    /// <summary>Closing the window leaves the app in the tray. Off by default (the owner's decision): nothing needs to stay resident.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>The last good run a stall was already announced for, so a stall is announced once.</summary>
    public DateTimeOffset? NotifiedStallSince { get; set; }

    private static string FilePath => Path.Combine(AppPaths.LocalDir, "app-settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath)) ?? new UiSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that did not stick is not worth an error.
        }
    }
}

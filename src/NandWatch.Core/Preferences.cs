using System.Text.Json;

namespace NandWatch.Core;

/// <summary>
/// The few app preferences the collector must honour too, read from the app's
/// own settings file (<c>app-settings.json</c> in the local folder, written by
/// the app). A missing or unreadable file means the defaults.
/// </summary>
public static class Preferences
{
    public static string FilePath => Path.Combine(AppPaths.LocalDir, "app-settings.json");

    /// <summary>Whether alerts are shown as Windows notifications. On by default; they are kept in the history either way.</summary>
    public static bool Notify => ReadBool("Notify") ?? true;

    private static bool? ReadBool(string name)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v)
                && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

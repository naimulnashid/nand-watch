using System.Text.Json;
using System.Text.Json.Nodes;

namespace NandWatch.Core;

/// <summary>
/// Where everything lives. Two places, on purpose:
/// <list type="bullet">
/// <item><b>The data folder</b> (default <see cref="DefaultDataDir"/>) holds
/// the history and its backup. It must not be on the system drive: the drives'
/// own counters survive a Windows reset, but the record of how they changed
/// over time does not, unless it lives elsewhere.</item>
/// <item><b>The local folder</b> (<c>%LOCALAPPDATA%\NAND Watch</c>) holds what
/// is cheap to lose: the pointer to the data folder and the logs.</item>
/// </list>
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "NANDWATCH_DATA_DIR";
    public const string LocalDirVariable = "NANDWATCH_LOCAL_DIR";

    /// <summary>What the installer offers: this machine's synced data root.</summary>
    public const string DefaultDataDir = @"D:\PersistentData\nand-watch";

    public static string LocalDir => Environment.GetEnvironmentVariable(LocalDirVariable) is { Length: > 0 } local
        ? Path.GetFullPath(local)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAND Watch");

    public static string LocationFile => Path.Combine(LocalDir, "location.json");

    public static string LogsDir => Path.Combine(LocalDir, "logs");

    /// <summary>
    /// The data folder, or null when none has been chosen (before the first
    /// install, or after a reset). <see cref="DataDirVariable"/> overrides it,
    /// which demo and test runs rely on.
    /// </summary>
    public static string? DataDir => Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } env
        ? Path.GetFullPath(env)
        : ReadLocation().DataDir;

    /// <summary>
    /// What the elevated RST reader writes, in a folder only administrators
    /// can write and the user can read (tools\Register-RstReader.ps1 makes it).
    /// NANDWATCH_RST_FILE points elsewhere, for tests and demos.
    /// </summary>
    public static string RstReport => Environment.GetEnvironmentVariable("NANDWATCH_RST_FILE") is { Length: > 0 } file
        ? Path.GetFullPath(file)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NAND Watch", "rst", "rst-health.json");

    public static string DatabasePath(string dataDir) => Path.Combine(dataDir, "live", "nand-watch.db");

    /// <summary>
    /// The restore source. Written with SQLite's backup API, so it is one
    /// consistent file, unlike the live database, whose WAL sidecars a sync
    /// client can upload out of step with it.
    /// </summary>
    public static string BackupPath(string dataDir) => Path.Combine(dataDir, "nand-watch.db");

    public sealed record Location(string? DataDir, bool AllowSystemDrive);

    public static Location ReadLocation()
    {
        try
        {
            if (!File.Exists(LocationFile)) return new(null, false);
            var node = JsonNode.Parse(File.ReadAllText(LocationFile));
            var dir = node?["dataDir"]?.GetValue<string>();
            var allow = node?["allowSystemDrive"]?.GetValue<bool>() ?? false;
            return new(string.IsNullOrWhiteSpace(dir) ? null : dir, allow);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(null, false);
        }
    }

    /// <summary>The setup's deliberate tick, or a demo or test run pointed elsewhere by the variable.</summary>
    public static bool AllowSystemDrive => ReadLocation().AllowSystemDrive || Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 };

    public static void WriteLocation(string dataDir, bool allowSystemDrive)
    {
        Directory.CreateDirectory(LocalDir);
        var json = new JsonObject { ["dataDir"] = Path.GetFullPath(dataDir), ["allowSystemDrive"] = allowSystemDrive };
        File.WriteAllText(LocationFile, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>True when <paramref name="path"/> is on the drive Windows is installed on (never, off Windows).</summary>
    public static bool IsOnSystemDrive(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0) return false;
        return string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Path.GetPathRoot(windows), StringComparison.OrdinalIgnoreCase);
    }
}

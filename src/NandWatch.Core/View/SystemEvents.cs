namespace NandWatch.Core.View;

/// <summary>
/// What the System-log events this app keeps mean, in words, and which of them
/// are Windows' record of an unexpected shutdown. Wording follows Microsoft's
/// own event messages, shortened; an event not listed is named by its number.
/// </summary>
public static class SystemEvents
{
    /// <summary>The providers the collector copies, at warning level or worse.</summary>
    public static readonly string[] StorageProviders =
        ["disk", "stornvme", "storahci", "iaStorVD", "iaStorAC", "Ntfs", "Microsoft-Windows-Ntfs", "volmgr", "partmgr", "Microsoft-Windows-StorPort"];

    public const string KernelPower = "Microsoft-Windows-Kernel-Power";
    public const string EventLog = "EventLog";

    /// <summary>Kernel-Power 41 and EventLog 6008: Windows did not shut down cleanly. Logged at the next start.</summary>
    public static bool IsUnexpectedShutdown(string provider, int id) =>
        (provider == KernelPower && id == 41) || (provider == EventLog && id == 6008);

    public static string Describe(string provider, int id) => (provider, id) switch
    {
        ("disk", 7) => "Bad block",
        ("disk", 11) => "Controller error",
        ("disk", 15) => "Device not ready",
        ("disk", 51) => "Error during paging",
        ("disk", 52) => "The drive predicts it will fail",
        ("disk", 153) => "I/O retried",
        ("disk", 154) => "I/O failed: hardware error",
        ("disk", 157) => "Surprise removal",
        ("stornvme" or "storahci" or "iaStorVD" or "iaStorAC", 11) => "Controller error",
        ("stornvme" or "storahci" or "iaStorVD" or "iaStorAC" or "Microsoft-Windows-StorPort", 129) => "Reset issued to the device",
        ("Ntfs" or "Microsoft-Windows-Ntfs", 55) => "File system corruption found",
        ("Ntfs" or "Microsoft-Windows-Ntfs", 130) => "File system repaired",
        ("Ntfs" or "Microsoft-Windows-Ntfs", 137) => "Transaction log error",
        ("Ntfs" or "Microsoft-Windows-Ntfs", 140) => "Failed to flush data; corruption may occur",
        ("volmgr", 46) => "Crash dump setup failed",
        ("volmgr", 161) => "Crash dump creation failed",
        ("partmgr", 58) => "Two disks share a signature",
        (KernelPower, 41) => "Restarted without a clean shutdown",
        (EventLog, 6008) => "The previous shutdown was unexpected",
        _ => $"{provider} event {id}",
    };

    public static string Level(int level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        _ => "Information",
    };
}

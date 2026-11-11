namespace NandWatch.App.State;

/// <summary>
/// A stall, announced once: no good run for six hours while the app is
/// running (the window or the tray). The hourly task catches up after sleep,
/// so six hours means it is not running at all. The collector cannot say this
/// itself.
/// </summary>
public static class Stall
{
    /// <summary>
    /// The window and the tray are two processes and both check, so what has
    /// been announced is read from the file each time, not from a copy made
    /// when the process started: whichever sees the stall first writes it,
    /// and the other finds it there.
    /// </summary>
    public static void Check(DateTimeOffset? lastOk)
    {
        if (lastOk is not { } last || DateTimeOffset.UtcNow - last < TimeSpan.FromHours(6)) return;
        var settings = UiSettings.Load();
        if (!settings.Notify || settings.NotifiedStallSince == last) return;
        settings.NotifiedStallSince = last;
        settings.Save(keepStallMark: false);
        NandWatch.Windows.Notify.Toasts.Show($"NAND Watch has not read the drives for {(int)(DateTimeOffset.UtcNow - last).TotalHours} hours",
            "The hourly task may have stopped. Open Sync Status to see why.");
    }
}

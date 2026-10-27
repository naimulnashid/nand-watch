using System.Runtime.InteropServices;

namespace NandWatch.Windows.Collect;

/// <summary>What Task Scheduler knows about the collector's task. Times are UTC.</summary>
public sealed record TaskInfo(bool Exists, bool Enabled, bool Running, DateTimeOffset? LastRun, int LastResult, DateTimeOffset? NextRun);

/// <summary>
/// The hourly task, read through Task Scheduler's own COM API (Data Usage
/// Native's way) rather than by parsing schtasks' output. Reading it needs no
/// elevation.
/// </summary>
public static class CollectorTask
{
    /// <summary>As tools\Register-Tasks.ps1 names it.</summary>
    public const string Name = "NAND Watch Collector";

    /// <summary>The elevated RST reader's task (tools\Register-RstReader.ps1), when it is installed.</summary>
    public const string RstReaderName = "NAND Watch RST Reader";

    private const int StateDisabled = 1, StateRunning = 4;

    /// <summary>A task's state; null when Task Scheduler itself could not be asked.</summary>
    public static TaskInfo? Query(string name = Name)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return null;
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            dynamic task;
            try { task = service.GetFolder(@"\").GetTask(name); }
            catch (Exception ex) when (ex is COMException or FileNotFoundException) { return new TaskInfo(false, false, false, null, 0, null); }
            static DateTimeOffset? Time(DateTime t) => t.Year < 2000 ? null : new DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero);
            var state = (int)task.State;
            return new TaskInfo(true, state != StateDisabled && (bool)task.Enabled, state == StateRunning,
                Time((DateTime)task.LastRunTime), (int)task.LastTaskResult, Time((DateTime)task.NextRunTime));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a registered task now. Needs no elevation, even for the RST
    /// reader, which runs as SYSTEM: its registration lets the user run it.
    /// Returns null when it started, else why not.
    /// </summary>
    public static string? Run(string name)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return "Task Scheduler is not available.";
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            service.GetFolder(@"\").GetTask(name).Run(null);
            return null;
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return ex is FileNotFoundException || (ex is COMException c && c.HResult == unchecked((int)0x80070002))
                ? $"The '{name}' task is not registered."
                : ex.Message;
        }
    }
}

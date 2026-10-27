using WindowsCollector = NandWatch.Windows.Collect.Collector;
using NandWatch.Windows.Collect;

namespace NandWatch.Collector;

/// <summary>
/// What the scheduled task runs, hourly and at sign-in:
/// <c>NandWatchCollector.exe --trigger scheduled</c>. One collector run, then exit. Its line in
/// <c>logs\collector.log</c> and its row in the run log are the only output.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var i = Array.IndexOf(args, "--trigger");
        var trigger = i >= 0 && i + 1 < args.Length ? args[i + 1] : "manual";
        return WindowsCollector.Run(new CollectOptions(trigger, args.Contains("--backup"), args.Contains("--retry-refused"))).ExitCode;
    }
}

using System.IO.Pipes;
using System.Text;

namespace NandWatch.App.State;

/// <summary>
/// The window's line to the tray process: a named pipe, one message per
/// connection. The pattern is AI Usage Native's.
/// </summary>
/// <remarks>
/// <para><c>settings</c>: app-settings.json or the data folder changed (the
/// tray leaves if it was only there for "Keep in the tray when closed" and
/// that is now off). It doubles as "is a tray running?": the send fails when
/// none is.</para>
/// <para><c>exit</c>: Exit was chosen in the window; the tray goes too.</para>
/// <para>Nothing about the drives crosses it: each process reads the history
/// for itself. The other direction needs no pipe: the tray starts the app
/// again, and the single-instance redirect hands that to the open window
/// (see <see cref="Program"/>).</para>
/// <para>The name carries <see cref="Program.InstanceKey"/>, so a demo copy
/// on its own data folder never talks to the real tray.</para>
/// </remarks>
public static class TrayLink
{
    private static string Name => Program.InstanceKey() + ".Tray";

    /// <summary>Listens until <paramref name="stop"/>; <paramref name="handle"/> runs on the listening thread.</summary>
    public static void Serve(Action<string> handle, CancellationToken stop) => new Thread(() =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.WaitForConnectionAsync(stop).GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                if (reader.ReadLine() is { Length: > 0 } line) handle(line);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that hung up mid-line: wait for the next one.
            }
        }
    })
    { IsBackground = true, Name = "tray-link" }.Start();

    /// <summary>Sends one message to the tray. False when no tray is listening.</summary>
    public static bool Send(string message)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(500);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(message);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

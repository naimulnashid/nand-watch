using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NandWatch.RstReader;

/// <summary>
/// <c>NandWatchRstReader.exe --out &lt;folder&gt;</c>: every NVMe drive's health
/// log, read through Intel RST, into <c>&lt;folder&gt;\rst-health.json</c>.
/// </summary>
/// <remarks>
/// <para>Run as SYSTEM by the "NAND Watch RST Reader" task, once a day and
/// from the app's button; its arguments are fixed when an administrator
/// registers the task, and it reads nothing a user can write. It decodes
/// nothing either: the descriptor and the page go out raw, and the
/// unelevated collector parses them (Core's DeviceDescriptor and HealthLog).</para>
/// <para>Per drive: Windows' device descriptor (to say which drive it is,
/// and to skip anything that is not NVMe), its SCSI address (the RST port and
/// path), then the one pass-through request. Run by hand from an elevated
/// terminal to check what it reads, with <c>nandwatch rst-show</c>.</para>
/// </remarks>
public static class Program
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlScsiGetAddress = 0x00041018;
    private const uint IoctlScsiMiniport = 0x0004D008;

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
    private const int BusNvme = 17;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    public static int Main(string[] args)
    {
        var i = Array.IndexOf(args, "--out");
        if (i < 0 || i + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: NandWatchRstReader --out <folder>");
            return 2;
        }
        var outDir = Path.GetFullPath(args[i + 1]);
        var started = DateTimeOffset.UtcNow;
        var drives = new List<Dictionary<string, object?>>();
        for (var n = 0; n < 32; n++)
            if (ReadDrive(n) is { } d) drives.Add(d);

        Directory.CreateDirectory(outDir);
        var file = Path.Combine(outDir, "rst-health.json");
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["startedUtc"] = started.ToString("O"),
            ["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["drives"] = drives,
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, file, overwrite: true);
        var read = drives.Count(d => d["ok"] is true);
        Console.WriteLine($"{read} of {drives.Count} NVMe drive(s) read through Intel RST into {file}.");
        return read > 0 || drives.Count == 0 ? 0 : 1;
    }

    /// <summary>One drive; null when there is no such drive or it is not NVMe.</summary>
    private static Dictionary<string, object?>? ReadDrive(int n)
    {
        byte[] descriptor;
        int port, path, target, lun;
        using (var disk = CreateFile($@"\\.\PhysicalDrive{n}", 0, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
        {
            if (disk.IsInvalid) return null;
            // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0), PropertyStandardQuery (0).
            var query = new byte[12];
            var reply = new byte[1024];
            if (!DeviceIoControl(disk, IoctlStorageQueryProperty, query, query.Length, reply, reply.Length, out var got, IntPtr.Zero) || got < 36) return null;
            descriptor = reply[..got];
            if (BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(28)) != BusNvme) return null;

            // SCSI_ADDRESS: length, port, path, target, lun.
            var address = new byte[8];
            if (!DeviceIoControl(disk, IoctlScsiGetAddress, null, 0, address, address.Length, out _, IntPtr.Zero))
                return Result(n, descriptor, null, Marshal.GetLastPInvokeError());
            (port, path, target, lun) = (address[4], address[5], address[6], address[7]);
        }

        var entry = Result(n, descriptor, (port, path, target, lun), 0);
        using var adapter = CreateFile($@"\\.\Scsi{port}:", GenericRead | GenericWrite, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (adapter.IsInvalid)
        {
            entry["error"] = Marshal.GetLastPInvokeError();
            entry["detail"] = new Win32Exception((int)entry["error"]!).Message;
            return entry;
        }
        var buffer = RstRequest.HealthLog((byte)path);
        var ok = DeviceIoControl(adapter, IoctlScsiMiniport, buffer, buffer.Length, buffer, buffer.Length, out var returned, IntPtr.Zero);
        entry["ok"] = ok;
        entry["returned"] = returned;
        entry["error"] = ok ? 0 : Marshal.GetLastPInvokeError();
        if (!ok) entry["detail"] = new Win32Exception((int)entry["error"]!).Message;
        entry["srbReturnCode"] = RstRequest.ReturnCode(buffer);
        entry["completion"] = RstRequest.Completion(buffer);
        if (ok) entry["page"] = Convert.ToBase64String(RstRequest.Page(buffer));
        return entry;
    }

    private static Dictionary<string, object?> Result(int n, byte[] descriptor, (int Port, int Path, int Target, int Lun)? at, int error) => new()
    {
        ["disk"] = n,
        ["descriptor"] = Convert.ToBase64String(descriptor),
        ["port"] = at?.Port,
        ["path"] = at?.Path,
        ["target"] = at?.Target,
        ["lun"] = at?.Lun,
        ["ok"] = false,
        ["error"] = error,
        ["detail"] = error == 0 ? null : new Win32Exception(error).Message,
    };
}

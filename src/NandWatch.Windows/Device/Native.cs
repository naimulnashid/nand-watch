using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NandWatch.Windows.Device;

/// <summary>
/// The Windows calls the drive reader makes. Only queries: see
/// ReadOnlyGuardTests for the list of IOCTL codes this file may hold.
/// </summary>
internal static class Native
{
    internal const uint IoctlStorageQueryProperty = Core.Nvme.StorageQuery.IoctlStorageQueryProperty;
    internal const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    internal const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    /// <summary>
    /// No access rights at all: enough for FILE_ANY_ACCESS queries, and the
    /// one way a standard user can open a physical drive. GENERIC_READ is
    /// tried only to learn whether it changes what the driver answers.
    /// </summary>
    internal const uint NoAccess = 0;
    internal const uint GenericRead = 0x80000000;

    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    internal const int ErrorFileNotFound = 2;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryDosDeviceW")]
    private static extern int QueryDosDevice(string name, char[] target, int max);

    /// <summary>What a drive letter points at, e.g. \Device\HarddiskVolume3; null if Windows will not say.</summary>
    internal static string? DosTarget(string letter)
    {
        var buffer = new char[1024];
        var n = QueryDosDevice(letter, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end and >= 0 ? end : n) : null;
    }

    /// <summary>Opens a device path; the handle is invalid and the error set when it fails.</summary>
    internal static SafeFileHandle Open(string path, uint access, out int error)
    {
        var handle = CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        return handle;
    }

    internal static bool StorageQuery(SafeFileHandle device, byte[] query, byte[] output, out int returned, out int error) =>
        Call(DeviceIoControl(device, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out returned, IntPtr.Zero), out error);

    /// <summary>The protocol queries use one buffer for the request and the reply, as Microsoft's sample does.</summary>
    internal static bool StorageQueryInPlace(SafeFileHandle device, byte[] buffer, out int returned, out int error) =>
        Call(DeviceIoControl(device, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out returned, IntPtr.Zero), out error);

    internal static bool DeviceNumber(SafeFileHandle device, byte[] output, out int returned, out int error) =>
        Call(DeviceIoControl(device, IoctlStorageGetDeviceNumber, null, 0, output, output.Length, out returned, IntPtr.Zero), out error);

    internal static bool DiskExtents(SafeFileHandle volume, byte[] output, out int returned, out int error) =>
        Call(DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents, null, 0, output, output.Length, out returned, IntPtr.Zero), out error);

    private static bool Call(bool ok, out int error)
    {
        error = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }
}

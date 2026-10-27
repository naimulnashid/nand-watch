using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using NandWatch.Core.Nvme;

namespace NandWatch.Windows.Device;

/// <summary>What one read returned: the data, or the Windows error and its text.</summary>
public sealed record ReadResult(bool Ok, byte[] Data, int Error, string Detail)
{
    public static ReadResult From(byte[] data) => new(true, data, 0, "");

    public static ReadResult Failed(int error, string? detail = null) =>
        new(false, [], error, detail ?? $"{error}: {new Win32Exception(error).Message.TrimEnd('.')}");
}

/// <summary>
/// One physical drive, opened through \\.\PhysicalDriveN. Sends only the
/// queries in <see cref="NvmeRead"/> and the descriptor queries.
/// </summary>
public sealed class NvmeReader : IDisposable
{
    private readonly SafeFileHandle _handle;

    private NvmeReader(int number, SafeFileHandle handle, uint access)
    {
        Number = number;
        _handle = handle;
        Access = access;
    }

    public int Number { get; }

    /// <summary>The access the handle was opened with: 0 (none) or GENERIC_READ.</summary>
    public uint Access { get; }

    /// <summary>No access rights: all a standard user can open a physical drive with.</summary>
    public const uint NoAccess = Native.NoAccess;

    /// <summary>How far to look. Drive numbers can have gaps, so a missing one is not the end.</summary>
    public const int MaxDrives = 32;

    public static string PathOf(int number) => $@"\\.\PhysicalDrive{number}";

    /// <summary>True unless Windows says there is no such drive; access denied still means it exists.</summary>
    public static bool Exists(int number)
    {
        using var reader = Open(number, NoAccess, out var error);
        return reader is not null || error != Native.ErrorFileNotFound;
    }

    /// <summary>Opens the drive with the given access, or returns the Windows error.</summary>
    public static NvmeReader? Open(int number, uint access, out int error)
    {
        var handle = Native.Open(PathOf(number), access, out error);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new NvmeReader(number, handle, access);
    }

    public ReadResult Descriptor(StorageProperty property)
    {
        var output = new byte[8192];
        return Native.StorageQuery(_handle, StorageQuery.Standard(property), output, out var returned, out var error)
            ? ReadResult.From(output[..returned])
            : ReadResult.Failed(error);
    }

    public ReadResult Read(StorageProperty scope, NvmeRead read)
    {
        var buffer = StorageQuery.Protocol(scope, read);
        if (!Native.StorageQueryInPlace(_handle, buffer, out var returned, out var error)) return ReadResult.Failed(error);
        var reply = StorageQuery.ReadProtocolReply(buffer, returned);
        return reply.Ok ? ReadResult.From(reply.Data) : ReadResult.Failed(-1, reply.Error);
    }

    /// <summary>STORAGE_DEVICE_NUMBER.DeviceNumber: should equal the N in PhysicalDriveN.</summary>
    public int? DeviceNumber()
    {
        var output = new byte[12];
        return Native.DeviceNumber(_handle, output, out var returned, out _) && returned >= 8 ? BitConverter.ToInt32(output, 4) : null;
    }

    public void Dispose() => _handle.Dispose();
}

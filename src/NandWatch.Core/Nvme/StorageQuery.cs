using System.Buffers.Binary;

namespace NandWatch.Core.Nvme;

/// <summary>The STORAGE_PROPERTY_ID values this app asks for (winioctl.h).</summary>
public enum StorageProperty
{
    Device = 0,
    Adapter = 1,
    AdapterProtocolSpecific = 49,
    DeviceProtocolSpecific = 50,
    DeviceTemperature = 52,
    DeviceUnsafeShutdownCount = 61,
    DeviceEndurance = 62,
}

/// <summary>
/// One NVMe read the app may send. The constructor is private: the only
/// requests that exist are the members below, all Identify or Get Log Page.
/// </summary>
/// <remarks>
/// <b>Read-only by construction.</b> Windows passes these to the drive as
/// Identify and Get Log Page admin commands, which read and change nothing.
/// There is deliberately no way to express a Get/Set Features request, and the
/// app never uses IOCTL_STORAGE_PROTOCOL_COMMAND (raw pass-through), which can
/// send anything, format and firmware commands included. A test enforces both.
/// </remarks>
public sealed record NvmeRead
{
    private NvmeRead(int dataType, uint requestValue, int length, string name)
    {
        DataType = dataType;
        RequestValue = requestValue;
        Length = length;
        Name = name;
    }

    /// <summary>STORAGE_PROTOCOL_NVME_DATA_TYPE: 1 Identify, 2 log page.</summary>
    public int DataType { get; }

    /// <summary>The CNS for Identify, the log identifier for a log page.</summary>
    public uint RequestValue { get; }

    public int Length { get; }
    public string Name { get; }

    public static NvmeRead IdentifyController { get; } = new(1, Nvme.IdentifyController.Cns, Nvme.IdentifyController.Size, "identify-controller");
    public static NvmeRead HealthLog { get; } = new(2, Nvme.HealthLog.LogId, Nvme.HealthLog.Size, "health");

    /// <summary>Standard log pages the probe reads: error information, firmware slots, commands supported, self-test.</summary>
    public static IReadOnlyList<NvmeRead> StandardLogs { get; } =
    [
        new(2, 0x01, 512, "log-01-error-information"),
        new(2, 0x03, 512, "log-03-firmware-slot"),
        new(2, 0x05, 4096, "log-05-commands-supported"),
        new(2, 0x06, 512, "log-06-self-test"),
    ];

    /// <summary>
    /// A vendor-specific log page (C0h to FFh), first 512 bytes. Still a Get
    /// Log Page, but what a vendor page holds is undocumented, so the probe
    /// reads these only when asked.
    /// </summary>
    public static NvmeRead VendorLog(byte id) =>
        id >= 0xC0 ? new(2, id, 512, $"log-{id:x2}-vendor") : throw new ArgumentOutOfRangeException(nameof(id), "Vendor log pages are C0h to FFh.");
}

/// <summary>
/// The byte layout of IOCTL_STORAGE_QUERY_PROPERTY's buffers. Pure: building
/// a request and reading a reply are tested here, and NandWatch.Windows only
/// hands the bytes to DeviceIoControl.
/// </summary>
public static class StorageQuery
{
    /// <summary>CTL_CODE(IOCTL_STORAGE_BASE, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS).</summary>
    public const uint IoctlStorageQueryProperty = 0x002D1400;

    /// <summary>FIELD_OFFSET(STORAGE_PROPERTY_QUERY, AdditionalParameters).</summary>
    private const int QueryHeader = 8;

    /// <summary>sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA): ten DWORDs.</summary>
    private const int ProtocolData = 40;

    /// <summary>Where the protocol data starts in the reply: after STORAGE_PROTOCOL_DATA_DESCRIPTOR's Version and Size.</summary>
    private const int ReplyHeader = 8;

    private const int ProtocolTypeNvme = 3;

    /// <summary>A standard query (PropertyStandardQuery) for a descriptor.</summary>
    public static byte[] Standard(StorageProperty property)
    {
        var buffer = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)property);
        return buffer;
    }

    /// <summary>
    /// One NVMe read, at adapter (controller) or device (namespace) scope. The
    /// same buffer carries the request in and the reply out, as in Microsoft's
    /// own sample, so it is sized for the reply.
    /// </summary>
    public static byte[] Protocol(StorageProperty scope, NvmeRead read)
    {
        if (scope is not (StorageProperty.AdapterProtocolSpecific or StorageProperty.DeviceProtocolSpecific))
            throw new ArgumentOutOfRangeException(nameof(scope), "An NVMe read is adapter- or device-protocol-specific.");
        var buffer = new byte[QueryHeader + ProtocolData + read.Length];
        var b = buffer.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(b, (int)scope);
        BinaryPrimitives.WriteInt32LittleEndian(b[4..], 0); // PropertyStandardQuery
        var p = b[QueryHeader..];
        BinaryPrimitives.WriteInt32LittleEndian(p, ProtocolTypeNvme);
        BinaryPrimitives.WriteInt32LittleEndian(p[4..], read.DataType);
        BinaryPrimitives.WriteUInt32LittleEndian(p[8..], read.RequestValue);
        BinaryPrimitives.WriteUInt32LittleEndian(p[12..], 0);              // sub-value: log offset 0 / NSID 0
        BinaryPrimitives.WriteInt32LittleEndian(p[16..], ProtocolData);    // data follows this structure
        BinaryPrimitives.WriteInt32LittleEndian(p[20..], read.Length);
        return buffer;
    }

    /// <summary>
    /// The data in a protocol reply, or why there is none. The descriptor
    /// says where its data is; that is checked against what came back rather
    /// than trusted.
    /// </summary>
    public static ProtocolReply ReadProtocolReply(ReadOnlySpan<byte> buffer, int returned)
    {
        if (returned < ReplyHeader + ProtocolData) return ProtocolReply.Fail($"reply too short ({returned} bytes)");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        var p = buffer[ReplyHeader..];
        var offset = BinaryPrimitives.ReadInt32LittleEndian(p[16..]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(p[20..]);
        var start = ReplyHeader + offset;
        if (offset < ProtocolData || length <= 0 || start + length > returned)
            return ProtocolReply.Fail($"descriptor points outside the reply (version {version}, size {size}, offset {offset}, length {length}, returned {returned})");
        return new ProtocolReply(true, buffer.Slice(start, length).ToArray(), null);
    }
}

public sealed record ProtocolReply(bool Ok, byte[] Data, string? Error)
{
    public static ProtocolReply Fail(string error) => new(false, [], error);
}

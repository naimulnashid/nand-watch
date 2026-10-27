using System.Buffers.Binary;
using System.Text;

namespace NandWatch.Core.Nvme;

/// <summary>STORAGE_DEVICE_DESCRIPTOR: what Windows itself knows of a disk.</summary>
/// <remarks>
/// For an NVMe drive, <see cref="Serial"/> is NOT the drive's serial number:
/// it is the namespace's EUI-64 or NGUID, written as groups of four hex digits
/// with underscores and a trailing dot (20 or 40 characters). Stable and
/// unique, so it is the drive's key here, but it never matches smartctl's
/// serial_number.
/// </remarks>
public sealed record DeviceDescriptor(int BusType, bool Removable, string Vendor, string Product, string Revision, string Serial)
{
    public string BusName => StorageBus.Name(BusType);

    public static DeviceDescriptor? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < 36) return null;
        return new DeviceDescriptor(
            BusType: BinaryPrimitives.ReadInt32LittleEndian(b[28..]),
            Removable: b[10] != 0,
            Vendor: Text(b, BinaryPrimitives.ReadInt32LittleEndian(b[12..])),
            Product: Text(b, BinaryPrimitives.ReadInt32LittleEndian(b[16..])),
            Revision: Text(b, BinaryPrimitives.ReadInt32LittleEndian(b[20..])),
            Serial: Text(b, BinaryPrimitives.ReadInt32LittleEndian(b[24..])));
    }

    /// <summary>A NUL-terminated ASCII string at an offset; 0 means absent.</summary>
    private static string Text(ReadOnlySpan<byte> b, int offset)
    {
        if (offset <= 0 || offset >= b.Length) return "";
        var s = b[offset..];
        var end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]).Trim();
    }
}

/// <summary>STORAGE_ADAPTER_DESCRIPTOR's bus type: the controller the disk hangs off.</summary>
public static class AdapterDescriptor
{
    public static int? BusType(ReadOnlySpan<byte> b) => b.Length > 24 ? b[24] : null;
}

/// <summary>STORAGE_BUS_TYPE names. A RAID bus in front of an NVMe drive usually means Intel RST / VMD.</summary>
public static class StorageBus
{
    public const int Nvme = 17;

    public static string Name(int type) => type switch
    {
        0 => "Unknown", 1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "Fibre",
        7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC",
        14 => "Virtual", 15 => "FileBackedVirtual", 16 => "Spaces", 17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => $"bus {type}",
    };
}

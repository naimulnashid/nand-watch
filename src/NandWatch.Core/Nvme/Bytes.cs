using System.Buffers.Binary;
using System.Text;

namespace NandWatch.Core.Nvme;

/// <summary>
/// Field reads for NVMe structures. Every multi-byte NVMe field is little
/// endian, whatever the host is.
/// </summary>
internal static class Bytes
{
    public static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);

    public static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);

    /// <summary>
    /// The health log's big counters are 16 bytes. A 64-bit read would be right
    /// for any drive alive today and silently wrong the day one is not.
    /// </summary>
    public static UInt128 U128(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt128LittleEndian(b[at..]);

    /// <summary>NVMe text fields are ASCII, padded with spaces (some drives pad with NULs).</summary>
    public static string Ascii(ReadOnlySpan<byte> b, int at, int length) =>
        Encoding.ASCII.GetString(b.Slice(at, length)).TrimEnd(' ', '\0').Trim();
}

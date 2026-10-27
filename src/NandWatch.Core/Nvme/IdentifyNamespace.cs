namespace NandWatch.Core.Nvme;

/// <summary>
/// Identify Namespace (CNS 00h). Read only to recognise it: under Intel RST /
/// VMD a device-scope request for Identify CONTROLLER comes back as this.
/// </summary>
public static class IdentifyNamespace
{
    /// <summary>
    /// Namespace size in bytes: NSZE (blocks, bytes 0-7) times the block size
    /// of the format in use (FLBAS, byte 26, indexes the LBA formats from byte
    /// 128; each format's byte 2 is log2 of its block size). Null if the
    /// numbers cannot be a namespace.
    /// </summary>
    public static ulong? SizeBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 192) return null;
        var blocks = BitConverter.ToUInt64(data[..8]);
        var format = data[26] & 0x0F;
        var shift = data[128 + 4 * format + 2];
        if (blocks == 0 || shift is < 9 or > 16) return null;
        return blocks << shift;
    }
}

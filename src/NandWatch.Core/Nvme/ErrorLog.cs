namespace NandWatch.Core.Nvme;

/// <summary>
/// One entry of the Error Information log (log identifier 01h, 64 bytes each).
/// An entry whose error count is 0 is unused.
/// </summary>
/// <remarks>
/// Most entries on a healthy drive are rejected ADMIN commands (submission
/// queue 0), not media trouble: a host asking for something the drive does not
/// support is logged too. Probing a drive can therefore add entries to its own
/// error log, which is why the collector must not retry a read the drive
/// refuses. Media errors live on I/O queues (1 and up) with status code type 2.
/// </remarks>
public sealed record ErrorLogEntry(ulong ErrorCount, ushort SubmissionQueue, ushort CommandId, ushort Status, ulong Lba, uint Namespace)
{
    public const int Size = 64;
    public const byte LogId = 0x01;

    /// <summary>Status Code Type: 0 generic, 1 command specific, 2 media and data integrity.</summary>
    public int StatusCodeType => (Status >> 9) & 0x7;

    public int StatusCode => (Status >> 1) & 0xFF;

    /// <summary>
    /// An entry that can mean trouble: on an I/O queue, or a media and data
    /// integrity status. Rejected admin commands are neither, and are not kept.
    /// </summary>
    public bool IsMediaOrIo => SubmissionQueue != 0 || StatusCodeType == 2;

    public static IReadOnlyList<ErrorLogEntry> Parse(ReadOnlySpan<byte> page)
    {
        var entries = new List<ErrorLogEntry>();
        for (var at = 0; at + Size <= page.Length; at += Size)
        {
            var e = page.Slice(at, Size);
            var count = BitConverter.ToUInt64(e[..8]);
            if (count == 0) continue;
            entries.Add(new ErrorLogEntry(count, Bytes.U16(e, 8), Bytes.U16(e, 10), Bytes.U16(e, 12),
                BitConverter.ToUInt64(e.Slice(16, 8)), Bytes.U32(e, 24)));
        }
        return entries;
    }

    /// <summary>The handful of codes worth naming here, from the NVMe base specification.</summary>
    public string Meaning => (StatusCodeType, StatusCode) switch
    {
        (0, 0x01) => "invalid command opcode",
        (0, 0x02) => "invalid field in command",
        (0, 0x0B) => "invalid namespace or format",
        (1, 0x09) => "invalid log page",
        (2, 0x80) => "write fault",
        (2, 0x81) => "unrecovered read error",
        (2, 0x82) => "end-to-end guard check error",
        (2, 0x86) => "access denied",
        _ => $"status type {StatusCodeType}, code {StatusCode:x2}h",
    };

    public string Describe() => $"#{ErrorCount}: {(SubmissionQueue == 0 ? "admin queue" : $"I/O queue {SubmissionQueue}")}, {Meaning}";
}

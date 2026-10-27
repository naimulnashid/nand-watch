using System.Buffers.Binary;
using System.Text;

namespace NandWatch.RstReader;

/// <summary>
/// The one request this helper can send: NVMe Get Log Page, log 02h (SMART /
/// Health Information), 512 bytes, through Intel RST's miniport pass-through.
/// Pure byte layout, no Windows calls, so the tests can pin every byte.
/// </summary>
/// <remarks>
/// <para>Ported from CrystalDiskInfo (<c>AtaSmart.h</c>, <c>INTEL_NVME_PASS_THROUGH</c>;
/// <c>AtaSmart.cpp</c>, <c>GetSmartAttributeNVMeIntelRst</c>), MIT License,
/// Copyright (c) 2008-2023 hiyohiyo; see THIRD-PARTY-NOTICES.md. Checked
/// against its source at commit 4fccdb4 (2026-10-19).</para>
/// <para>The layout, offsets from the start of the buffer:
/// <c>SRB_IO_CONTROL</c> 0x00-0x1B (header length, signature "IntelNvm",
/// timeout, control code 0xF0002808, return code, length); the payload
/// 0x1C-0xA3 (version 1, path id, target id, lun, the 64-byte NVMe command at
/// 0x20, completion entry 0x60-0x6F, queue id, parameter length 0xA4, return
/// length 0x1000, reserved); data 0xA4-0x10A3.</para>
/// <para><b>The namespace is 0xFFFFFFFF</b> (all namespaces): the
/// controller's own health log. A drive that keeps it per controller only
/// (this owner's Samsung) refuses it with a namespace id, which is why the
/// unelevated path fails for it.</para>
/// </remarks>
public static class RstRequest
{
    public const int SrbHeaderLength = 0x1C;
    public const int PayloadLength = 0x88;
    public const int DataOffset = SrbHeaderLength + PayloadLength;   // 0xA4
    public const int DataLength = 0x1000;
    public const int Length = DataOffset + DataLength;               // 0x10A4
    public const int PageLength = 512;

    public const uint IntelNvmePassThrough = 0xF0002808;   // CTL_CODE(0xF000, 0xA02, METHOD_BUFFERED, FILE_ANY_ACCESS)

    /// <summary>NVMe admin opcode 02h, Get Log Page. The only opcode in this program.</summary>
    public const byte GetLogPage = 0x02;

    /// <summary>CDW10: log identifier 02h (SMART / Health), NUMD 0x7F (128 dwords = 512 bytes).</summary>
    public const uint HealthLogCdw10 = 0x007F0002;

    public const uint AllNamespaces = 0xFFFFFFFF;

    /// <summary>The whole request for the drive at <paramref name="pathId"/> on its RST port.</summary>
    public static byte[] HealthLog(byte pathId)
    {
        var b = new byte[Length];
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x00..], SrbHeaderLength);
        Encoding.ASCII.GetBytes("IntelNvm").CopyTo(s[0x04..]);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x0C..], 10);                       // timeout, seconds
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x10..], IntelNvmePassThrough);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x18..], Length - SrbHeaderLength);

        b[0x1C] = 1;                                                                   // payload version
        b[0x1D] = pathId;
        b[0x20] = GetLogPage;                                                          // CDW0: opcode
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x24..], AllNamespaces);            // NSID
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x48..], HealthLogCdw10);           // CDW10
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x74..], DataOffset);               // parameter buffer length
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x78..], DataLength);               // return buffer length
        return b;
    }

    /// <summary>The 512-byte page in a reply.</summary>
    public static byte[] Page(byte[] reply) => reply.AsSpan(DataOffset, PageLength).ToArray();

    /// <summary>SRB_IO_CONTROL's return code: what the miniport said.</summary>
    public static uint ReturnCode(byte[] reply) => BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(0x14));

    /// <summary>The NVMe completion entry's four dwords, as the miniport copied them back.</summary>
    public static uint[] Completion(byte[] reply) =>
        Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(0x60 + i * 4))).ToArray();
}

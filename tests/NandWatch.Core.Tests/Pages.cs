using System.Buffers.Binary;
using System.Text;

namespace NandWatch.Core.Tests;

/// <summary>
/// Synthetic NVMe pages. Invented values only: a real page carries a serial
/// number and a record of the machine's use, and never enters the repo.
/// </summary>
internal static class Pages
{
    public static byte[] Health(Action<Span<byte>>? edit = null)
    {
        var p = new byte[512];
        p[0] = 0b0000_0101;                                   // spare + reliability
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), 311); // 38 C
        p[3] = 100; p[4] = 10; p[5] = 7; p[6] = 0;
        U128(p, 32, 11);
        U128(p, 48, 1_953_125);                               // exactly 1 TB written
        U128(p, 64, 13); U128(p, 80, 14); U128(p, 96, 15); U128(p, 112, 16);
        U128(p, 128, 17); U128(p, 144, 18); U128(p, 160, 19); U128(p, 176, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(192), 21);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(196), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(200), 313);   // sensor 1: 40 C
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(202), 318);   // sensor 2: 45 C
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(216), 23);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(220), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(224), 25);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(228), 26);
        edit?.Invoke(p);
        return p;
    }

    public static void U128(Span<byte> p, int at, UInt128 value) => BinaryPrimitives.WriteUInt128LittleEndian(p[at..], value);

    public static byte[] Identify()
    {
        var p = new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(p, 0x1234);
        Ascii(p, 4, 20, "EXAMPLE0SERIAL");
        Ascii(p, 24, 40, "Example NVMe 512GB");
        Ascii(p, 64, 8, "FW1.0");
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(80), 0x0001_0400);
        p[261] = 0x03; p[263] = 4; p[265] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(266), 358);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(268), 361);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(272), 16384);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(276), 8192);
        U128(p, 280, 512_110_190_592);
        return p;
    }

    /// <summary>Space-padded, as the spec says text fields are.</summary>
    private static void Ascii(byte[] p, int at, int length, string text) =>
        Encoding.ASCII.GetBytes(text.PadRight(length)).CopyTo(p, at);
}

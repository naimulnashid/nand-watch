using System.Buffers.Binary;
using NandWatch.Core.Nvme;

namespace NandWatch.Core.Tests;

/// <summary>
/// What Intel RST / VMD answers, as the probe found on 2026-10-24: a
/// device-scope Identify Controller request returns Identify Namespace.
/// </summary>
public class RstTests
{
    /// <summary>Identify Namespace for a 512 GB drive in 512-byte blocks: 1,000,215,216 of them.</summary>
    private static byte[] Namespace512()
    {
        var p = new byte[4096];
        BinaryPrimitives.WriteUInt64LittleEndian(p, 1_000_215_216);   // NSZE: starts B0 12 9E 3B, as the probe saw
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), 1_000_215_216);
        p[26] = 0;                                                    // FLBAS: format 0
        p[128 + 2] = 9;                                               // format 0: 2^9 = 512-byte blocks
        return p;
    }

    [Fact]
    public void Namespace_data_is_not_mistaken_for_the_controller()
    {
        var bytes = Namespace512();
        Assert.False(IdentifyController.Parse(bytes).IsPlausible);
        Assert.Equal(512_110_190_592ul, IdentifyNamespace.SizeBytes(bytes));
    }

    [Fact]
    public void Real_controller_data_is_plausible_and_not_a_namespace_size()
    {
        Assert.True(IdentifyController.Parse(Pages.Identify()).IsPlausible);
    }
}

public class ErrorLogTests
{
    private static byte[] Log(params (ulong Count, ushort Queue, ushort Status)[] entries)
    {
        var p = new byte[512];
        for (var i = 0; i < entries.Length; i++)
        {
            var at = i * 64;
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(at), entries[i].Count);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at + 8), entries[i].Queue);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at + 12), entries[i].Status);
        }
        return p;
    }

    [Fact]
    public void Unused_entries_are_skipped_and_status_is_decoded()
    {
        // Status field: bit 0 is the phase tag, bits 1-8 the code, 9-11 the type.
        var entries = ErrorLogEntry.Parse(Log((7, 0, 0x02 << 1 | 1), (6, 3, 2 << 9 | 0x81 << 1)));
        Assert.Equal(2, entries.Count);
        Assert.Equal("#7: admin queue, invalid field in command", entries[0].Describe());
        Assert.Equal("#6: I/O queue 3, unrecovered read error", entries[1].Describe());
    }

    [Fact]
    public void An_all_zero_page_has_no_entries()
    {
        Assert.Empty(ErrorLogEntry.Parse(new byte[512]));
    }
}

public class TemperatureDescriptorTests
{
    /// <summary>The bytes the Samsung returned on 2026-10-24: 47 C, warning 85 C (HWiNFO showed the same).</summary>
    private const string Samsung = "28000000280000000080008001000000000000000000000000002F00550000800100000000000000";

    /// <summary>The Micron's: 38 C, warning 87 C.</summary>
    private const string Micron = "28000000280000000080008001000000000000000000000000002600570000800100000000000000";

    [Fact]
    public void Behind_RST_the_warning_comes_from_the_sensor_and_critical_is_not_reported()
    {
        var t = TemperatureDescriptor.Parse(Convert.FromHexString(Samsung))!;
        Assert.Null(t.CriticalC);
        Assert.Null(t.WarningC);
        Assert.Equal((short)47, t.TemperatureC);
        Assert.Equal((short)85, t.EffectiveWarningC);
        Assert.Equal("47 C, warning 85 C, critical not reported", t.Describe());
    }

    [Fact]
    public void The_Micron_reads_38_C_with_an_87_C_warning()
    {
        var t = TemperatureDescriptor.Parse(Convert.FromHexString(Micron))!;
        Assert.Equal(((short)38, (short)87), (t.TemperatureC!.Value, t.EffectiveWarningC!.Value));
        Assert.Equal(0, t.Sensors[0].Index);
    }

    [Fact]
    public void A_header_threshold_wins_when_the_drive_reports_one()
    {
        var b = Convert.FromHexString(Micron);
        b[10] = 80; b[11] = 0;                                         // header warning: 80 C
        Assert.Equal((short)80, TemperatureDescriptor.Parse(b)!.EffectiveWarningC);
    }
}

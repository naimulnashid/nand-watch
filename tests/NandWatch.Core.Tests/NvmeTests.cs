using NandWatch.Core.Nvme;

namespace NandWatch.Core.Tests;

public class HealthLogTests
{
    [Fact]
    public void Every_field_is_read_from_its_spec_offset()
    {
        var h = HealthLog.Parse(Pages.Health());
        Assert.Equal(CriticalWarning.SpareBelowThreshold | CriticalWarning.ReliabilityDegraded, h.CriticalWarning);
        Assert.Equal(311, h.CompositeTemperatureK);
        Assert.Equal((100, 10, 7), (h.AvailableSpare, h.AvailableSpareThreshold, h.PercentageUsed));
        Assert.Equal((UInt128)11, h.DataUnitsRead);
        Assert.Equal((UInt128)1_953_125, h.DataUnitsWritten);
        Assert.Equal([13, 14, 15, 16, 17, 18, 19, 20],
            new[] { h.HostReadCommands, h.HostWriteCommands, h.ControllerBusyMinutes, h.PowerCycles, h.PowerOnHours, h.UnsafeShutdowns, h.MediaErrors, h.ErrorLogEntries }.Select(v => (int)v));
        Assert.Equal((21u, 22u), (h.WarningTemperatureMinutes, h.CriticalTemperatureMinutes));
        Assert.Equal([313, 318, 0, 0, 0, 0, 0, 0], h.TemperatureSensorsK.Select(v => (int)v));
        Assert.Equal((23u, 24u, 25u, 26u), (h.ThermalLimit1Transitions, h.ThermalLimit2Transitions, h.ThermalLimit1Seconds, h.ThermalLimit2Seconds));
    }

    [Fact]
    public void Counters_are_128_bit()
    {
        var big = (UInt128)ulong.MaxValue + 5;
        var h = HealthLog.Parse(Pages.Health(p => Pages.U128(p, 48, big)));
        Assert.Equal(big, h.DataUnitsWritten);
    }

    [Fact]
    public void A_data_unit_is_512000_bytes_and_TB_are_decimal()
    {
        Assert.Equal(1m, HealthLog.Terabytes(1_953_125));
        Assert.Equal(150m, HealthLog.Terabytes(292_968_750));
    }

    [Fact]
    public void Zero_kelvin_is_not_reported_rather_than_minus_273()
    {
        Assert.Null(HealthLog.Celsius(0));
        Assert.Equal(38, HealthLog.Celsius(311));
    }

    [Fact]
    public void A_short_page_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HealthLog.Parse(new byte[511]));
    }
}

public class IdentifyControllerTests
{
    [Fact]
    public void Text_fields_lose_their_padding_and_numbers_their_offsets_are_right()
    {
        var id = IdentifyController.Parse(Pages.Identify());
        Assert.Equal("EXAMPLE0SERIAL", id.SerialNumber);
        Assert.Equal("Example NVMe 512GB", id.Model);
        Assert.Equal("FW1.0", id.Firmware);
        Assert.Equal("1.4.0", id.Version);
        Assert.Equal(5, id.PowerStateCount);
        Assert.True(id.AutonomousPowerStates);
        Assert.Equal((358, 361), (id.WarningTemperatureK, id.CriticalTemperatureK));
        Assert.Equal((16384u, 8192u), (id.HostMemoryBufferPreferred, id.HostMemoryBufferMinimum));
        Assert.Equal((UInt128)512_110_190_592, id.TotalCapacityBytes);
    }
}

public class StorageQueryTests
{
    [Fact]
    public void A_health_request_is_laid_out_as_winioctl_says()
    {
        var b = StorageQuery.Protocol(StorageProperty.DeviceProtocolSpecific, NvmeRead.HealthLog);
        int I(int at) => BitConverter.ToInt32(b, at);
        Assert.Equal(8 + 40 + 512, b.Length);
        Assert.Equal(50, I(0));     // StorageDeviceProtocolSpecificProperty
        Assert.Equal(0, I(4));      // PropertyStandardQuery
        Assert.Equal(3, I(8));      // ProtocolTypeNvme
        Assert.Equal(2, I(12));     // NVMeDataTypeLogPage
        Assert.Equal(2, I(16));     // log identifier 02h
        Assert.Equal(40, I(24));    // data offset: after the protocol structure
        Assert.Equal(512, I(28));   // data length
    }

    [Fact]
    public void A_reply_is_read_where_its_descriptor_points()
    {
        var b = StorageQuery.Protocol(StorageProperty.AdapterProtocolSpecific, NvmeRead.HealthLog);
        b[48] = 0xAB;
        var reply = StorageQuery.ReadProtocolReply(b, b.Length);
        Assert.True(reply.Ok);
        Assert.Equal(512, reply.Data.Length);
        Assert.Equal(0xAB, reply.Data[0]);
    }

    [Fact]
    public void A_reply_pointing_past_what_came_back_is_an_error_not_garbage()
    {
        var b = StorageQuery.Protocol(StorageProperty.DeviceProtocolSpecific, NvmeRead.HealthLog);
        var reply = StorageQuery.ReadProtocolReply(b, 100);
        Assert.False(reply.Ok);
        Assert.Contains("outside", reply.Error);
    }

    [Fact]
    public void Only_vendor_log_identifiers_are_vendor_logs()
    {
        Assert.Equal((uint)0xC0, NvmeRead.VendorLog(0xC0).RequestValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => NvmeRead.VendorLog(0x02));
    }

    [Fact]
    public void Every_NVMe_read_is_Identify_or_Get_Log_Page()
    {
        var all = new[] { NvmeRead.IdentifyController, NvmeRead.HealthLog }.Concat(NvmeRead.StandardLogs)
            .Concat(Enumerable.Range(0xC0, 0x40).Select(i => NvmeRead.VendorLog((byte)i)));
        Assert.All(all, r => Assert.Contains(r.DataType, new[] { 1, 2 }));
    }
}

public class SmartctlTests
{
    private const string Json = """
        {
          "model_name": "Example NVMe 512GB",
          "serial_number": "EXAMPLE0SERIAL",
          "firmware_version": "FW1.0",
          "nvme_smart_health_information_log": {
            "critical_warning": 5, "temperature": 38, "available_spare": 100,
            "available_spare_threshold": 10, "percentage_used": 7,
            "data_units_read": 11, "data_units_written": 1953125,
            "host_reads": 13, "host_writes": 14, "controller_busy_time": 15,
            "power_cycles": 16, "power_on_hours": 17, "unsafe_shutdowns": 18,
            "media_errors": 19, "num_err_log_entries": 20,
            "warning_temp_time": 21, "critical_comp_time": 22,
            "temperature_sensors": [40, 45]
          }
        }
        """;

    private static IReadOnlyList<ComparedField> Run(string json, Action<Span<byte>>? after = null) =>
        HealthCompare.Compare(HealthLog.Parse(Pages.Health()), HealthLog.Parse(Pages.Health(after)),
            IdentifyController.Parse(Pages.Identify()), SmartctlReading.Parse(json));

    [Fact]
    public void Identical_readings_match_on_every_field()
    {
        var rows = Run(Json);
        Assert.All(rows, r => Assert.Equal(Agreement.Match, r.Agreement));
        Assert.Equal(21, rows.Count);
    }

    [Fact]
    public void A_counter_that_moved_during_the_read_matches_inside_the_bracket()
    {
        var rows = Run(Json.Replace("\"host_writes\": 14", "\"host_writes\": 15"), p => Pages.U128(p, 80, 16));
        Assert.Equal(Agreement.Match, rows.Single(r => r.Field == "host_writes").Agreement);
    }

    [Fact]
    public void A_value_outside_the_bracket_differs()
    {
        var rows = Run(Json.Replace("\"data_units_written\": 1953125", "\"data_units_written\": 1953126"));
        Assert.Equal(Agreement.Differ, rows.Single(r => r.Field == "data_units_written").Agreement);
    }

    [Fact]
    public void A_key_smartctl_does_not_print_is_absent_not_different()
    {
        var rows = Run(Json.Replace("\"media_errors\": 19,", ""));
        Assert.Equal(Agreement.Absent, rows.Single(r => r.Field == "media_errors").Agreement);
    }
}

public class ShortReplyTests
{
    [Fact]
    public void A_short_reply_is_null_rather_than_an_exception()
    {
        Assert.Null(HealthLog.TryParse(new byte[100]));
        Assert.Null(IdentifyController.TryParse(new byte[100]));
        Assert.NotNull(HealthLog.TryParse(Pages.Health()));
    }
}

public class HealthPageTests
{
    [Fact]
    public void ToPage_is_the_exact_inverse_of_Parse()
    {
        var page = Pages.Health(p => Pages.U128(p, 128, (UInt128)ulong.MaxValue + 9));
        Assert.Equal(page, HealthLog.Parse(page).ToPage());
    }
}

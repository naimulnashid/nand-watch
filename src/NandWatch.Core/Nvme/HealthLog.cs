using System.Buffers.Binary;

namespace NandWatch.Core.Nvme;

/// <summary>The critical warning byte, bit by bit (health log byte 0).</summary>
[Flags]
public enum CriticalWarning : byte
{
    None = 0,
    SpareBelowThreshold = 1 << 0,
    Temperature = 1 << 1,
    ReliabilityDegraded = 1 << 2,
    ReadOnly = 1 << 3,
    VolatileBackupFailed = 1 << 4,
    PersistentMemoryReadOnly = 1 << 5,
}

/// <summary>
/// The SMART / Health Information log page (log identifier 02h), 512 bytes,
/// as the NVMe base specification lays it out. Values are kept exactly as the
/// drive reports them; conversions are separate members, so a stored page can
/// always be read again.
/// </summary>
/// <remarks>
/// <para><b>A data unit is 1,000 sectors of 512 bytes = 512,000 bytes</b>, and
/// the drive rounds UP. Endurance ratings (TBW) are decimal terabytes, so
/// <see cref="Terabytes"/> divides by 10^12, never 2^40.</para>
/// <para><b>Temperatures are kelvin</b>, and 0 means "not reported".
/// <see cref="Celsius"/> subtracts 273, as smartctl prints them.</para>
/// <para><b>Percentage used is the vendor's estimate</b>: a whole number that
/// may pass 100 and stops at 255. It is not derived from bytes written.</para>
/// </remarks>
public sealed record HealthLog
{
    public const int Size = 512;
    public const long BytesPerDataUnit = 512_000;
    public const byte LogId = 0x02;

    public required CriticalWarning CriticalWarning { get; init; }
    public required ushort CompositeTemperatureK { get; init; }
    public required byte AvailableSpare { get; init; }
    public required byte AvailableSpareThreshold { get; init; }
    public required byte PercentageUsed { get; init; }
    public required byte EnduranceGroupCriticalWarning { get; init; }
    public required UInt128 DataUnitsRead { get; init; }
    public required UInt128 DataUnitsWritten { get; init; }
    public required UInt128 HostReadCommands { get; init; }
    public required UInt128 HostWriteCommands { get; init; }
    public required UInt128 ControllerBusyMinutes { get; init; }
    public required UInt128 PowerCycles { get; init; }
    public required UInt128 PowerOnHours { get; init; }
    public required UInt128 UnsafeShutdowns { get; init; }
    public required UInt128 MediaErrors { get; init; }
    public required UInt128 ErrorLogEntries { get; init; }
    public required uint WarningTemperatureMinutes { get; init; }
    public required uint CriticalTemperatureMinutes { get; init; }

    /// <summary>Sensors 1 to 8 in kelvin; 0 is a sensor the drive does not have.</summary>
    public required IReadOnlyList<ushort> TemperatureSensorsK { get; init; }

    public required uint ThermalLimit1Transitions { get; init; }
    public required uint ThermalLimit2Transitions { get; init; }
    public required uint ThermalLimit1Seconds { get; init; }
    public required uint ThermalLimit2Seconds { get; init; }

    public static HealthLog Parse(ReadOnlySpan<byte> page)
    {
        if (page.Length < Size) throw new ArgumentException($"A health log page is {Size} bytes; got {page.Length}.", nameof(page));
        var sensors = new ushort[8];
        for (var i = 0; i < sensors.Length; i++) sensors[i] = Bytes.U16(page, 200 + 2 * i);
        return new HealthLog
        {
            CriticalWarning = (CriticalWarning)page[0],
            CompositeTemperatureK = Bytes.U16(page, 1),
            AvailableSpare = page[3],
            AvailableSpareThreshold = page[4],
            PercentageUsed = page[5],
            EnduranceGroupCriticalWarning = page[6],
            DataUnitsRead = Bytes.U128(page, 32),
            DataUnitsWritten = Bytes.U128(page, 48),
            HostReadCommands = Bytes.U128(page, 64),
            HostWriteCommands = Bytes.U128(page, 80),
            ControllerBusyMinutes = Bytes.U128(page, 96),
            PowerCycles = Bytes.U128(page, 112),
            PowerOnHours = Bytes.U128(page, 128),
            UnsafeShutdowns = Bytes.U128(page, 144),
            MediaErrors = Bytes.U128(page, 160),
            ErrorLogEntries = Bytes.U128(page, 176),
            WarningTemperatureMinutes = Bytes.U32(page, 192),
            CriticalTemperatureMinutes = Bytes.U32(page, 196),
            TemperatureSensorsK = sensors,
            ThermalLimit1Transitions = Bytes.U32(page, 216),
            ThermalLimit2Transitions = Bytes.U32(page, 220),
            ThermalLimit1Seconds = Bytes.U32(page, 224),
            ThermalLimit2Seconds = Bytes.U32(page, 228),
        };
    }

    /// <summary>
    /// The page this record was parsed from, byte for byte where the spec
    /// defines a field (reserved bytes are zero). The demo history writes real
    /// pages with it, so everything downstream reads them as it reads a drive's.
    /// </summary>
    public byte[] ToPage()
    {
        var p = new byte[Size];
        var b = p.AsSpan();
        b[0] = (byte)CriticalWarning;
        BinaryPrimitives.WriteUInt16LittleEndian(b[1..], CompositeTemperatureK);
        b[3] = AvailableSpare;
        b[4] = AvailableSpareThreshold;
        b[5] = PercentageUsed;
        b[6] = EnduranceGroupCriticalWarning;
        UInt128[] big = [DataUnitsRead, DataUnitsWritten, HostReadCommands, HostWriteCommands, ControllerBusyMinutes, PowerCycles, PowerOnHours, UnsafeShutdowns, MediaErrors, ErrorLogEntries];
        for (var i = 0; i < big.Length; i++) BinaryPrimitives.WriteUInt128LittleEndian(b[(32 + 16 * i)..], big[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(b[192..], WarningTemperatureMinutes);
        BinaryPrimitives.WriteUInt32LittleEndian(b[196..], CriticalTemperatureMinutes);
        for (var i = 0; i < 8 && i < TemperatureSensorsK.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(b[(200 + 2 * i)..], TemperatureSensorsK[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(b[216..], ThermalLimit1Transitions);
        BinaryPrimitives.WriteUInt32LittleEndian(b[220..], ThermalLimit2Transitions);
        BinaryPrimitives.WriteUInt32LittleEndian(b[224..], ThermalLimit1Seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(b[228..], ThermalLimit2Seconds);
        return p;
    }

    /// <summary>Null for a reply too short to be a health log, so a probe of an odd drive reports it rather than stopping.</summary>
    public static HealthLog? TryParse(ReadOnlySpan<byte> page) => page.Length >= Size ? Parse(page) : null;

    public static int? Celsius(ushort kelvin) => kelvin == 0 ? null : kelvin - 273;

    public static decimal Terabytes(UInt128 dataUnits) => (decimal)dataUnits * BytesPerDataUnit / 1_000_000_000_000m;

    /// <summary>
    /// Every field as (spec name, value as text): the probe's report and the
    /// smartctl comparison both read this, so they cannot drift apart.
    /// </summary>
    public IEnumerable<(string Name, string Value)> Fields()
    {
        yield return ("Critical warning", $"0x{(byte)CriticalWarning:x2} ({CriticalWarning})");
        yield return ("Composite temperature", $"{CompositeTemperatureK} K ({Celsius(CompositeTemperatureK)?.ToString() ?? "-"} C)");
        yield return ("Available spare", $"{AvailableSpare}%");
        yield return ("Available spare threshold", $"{AvailableSpareThreshold}%");
        yield return ("Percentage used", $"{PercentageUsed}%");
        yield return ("Endurance group critical warning", $"0x{EnduranceGroupCriticalWarning:x2}");
        yield return ("Data units read", $"{DataUnitsRead} ({Terabytes(DataUnitsRead):0.000} TB)");
        yield return ("Data units written", $"{DataUnitsWritten} ({Terabytes(DataUnitsWritten):0.000} TB)");
        yield return ("Host read commands", HostReadCommands.ToString());
        yield return ("Host write commands", HostWriteCommands.ToString());
        yield return ("Controller busy time", $"{ControllerBusyMinutes} min");
        yield return ("Power cycles", PowerCycles.ToString());
        yield return ("Power on hours", PowerOnHours.ToString());
        yield return ("Unsafe shutdowns", UnsafeShutdowns.ToString());
        yield return ("Media and data integrity errors", MediaErrors.ToString());
        yield return ("Error information log entries", ErrorLogEntries.ToString());
        yield return ("Warning composite temperature time", $"{WarningTemperatureMinutes} min");
        yield return ("Critical composite temperature time", $"{CriticalTemperatureMinutes} min");
        for (var i = 0; i < TemperatureSensorsK.Count; i++)
            if (TemperatureSensorsK[i] != 0)
                yield return ($"Temperature sensor {i + 1}", $"{TemperatureSensorsK[i]} K ({Celsius(TemperatureSensorsK[i])} C)");
        yield return ("Thermal limit 1 transitions", ThermalLimit1Transitions.ToString());
        yield return ("Thermal limit 2 transitions", ThermalLimit2Transitions.ToString());
        yield return ("Thermal limit 1 time", $"{ThermalLimit1Seconds} s");
        yield return ("Thermal limit 2 time", $"{ThermalLimit2Seconds} s");
    }
}

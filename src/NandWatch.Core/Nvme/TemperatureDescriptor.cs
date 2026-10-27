using System.Buffers.Binary;

namespace NandWatch.Core.Nvme;

/// <summary>One sensor of the descriptor: its reading and the threshold above which the drive warns.</summary>
public sealed record TemperatureSensor(int Index, short? TemperatureC, short? OverThresholdC);

/// <summary>
/// STORAGE_TEMPERATURE_DATA_DESCRIPTOR (property 52), which Windows builds
/// from the drive itself, in whole degrees Celsius. Behind Intel RST it is the
/// only temperature this app can read for a drive that refuses its health log.
/// </summary>
/// <remarks>
/// <para>Layout checked 2026-10-24 against both drives' raw bytes and HWiNFO64:
/// a 24-byte header (version, size, critical, warning, sensor count, padding),
/// then 16 bytes per sensor (index, temperature, over threshold, under
/// threshold, three flags, padding).</para>
/// <para><b>-32768 (0x8000) means "not reported".</b> Behind RST the header's
/// critical and warning are always that; the sensor's over threshold carries
/// the drive's warning temperature instead (87 C Micron, 85 C Samsung, as
/// HWiNFO shows them). The critical temperature is not available here.</para>
/// </remarks>
public sealed record TemperatureDescriptor(short? CriticalC, short? WarningC, IReadOnlyList<TemperatureSensor> Sensors)
{
    private const int Header = 24, Info = 16;
    private const short NotReported = short.MinValue;

    /// <summary>The drive's warning temperature: the header's when given, else the first sensor's over threshold.</summary>
    public short? EffectiveWarningC => WarningC ?? Sensors.Select(s => s.OverThresholdC).FirstOrDefault(t => t is not null);

    public short? TemperatureC => Sensors.Select(s => s.TemperatureC).FirstOrDefault(t => t is not null);

    public static TemperatureDescriptor? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < Header) return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(b[12..]);
        var sensors = new List<TemperatureSensor>();
        for (var i = 0; i < count && Header + Info * (i + 1) <= b.Length; i++)
        {
            var s = b[(Header + Info * i)..];
            sensors.Add(new TemperatureSensor(BinaryPrimitives.ReadUInt16LittleEndian(s), Value(s[2..]), Value(s[4..])));
        }
        return new TemperatureDescriptor(Value(b[8..]), Value(b[10..]), sensors);
    }

    private static short? Value(ReadOnlySpan<byte> b)
    {
        var v = BinaryPrimitives.ReadInt16LittleEndian(b);
        return v == NotReported ? null : v;
    }

    public string Describe() =>
        $"{TemperatureC?.ToString() ?? "-"} C, warning {EffectiveWarningC?.ToString() ?? "-"} C, critical {CriticalC?.ToString() ?? "not reported"}";
}

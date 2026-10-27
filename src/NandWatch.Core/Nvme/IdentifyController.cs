namespace NandWatch.Core.Nvme;

/// <summary>
/// The fields of Identify Controller (CNS 01h, 4,096 bytes) this app reads.
/// Offsets are the NVMe base specification's.
/// </summary>
public sealed record IdentifyController
{
    public const int Size = 4096;
    public const byte Cns = 0x01;

    public required ushort VendorId { get; init; }
    public required string SerialNumber { get; init; }
    public required string Model { get; init; }
    public required string Firmware { get; init; }

    /// <summary>The NVMe version the controller implements, e.g. "1.4.0"; "" when it reports none.</summary>
    public required string Version { get; init; }

    /// <summary>Bit 0: the health log is available per namespace as well as per controller.</summary>
    public required byte LogPageAttributes { get; init; }

    public required byte PowerStateCount { get; init; }
    public required bool AutonomousPowerStates { get; init; }

    /// <summary>The drive's own warning and critical composite temperatures, kelvin (0 = not reported).</summary>
    public required ushort WarningTemperatureK { get; init; }
    public required ushort CriticalTemperatureK { get; init; }

    /// <summary>
    /// Host Memory Buffer preferred and minimum sizes, in 4 KiB units. Non-zero
    /// on DRAM-less drives, which borrow host RAM for their mapping table.
    /// </summary>
    public required uint HostMemoryBufferPreferred { get; init; }
    public required uint HostMemoryBufferMinimum { get; init; }

    /// <summary>Total NVM capacity in bytes; 0 when the controller does not report it.</summary>
    public required UInt128 TotalCapacityBytes { get; init; }

    /// <summary>
    /// False when the reply is not Identify Controller at all: under Intel RST
    /// a device-scope request returns Identify Namespace, whose bytes read as
    /// a model of binary noise. Model and firmware must be printable ASCII.
    /// </summary>
    public bool IsPlausible => Printable(Model) && Printable(Firmware);

    private static bool Printable(string s) => s.Length > 0 && s.All(c => c is >= ' ' and <= '~');

    /// <summary>The last field read here ends at byte 311.</summary>
    private const int FieldsEnd = 312;

    /// <summary>Null for a reply too short to hold the fields read here.</summary>
    public static IdentifyController? TryParse(ReadOnlySpan<byte> data) => data.Length >= FieldsEnd ? Parse(data) : null;

    public static IdentifyController Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < FieldsEnd) throw new ArgumentException($"Identify Controller is {Size} bytes; got {data.Length}.", nameof(data));
        var ver = Bytes.U32(data, 80);
        return new IdentifyController
        {
            VendorId = Bytes.U16(data, 0),
            SerialNumber = Bytes.Ascii(data, 4, 20),
            Model = Bytes.Ascii(data, 24, 40),
            Firmware = Bytes.Ascii(data, 64, 8),
            Version = ver == 0 ? "" : $"{ver >> 16}.{(ver >> 8) & 0xff}.{ver & 0xff}",
            LogPageAttributes = data[261],
            PowerStateCount = (byte)(data[263] + 1),
            AutonomousPowerStates = (data[265] & 1) != 0,
            WarningTemperatureK = Bytes.U16(data, 266),
            CriticalTemperatureK = Bytes.U16(data, 268),
            HostMemoryBufferPreferred = Bytes.U32(data, 272),
            HostMemoryBufferMinimum = Bytes.U32(data, 276),
            TotalCapacityBytes = Bytes.U128(data, 280),
        };
    }
}

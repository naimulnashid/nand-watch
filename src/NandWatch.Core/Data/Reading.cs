using NandWatch.Core.Nvme;

namespace NandWatch.Core.Data;

/// <summary>
/// A drive as this app knows it: model plus Windows' device id, which for an
/// NVMe drive is the namespace's EUI-64 or NGUID (see
/// <see cref="DeviceDescriptor"/>). A new id is a new drive, never a reset.
/// </summary>
public sealed record DriveIdentity(string Model, string DeviceId, string Firmware);

/// <summary>Everything one collector run learned about one drive.</summary>
public sealed record DriveReading
{
    public required DriveIdentity Drive { get; init; }
    public required int DiskNumber { get; init; }
    public required DateTimeOffset At { get; init; }

    /// <summary>The health log, when the drive answered; with its raw page, kept so a parser fix can recompute history.</summary>
    public HealthLog? Health { get; init; }
    public byte[]? HealthPage { get; init; }

    /// <summary>From Windows' temperature descriptor: the only temperature for a drive that refuses its health log.</summary>
    public short? SensorTemperatureC { get; init; }
    public short? WarningTemperatureC { get; init; }

    /// <summary>The error log's entries, when it was read; null when it was not.</summary>
    public IReadOnlyList<ErrorLogEntry>? ErrorEntries { get; init; }

    /// <summary>Null for this collector's own read; <see cref="RstSource"/> for a page from the elevated RST reader.</summary>
    public string? Source { get; init; }

    public const string RstSource = "rst";
}

/// <summary>One event from Windows' System log, as kept: no message text.</summary>
public sealed record SystemEvent(DateTimeOffset At, string Provider, int Id, int Level, int? Disk, long RecordId);

/// <summary>One disk volume's size and free space at a run.</summary>
public sealed record VolumeReading(string Letter, int DiskNumber, long TotalBytes, long FreeBytes);

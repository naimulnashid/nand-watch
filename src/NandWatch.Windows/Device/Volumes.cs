namespace NandWatch.Windows.Device;

/// <summary>One mounted fixed volume and the physical disks it spans.</summary>
public sealed record VolumeInfo(string Letter, string Label, string Format, long TotalBytes, long FreeBytes, string? Target, IReadOnlyList<int> Disks, string? Error)
{
    /// <summary>
    /// A partition of a disk. Virtual drives (Google Drive for desktop's G:,
    /// for one) report themselves as fixed and can even claim a disk's
    /// extents, but their letter does not point at a HarddiskVolume.
    /// </summary>
    public bool IsDiskVolume => Target?.StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase) == true;
}

public static class Volumes
{
    /// <summary>
    /// Every fixed volume with a drive letter, mapped to its disks through
    /// IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS. A quickly filling QLC drive loses
    /// its fast-write cache, so free space belongs beside the health figures.
    /// </summary>
    public static IReadOnlyList<VolumeInfo> List()
    {
        var list = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            var letter = drive.Name.TrimEnd('\\');
            try
            {
                if (!drive.IsReady) continue;
                var target = Native.DosTarget(letter);
                var (disks, error) = DisksOf(letter);
                var volume = new VolumeInfo(letter, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.TotalFreeSpace, target, disks, error);
                list.Add(volume.IsDiskVolume ? volume : volume with { Disks = [] });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                list.Add(new VolumeInfo(letter, "", "", 0, 0, null, [], ex.Message));
            }
        }
        return list;
    }

    private static (IReadOnlyList<int> Disks, string? Error) DisksOf(string letter)
    {
        using var volume = Native.Open($@"\\.\{letter}", Native.NoAccess, out var openError);
        if (volume.IsInvalid) return ([], $"open failed ({openError})");
        // VOLUME_DISK_EXTENTS: a count (padded to 8), then 24-byte extents
        // whose first field is the disk number.
        var output = new byte[8 + 24 * 32];
        if (!Native.DiskExtents(volume, output, out var returned, out var error)) return ([], $"extents failed ({error})");
        var count = BitConverter.ToInt32(output, 0);
        var disks = new List<int>();
        for (var i = 0; i < count && 8 + 24 * (i + 1) <= returned; i++) disks.Add(BitConverter.ToInt32(output, 8 + 24 * i));
        return (disks.Distinct().ToList(), null);
    }
}

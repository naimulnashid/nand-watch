using System.Text.RegularExpressions;

namespace NandWatch.Core.Tests;

/// <summary>
/// The app reads drives and must never be able to do anything else. These
/// tests read the source: every IOCTL code it defines must be on the list of
/// queries, and every DeviceIoControl call must use one of them.
/// </summary>
public partial class ReadOnlyGuardTests
{
    /// <summary>
    /// The only codes allowed. All three are FILE_ANY_ACCESS queries:
    /// IOCTL_STORAGE_QUERY_PROPERTY (descriptors, Identify, Get Log Page),
    /// IOCTL_STORAGE_GET_DEVICE_NUMBER and IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS.
    /// Adding one means reading what it can do first: pass-through codes
    /// (IOCTL_STORAGE_PROTOCOL_COMMAND, SCSI and ATA pass-through) are never
    /// added, because they can send any command to the drive.
    /// </summary>
    private static readonly Dictionary<string, uint> Allowed = new()
    {
        ["IoctlStorageQueryProperty"] = 0x002D1400,
        ["IoctlStorageGetDeviceNumber"] = 0x002D1080,
        ["IoctlVolumeGetVolumeDiskExtents"] = 0x00560000,
    };

    /// <summary>
    /// The one contained exception (the owner's decision, 2026-10-24): the
    /// elevated RST reader, and ONLY its project, may also use
    /// IOCTL_SCSI_GET_ADDRESS (a query: which port and path a disk is on) and
    /// IOCTL_SCSI_MINIPORT, Intel RST's pass-through. That one CAN carry any
    /// NVMe command, so what it carries is pinned below: Get Log Page 02h.
    /// </summary>
    private static readonly Dictionary<string, uint> AllowedInRstReader = new()
    {
        ["IoctlStorageQueryProperty"] = 0x002D1400,
        ["IoctlScsiGetAddress"] = 0x00041018,
        ["IoctlScsiMiniport"] = 0x0004D008,
    };

    private static readonly string RstReaderDir = $"{Path.DirectorySeparatorChar}NandWatch.RstReader{Path.DirectorySeparatorChar}";

    private static Dictionary<string, uint> AllowedIn(string path) => path.Contains(RstReaderDir) ? AllowedInRstReader : Allowed;

    private static IEnumerable<(string Path, string Text)> Sources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NandWatch.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Directory.EnumerateFiles(Path.Combine(dir.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p)));
    }

    [Fact]
    public void Every_IOCTL_constant_is_an_allowed_query()
    {
        var found = Sources().SelectMany(s => IoctlConstant().Matches(s.Text).Select(m => (s.Path, Name: m.Groups[1].Value, Value: Convert.ToUInt32(m.Groups[2].Value, 16)))).ToList();
        Assert.NotEmpty(found);
        Assert.All(found, c => Assert.True(AllowedIn(c.Path).TryGetValue(c.Name, out var v) && v == c.Value, $"{c.Name} = 0x{c.Value:x8} in {c.Path} is not an allowed query"));
    }

    [Fact]
    public void Every_DeviceIoControl_call_uses_an_allowed_constant()
    {
        var calls = Sources().SelectMany(s => IoctlCall().Matches(s.Text).Select(m => (s.Path, Code: m.Groups[1].Value))).ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.True(AllowedIn(c.Path).ContainsKey(c.Code.Split('.').Last()), $"DeviceIoControl with '{c.Code}' in {c.Path}"));
    }

    [Fact]
    public void The_RST_reader_builds_one_request_only_Get_Log_Page_for_the_health_log()
    {
        var b = NandWatch.RstReader.RstRequest.HealthLog(pathId: 3);
        Assert.Equal(0x10A4, b.Length);
        Assert.Equal("IntelNvm"u8.ToArray(), b[0x04..0x0C]);
        Assert.Equal(0xF0002808u, BitConverter.ToUInt32(b, 0x10));
        Assert.Equal(0x10A4u - 0x1C, BitConverter.ToUInt32(b, 0x18));
        Assert.Equal(1, b[0x1C]);
        Assert.Equal(3, b[0x1D]);
        Assert.Equal(0x02, b[0x20]);                                   // Get Log Page
        Assert.Equal(0xFFFFFFFFu, BitConverter.ToUInt32(b, 0x24));     // all namespaces: the controller's log
        Assert.Equal(0x007F0002u, BitConverter.ToUInt32(b, 0x48));     // log 02h, 128 dwords
        Assert.All(new[] { 0x4C, 0x50, 0x54, 0x58, 0x5C }, o => Assert.Equal(0u, BitConverter.ToUInt32(b, o)));   // CDW11-15
        Assert.Equal(0xA4u, BitConverter.ToUInt32(b, 0x74));
        Assert.Equal(0x1000u, BitConverter.ToUInt32(b, 0x78));
        Assert.All(b[0xA4..], x => Assert.Equal(0, x));                // nothing is sent as data
    }

    [Fact]
    public void Only_the_request_builder_writes_an_NVMe_command_and_the_reader_sends_only_that()
    {
        var reader = Sources().Where(s => s.Path.Contains(RstReaderDir)).ToList();
        Assert.Equal(2, reader.Count);
        var program = reader.Single(s => s.Path.EndsWith("Program.cs")).Text;
        // The miniport call's buffer comes from RstRequest.HealthLog and nowhere else.
        Assert.Single(MiniportCall().Matches(program));
        Assert.Contains("var buffer = RstRequest.HealthLog(", program);
        Assert.DoesNotContain("0x20]", program);
        Assert.DoesNotContain("Opcode", program, StringComparison.OrdinalIgnoreCase);
        // And no other project builds an Intel pass-through.
        Assert.All(Sources().Where(s => !s.Path.Contains(RstReaderDir)), s => Assert.DoesNotContain("IntelNvm", s.Text));
    }

    [GeneratedRegex(@"DeviceIoControl\(\s*adapter,\s*IoctlScsiMiniport,\s*buffer,")]
    private static partial Regex MiniportCall();

    [GeneratedRegex(@"const\s+uint\s+(Ioctl\w*)\s*=\s*0x([0-9A-Fa-f]+)")]
    private static partial Regex IoctlConstant();

    /// <summary>The second argument of every call, whatever the first is.</summary>
    [GeneratedRegex(@"DeviceIoControl\(\s*[^,()]+,\s*([\w.]+)\s*,")]
    private static partial Regex IoctlCall();
}

using Microsoft.Data.Sqlite;
using NandWatch.Core.Data;

namespace NandWatch.Core.Tests;

/// <summary>A throwaway database in TEMP, deleted afterwards.</summary>
internal sealed class TestDb : IDisposable
{
    public string Dir { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nandwatch-test-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Dir, "live", "nand-watch.db");
    public SqliteConnection Conn { get; }

    public TestDb() => Conn = HealthDb.Open(Path, allowSystemDrive: true);

    public void Dispose()
    {
        Conn.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
    }
}

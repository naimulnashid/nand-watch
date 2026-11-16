using NandWatch.Core.Data;
using NandWatch.Core.Query;
using NandWatch.Core.View;

namespace NandWatch.Core.Tests;

/// <summary>
/// The run log and the alert list are read a page at a time. The pager's own
/// arithmetic (View/Pages.cs) is Onyx's file and is tested there; what is
/// tested here is that a page of rows is the page the pager asked for.
/// </summary>
public class PagingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Runs_come_a_page_at_a_time_newest_first_and_every_run_is_on_one_page()
    {
        using var db = new TestDb();
        for (var i = 0; i < 60; i++) HealthDb.StartRun(db.Conn, "test", T0.AddHours(i));

        Assert.Equal(60, HealthQueries.RunCount(db.Conn));
        var first = HealthQueries.Runs(db.Conn, 25);
        var second = HealthQueries.Runs(db.Conn, 25, 25);
        var third = HealthQueries.Runs(db.Conn, 25, 50);
        Assert.Equal(T0.AddHours(59), first[0].Started);
        Assert.Equal(T0.AddHours(34), second[0].Started);
        Assert.Equal(10, third.Count);
        Assert.Equal(T0, third[^1].Started);
        Assert.Equal(60, first.Concat(second).Concat(third).Select(r => r.Id).Distinct().Count());
        // Qualified: the tests have a Pages of their own, the NVMe log pages.
        Assert.Equal(3, NandWatch.Core.View.Pages.Count(60, 25));
    }

    [Fact]
    public void Alerts_come_a_page_at_a_time_and_a_history_without_the_table_counts_none()
    {
        using var db = new TestDb();
        for (var i = 0; i < 7; i++) HealthDb.InsertAlert(db.Conn, null, new Alert("spare", AlertLevel.Warning, $"t{i}", "b"), T0.AddHours(i));

        Assert.Equal(7, HealthQueries.AlertCount(db.Conn));
        Assert.Equal(["t6", "t5", "t4"], HealthQueries.Alerts(db.Conn, 3).Select(a => a.Title));
        Assert.Equal(["t0"], HealthQueries.Alerts(db.Conn, 3, 6).Select(a => a.Title));

        HealthDb.Command(db.Conn, "DROP TABLE alerts;").ExecuteNonQuery();
        Assert.Equal(0, HealthQueries.AlertCount(db.Conn));
        Assert.Empty(HealthQueries.Alerts(db.Conn, 3));
    }
}

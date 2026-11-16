namespace NandWatch.Core.View;

/// <summary>How a table is ordered, which decides what the pager's end buttons say.</summary>
public enum PageOrder { Rank, Time }

/// <summary>The four step labels of a pager.</summary>
public sealed record PagerWords(string First, string Prev, string Next, string Last);

/// <summary>
/// Pager arithmetic (rulebook: components, "Pagination"). The same rules as
/// reference/web/pagerRules.ts, one for one.
/// </summary>
public static class Pages
{
    /// <summary>One page size for every table in an app.</summary>
    public const int PageSize = 50;

    public static int Count(int total, int pageSize = PageSize) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, total) / (double)pageSize));

    /// <summary>
    /// The numbers a pager shows: first, last, the current page with two either
    /// side, and a gap (null) where pages are skipped: 1 … 4 5 6 7 8 … 12.
    /// A gap never stands in for a single page; that page is shown instead.
    /// </summary>
    public static List<int?> Items(int page, int count, int around = 2)
    {
        if (count <= 1) return [1];
        var current = Math.Clamp(page, 1, count);
        var shown = new SortedSet<int> { 1, count };
        for (var p = current - around; p <= current + around; p++)
            if (p >= 1 && p <= count) shown.Add(p);
        var items = new List<int?>();
        int? previous = null;
        foreach (var p in shown)
        {
            if (previous is { } prev)
            {
                if (p - prev == 2) items.Add(prev + 1);
                else if (p - prev > 2) items.Add(null);
            }
            items.Add(p);
            previous = p;
        }
        return items;
    }

    /// <summary>A requested page clamped to 1..count; what the jump box feeds.</summary>
    public static int Clamp(int page, int count) => Math.Clamp(page, 1, Math.Max(1, count));

    /// <summary>The rows a page shows, 1-indexed and inclusive.</summary>
    public static (int From, int To) Range(int page, int total, int pageSize = PageSize)
    {
        if (total <= 0) return (0, 0);
        var current = Clamp(page, Count(total, pageSize));
        return ((current - 1) * pageSize + 1, Math.Min(current * pageSize, total));
    }

    /// <summary>"Showing 51-100 of 312 rows", or "No rows".</summary>
    public static string RangeSentence(int page, int total, string noun = "rows", int pageSize = PageSize)
    {
        if (total <= 0) return $"No {noun}";
        var (from, to) = Range(page, total, pageSize);
        return $"Showing {from:N0}-{to:N0} of {total:N0} {noun}";
    }

    /// <summary>Newest / Newer / Older / Oldest on a newest-first log; First / Prev / Next / Last otherwise.</summary>
    public static PagerWords Words(PageOrder order) => order == PageOrder.Time
        ? new PagerWords("Newest", "Newer", "Older", "Oldest")
        : new PagerWords("First", "Prev", "Next", "Last");
}

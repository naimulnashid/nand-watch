using Microsoft.UI.Xaml;
using NandWatch.App.State;

namespace NandWatch.App.Views;

public enum PageKind
{
    Overview,
    Events,
    Sync,

    /// <summary>One drive's charts, reached from its panel on the Overview.</summary>
    Drive,
}

public sealed record Route(PageKind Kind, long? DriveId = null);

/// <summary>What every page needs from the shell.</summary>
public sealed class PageContext
{
    public required AppState State { get; init; }
    public required Action<Route> Navigate { get; init; }

    /// <summary>Reloads and rebuilds the current page in place, keeping the scroll position.</summary>
    public required Action Redraw { get; init; }

    /// <summary>Sync now: read every drive and write the backup.</summary>
    public required Func<Task> SyncNow { get; init; }

    public required Window Window { get; init; }
}

/// <summary>
/// A page reads its data off the UI thread, then builds its tree from it.
/// Keeping the two apart is what keeps the window responsive.
/// </summary>
public interface IPage
{
    /// <summary>Runs on a worker thread: queries only, no UI.</summary>
    void Load();

    /// <summary>Runs on the UI thread, after <see cref="Load"/>.</summary>
    UIElement Build();

    /// <summary>
    /// Fills the page with stand-in data in place of <see cref="Load"/>, so
    /// <see cref="Build"/> can draw its loading skeleton. No database access.
    /// </summary>
    void Placeholder();
}

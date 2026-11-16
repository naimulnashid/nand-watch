using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using NandWatch.App.Theme;
using NandWatch.Core.View;
using Windows.Foundation;

namespace NandWatch.App.Controls;

/// <summary>
/// The chip: a pill with a hairline edge that is pressed. The active one is
/// SOLID, accent-fill with the on-accent text, never the plain accent.
/// The web's <c>.chip</c>. Lifted from Data Usage Native's <c>Ui.Chip</c>.
/// </summary>
public static class OnyxChip
{
    public static Button Of(string text, bool active = false, bool small = false)
    {
        var label = OnyxText.Of(text, small ? TextStyle.Caption : TextStyle.Small,
            active ? OnyxTheme.OnAccentBrush : OnyxTheme.TextDimBrush, 500, numeric: true, selectable: false);
        var fill = active ? OnyxTheme.AccentFillBrush : OnyxTheme.TransparentBrush;
        var edge = active ? OnyxTheme.AccentFillBrush : OnyxTheme.BorderStrongBrush;
        var chip = new Button
        {
            Content = label,
            Padding = small ? new Thickness(10.4, 3, 10.4, 4) : new Thickness(13.6, 5.4, 13.6, 6.4),
            MinWidth = 0,
            MinHeight = 0,
            // Half the height: WinUI does not clamp an oversized radius as CSS does.
            CornerRadius = new CornerRadius(small ? 13 : 16),
            BorderThickness = new Thickness(1),
            Background = fill,
            BorderBrush = edge,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Resources["ButtonBackgroundPointerOver"] = active ? fill : OnyxTheme.SurfaceHoverBrush;
        chip.Resources["ButtonBackgroundPressed"] = active ? fill : OnyxTheme.SurfaceHoverBrush;
        chip.Resources["ButtonBorderBrushPointerOver"] = edge;
        chip.Resources["ButtonBorderBrushPressed"] = edge;
        // A step that cannot be taken keeps its shape and is dimmed as a whole
        // (Pager sets the opacity), not repainted in the system's greys.
        chip.Resources["ButtonBackgroundDisabled"] = fill;
        chip.Resources["ButtonBorderBrushDisabled"] = edge;
        chip.PointerEntered += (_, _) => { if (!active) label.Foreground = OnyxTheme.TextBrush; };
        chip.PointerExited += (_, _) => { if (!active) label.Foreground = OnyxTheme.TextDimBrush; };
        return chip;
    }
}

/// <summary>
/// The one pager, under every table that does not show all its rows
/// (rulebook: components, "Pager"). The same design as reference/web/Pager.tsx:
/// <code>
/// Showing 51-100 of 312 rows      First  Prev  1 … 4 5 6 … 12  Next  Last   Go to page [ 5 ] of 12  Go
/// </code>
/// On a newest-first log (<see cref="PageOrder.Time"/>) the ends read Newest,
/// Newer, Older, Oldest. The arithmetic is <see cref="Pages"/>.
/// </summary>
/// <remarks>
/// The pager owns the page number: a press sets <see cref="Page"/>, redraws
/// the pager and raises <see cref="PageChanged"/>, on which the page loads
/// its rows. When the total changes (rows pruned, a filter), call
/// <see cref="Set"/>: a page past the new end becomes the last page.
/// Copy with Theme.cs and Onyx.Core/Pages.cs.
/// </remarks>
public sealed class Pager : UserControl
{
    private readonly string _noun;
    private readonly PageOrder _order;
    private readonly int _pageSize;

    public int Page { get; private set; }
    public int Total { get; private set; }

    /// <summary>Raised with the new page after a press, never by <see cref="Set"/>.</summary>
    public event Action<int>? PageChanged;

    /// <param name="total">Rows in the whole result, not on this page.</param>
    /// <param name="noun">What a row is, plural: "rows", "runs", "events".</param>
    public Pager(int page, int total, string noun = "rows", PageOrder order = PageOrder.Rank, int pageSize = Pages.PageSize)
    {
        _noun = noun;
        _order = order;
        _pageSize = pageSize;
        IsTabStop = false;
        Set(page, total);
    }

    public void Set(int page, int total)
    {
        Total = Math.Max(0, total);
        Page = Pages.Clamp(page, Pages.Count(Total, _pageSize));
        Content = Build();
    }

    private void Go(int page)
    {
        var to = Pages.Clamp(page, Pages.Count(Total, _pageSize));
        if (to == Page) return;
        Set(to, Total);
        PageChanged?.Invoke(to);
    }

    private Border Build()
    {
        var count = Pages.Count(Total, _pageSize);
        var words = Pages.Words(_order);
        var flow = new PagerFlow();

        var range = OnyxText.Of(Pages.RangeSentence(Page, Total, _noun, _pageSize), TextStyle.Small, OnyxTheme.TextDimBrush, numeric: true);
        range.VerticalAlignment = VerticalAlignment.Center;
        flow.Children.Add(range);

        if (count > 1)
        {
            var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7.2 };
            AutomationProperties.SetName(steps, $"{_noun} pages");
            steps.Children.Add(Step(words.First, 1, Page > 1, "First page"));
            steps.Children.Add(Step(words.Prev, Page - 1, Page > 1, "Previous page"));

            var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4.8, Margin = new Thickness(5.6, 0, 5.6, 0) };
            foreach (var item in Pages.Items(Page, count))
            {
                if (item is not { } n)
                {
                    var gap = OnyxText.Of("…", TextStyle.Small, OnyxTheme.TextFaintBrush, selectable: false);
                    gap.VerticalAlignment = VerticalAlignment.Center;
                    gap.Margin = new Thickness(2.4, 0, 2.4, 0);
                    numbers.Children.Add(gap);
                    continue;
                }
                var chip = OnyxChip.Of(n.ToString("N0", CultureInfo.InvariantCulture), n == Page);
                chip.MinWidth = 38.4;
                chip.Padding = new Thickness(8.8, chip.Padding.Top, 8.8, chip.Padding.Bottom);
                AutomationProperties.SetName(chip, n == Page ? $"Page {n}, current" : $"Page {n}");
                chip.Click += (_, _) => Go(n);
                numbers.Children.Add(chip);
            }
            steps.Children.Add(numbers);

            steps.Children.Add(Step(words.Next, Page + 1, Page < count, "Next page"));
            steps.Children.Add(Step(words.Last, count, Page < count, "Last page"));
            flow.Children.Add(steps);
            flow.Children.Add(Jump(count));
        }

        // The rule above the pager is the table's last line.
        return new Border
        {
            Margin = new Thickness(0, 20, 0, 0),
            Padding = new Thickness(0, 17.6, 0, 0),
            BorderBrush = OnyxTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = flow,
        };
    }

    private Button Step(string label, int to, bool enabled, string tip)
    {
        var chip = OnyxChip.Of(label);
        // Disabled, so it is neither pressed nor reached by Tab.
        chip.IsEnabled = enabled;
        chip.Opacity = enabled ? 1 : 0.35;
        if (enabled) ToolTipService.SetToolTip(chip, tip);
        chip.Click += (_, _) => Go(to);
        return chip;
    }

    /// <summary>Straight to any page, rather than a walk through the ones between.</summary>
    private StackPanel Jump(int count)
    {
        var jump = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6.4 };
        var label = OnyxText.Of("Go to page", TextStyle.Small, OnyxTheme.TextDimBrush, selectable: false);
        label.VerticalAlignment = VerticalAlignment.Center;
        jump.Children.Add(label);

        var box = new TextBox
        {
            Text = Page.ToString(CultureInfo.InvariantCulture),
            Width = 67,
            MinWidth = 0,
            MinHeight = 0,
            MaxLength = 9,
            Padding = new Thickness(8, 4.8, 8, 4.8),
            TextAlignment = TextAlignment.Center,
            FontFamily = OnyxFonts.Sans,
            FontSize = OnyxText.Size(TextStyle.Small),
            CornerRadius = new CornerRadius(OnyxMetrics.RadiusSm),
            BorderThickness = new Thickness(1),
            Background = OnyxTheme.SurfaceInsetBrush,
            BorderBrush = OnyxTheme.BorderInputBrush,
            Foreground = OnyxTheme.TextBrush,
            SelectionHighlightColor = OnyxTheme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } },
        };
        foreach (var state in new[] { "", "PointerOver", "Focused" })
        {
            box.Resources[$"TextControlBackground{state}"] = OnyxTheme.SurfaceInsetBrush;
            box.Resources[$"TextControlForeground{state}"] = OnyxTheme.TextBrush;
            box.Resources[$"TextControlBorderBrush{state}"] = state == "Focused" ? OnyxTheme.AccentBrush : OnyxTheme.BorderInputBrush;
        }
        // WinUI thickens the bottom edge of a focused field; here the whole edge turns accent instead.
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        Typography.SetNumeralAlignment(box, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(box, $"Page number, 1 to {count}");
        box.BeforeTextChanging += (_, e) => e.Cancel = e.NewText.Any(c => c is < '0' or > '9');
        box.GotFocus += (_, _) => box.SelectAll();

        void Submit()
        {
            // Anything unusable goes back to the page that is showing.
            if (int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1) Go(page);
            box.Text = Page.ToString(CultureInfo.InvariantCulture);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            Submit();
        };
        jump.Children.Add(box);

        var of = OnyxText.Of($"of {count.ToString("N0", CultureInfo.InvariantCulture)}", TextStyle.Small, OnyxTheme.TextDimBrush, numeric: true, selectable: false);
        of.VerticalAlignment = VerticalAlignment.Center;
        jump.Children.Add(of);

        var go = OnyxChip.Of("Go", small: true);
        go.Click += (_, _) => Submit();
        jump.Children.Add(go);
        return jump;
    }

    /// <summary>
    /// The pager's row: the range sentence at the left edge and the rest at the
    /// right, on one line while they fit. In a narrower card the parts wrap
    /// onto lines of their own, from the left, so nothing is cut off and
    /// nothing scrolls sideways. The web's <c>.pager</c> flex row.
    /// </summary>
    private sealed class PagerFlow : Panel
    {
        private const double GapX = 16, GapY = 12;
        private readonly List<List<UIElement>> _rows = [];

        protected override Size MeasureOverride(Size available)
        {
            _rows.Clear();
            double width = 0, height = 0, rowWidth = 0, rowHeight = 0;
            List<UIElement>? row = null;
            foreach (var child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = child.DesiredSize;
                if (row is null || rowWidth + GapX + size.Width > available.Width)
                {
                    if (row is not null)
                    {
                        width = Math.Max(width, rowWidth);
                        height += rowHeight + GapY;
                    }
                    _rows.Add(row = []);
                    rowWidth = size.Width;
                    rowHeight = size.Height;
                }
                else
                {
                    rowWidth += GapX + size.Width;
                    rowHeight = Math.Max(rowHeight, size.Height);
                }
                row.Add(child);
            }
            width = Math.Max(width, rowWidth);
            height += rowHeight;
            return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double y = 0;
            foreach (var row in _rows)
            {
                var height = row.Max(c => c.DesiredSize.Height);
                var rest = row.Skip(1).Sum(c => c.DesiredSize.Width) + GapX * Math.Max(0, row.Count - 2);
                // The first part keeps the left edge; whatever shares its line ends at the right edge.
                var x = 0.0;
                for (var i = 0; i < row.Count; i++)
                {
                    var size = row[i].DesiredSize;
                    if (i == 1) x = Math.Max(x, final.Width - rest);
                    row[i].Arrange(new Rect(x, y + (height - size.Height) / 2, size.Width, size.Height));
                    x += size.Width + GapX;
                }
                y += height + GapY;
            }
            return final;
        }
    }
}

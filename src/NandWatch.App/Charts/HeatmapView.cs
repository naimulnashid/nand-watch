using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using NandWatch.App.Theme;
using NandWatch.Core.View;
using Windows.Foundation;

namespace NandWatch.App.Charts;

/// <summary>
/// One block of the Activity heat map (Data Usage Native's): 26 week columns,
/// a weekday column and a month row on one set of tracks, so the three stay
/// registered. Cells are fluid: the block fills its panel and stays square.
/// </summary>
/// <remarks>
/// Four kinds of cell: hidden (before the first reading, after today) is not
/// drawn; unknown (the drive ran, nothing collected) is the neutral
/// <c>HeatNone</c> with an outline; off is the ramp's zero; a known day takes
/// the ramp. <c>max</c> is passed in so a drive's blocks share one scale.
/// </remarks>
public sealed class HeatmapView : Canvas
{
    private const double DayColumn = 34, Gap = 3, MonthRow = 15, MinCell = 11, Gutter = 7;

    private readonly HeatmapBlock _block;
    private readonly long _max;
    private double _lastWidth = -1;

    public HeatmapView(HeatmapBlock block, long max)
    {
        _block = block;
        _max = max;
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _lastWidth) < 0.5) return;
            _lastWidth = e.NewSize.Width;
            Draw();
        };
        Height = CellSize(1000) * 7 + Gap * 7 + MonthRow + Gutter * 2;
    }

    private static double CellSize(double width) =>
        Math.Max(MinCell, (width - Gutter * 2 - DayColumn - Gap * Heatmap.Weeks) / Heatmap.Weeks);

    private void Draw()
    {
        Children.Clear();
        var width = ActualWidth;
        if (width <= 0) return;
        var cell = CellSize(width);
        Height = Gutter + MonthRow + Gap + 7 * cell + 6 * Gap + Gutter + 3;
        double X(int column) => Gutter + DayColumn + Gap + column * (cell + Gap);
        double Y(int row) => Gutter + MonthRow + Gap + row * (cell + Gap);
        // Anything drawn after layout must ask whether it sits in a skeleton.
        var skeleton = Views.Skeleton.Contains(this);

        foreach (var (label, column) in _block.Months)
        {
            var text = Ui.Text(label, 12, 500, Palette.TextFaintBrush, selectable: false);
            SetLeft(text, X(column));
            SetTop(text, Gutter - 2);
            Children.Add(text);
        }
        for (var row = 0; row < 7; row++)
        {
            var text = Ui.Text(Heatmap.DayLabels[row], 11, 400, Palette.TextFaintBrush, selectable: false);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(text, Gutter);
            SetTop(text, Y(row) + (cell - text.DesiredSize.Height) / 2);
            Children.Add(text);
        }

        foreach (var c in _block.Cells)
        {
            if (c.Kind == CellKind.Hidden) continue;
            var known = c.Kind is CellKind.Known or CellKind.Off;
            var rect = new Rectangle
            {
                Width = cell,
                Height = cell,
                RadiusX = 3,
                RadiusY = 3,
                Fill = skeleton ? Palette.InsetBrush : known ? Palette.Heat[Heatmap.Step(c.Bytes, _max)] : Palette.HeatNoneBrush,
                // Days with no reading carry an outline, so they cannot read as quiet ones.
                Stroke = known ? Palette.TransparentBrush : Palette.HeatNoneRingBrush,
                StrokeThickness = 1,
                CenterPoint = new Vector3((float)cell / 2, (float)cell / 2, 0),
                ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(140) },
            };
            SetLeft(rect, X(c.Column));
            SetTop(rect, Y(c.Row));
            var rest = rect.Stroke;
            rect.PointerEntered += (_, _) =>
            {
                rect.Scale = new Vector3(1.14f, 1.14f, 1);
                rect.Stroke = Palette.TextFaintBrush;
                SetZIndex(rect, 1);
            };
            rect.PointerExited += (_, _) =>
            {
                rect.Scale = Vector3.One;
                rect.Stroke = rest;
                SetZIndex(rect, 0);
            };
            ToolTipService.SetToolTip(rect, Ui.TipContent(Tip(c)));
            Children.Add(rect);
        }
    }

    private static string Tip(HeatmapCell c) => c.Kind switch
    {
        CellKind.Unknown => $"{Format.DayLong(c.Date)}: no reading",
        CellKind.Off => $"{Format.DayLong(c.Date)}: off, nothing written",
        _ => $"{Format.DayLong(c.Date)}: {Format.BytesText(c.Bytes)} written" + (c.IncludesGap ? ", with writes from the unrecorded stretch before it" : ""),
    };

    /// <summary>The legend row: the summary on the left, the Less-More scale on the right.</summary>
    public static Grid Legend(long total, int activeDays, string span)
    {
        var legend = new Grid { Margin = new Thickness(0, 16, 0, 0), ColumnSpacing = 16 };
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        legend.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summary = Ui.Text($"{Format.BytesText(total)} written across {activeDays} active {(activeDays == 1 ? "day" : "days")} {span}", 13.5, 400, Palette.TextFaintBrush, wrap: true);
        summary.VerticalAlignment = VerticalAlignment.Center;
        legend.Children.Add(summary);
        var scale = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        scale.Children.Add(Ui.Text("Less", 13.5, 400, Palette.TextFaintBrush, selectable: false));
        foreach (var brush in Palette.Heat)
            scale.Children.Add(new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(3), Background = brush, VerticalAlignment = VerticalAlignment.Center });
        scale.Children.Add(Ui.Text("More", 13.5, 400, Palette.TextFaintBrush, selectable: false));
        Grid.SetColumn(scale, 1);
        legend.Children.Add(scale);
        return legend;
    }
}

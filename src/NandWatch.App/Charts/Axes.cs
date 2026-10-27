using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using NandWatch.App.Theme;
using NandWatch.Core.View;
using Windows.Foundation;

namespace NandWatch.App.Charts;

/// <summary>The axis furniture every chart here shares (Data Usage Native's, taking a tick format).</summary>
public static class Axes
{
    /// <summary>
    /// Horizontal grid lines and their labels from zero, on Recharts' nice
    /// ticks. Returns the value mapped to the top of the plot.
    /// </summary>
    public static double YGrid(Canvas canvas, double max, double plotLeft, double plotRight, double plotTop, double plotBottom, Func<double, string> tick)
    {
        var ticks = ChartKit.NiceTicks(max);
        var top = ticks[^1];
        foreach (var t in ticks)
        {
            var y = plotBottom - (top > 0 ? t / top : 0) * (plotBottom - plotTop);
            canvas.Children.Add(ChartKit.HLine(plotLeft, plotRight, y, Palette.GridBrush));
            ChartKit.Label(canvas, tick(t), plotLeft - 8, y, 1);
        }
        return top;
    }

    /// <summary>A byte tick without trailing zeros: "15 GB", "1.5 TB".</summary>
    public static string ByteTick(double bytes)
    {
        var (value, unit) = Format.Bytes(bytes);
        if (value.Contains('.')) value = value.TrimEnd('0').TrimEnd('.');
        return $"{value} {unit}";
    }

    /// <summary>Day labels under a category axis, thinned so none overlap, the last kept.</summary>
    public static void DayLabels(Canvas canvas, IReadOnlyList<DateOnly> days, IReadOnlyList<double> centres, double y, double width, double gap = 28)
    {
        var labels = days.Select(Format.DayShort).ToList();
        var widths = labels.Select(l => ChartKit.MeasureText(l)).ToList();
        var keep = ChartKit.ThinLabels(centres, widths, gap, 0, width);
        for (var i = 0; i < labels.Count; i++)
            if (keep.Contains(i)) ChartKit.Label(canvas, labels[i], ChartKit.ClampCentre(centres[i], widths[i], 0, width), y, 0);
    }

    /// <summary>Time labels under a time axis, at round local times (<see cref="TimeTicks"/>).</summary>
    public static void TimeLabels(Canvas canvas, DateTimeOffset start, DateTimeOffset end, Func<DateTimeOffset, double> x, double y, double width)
    {
        var maxTicks = Math.Max(2, (int)(width / 90));
        foreach (var t in TimeTicks.For(start, end, TimeZoneInfo.Local, maxTicks))
        {
            var at = x(t.At);
            ChartKit.Label(canvas, t.Label, ChartKit.ClampCentre(at, ChartKit.MeasureText(t.Label), 0, width), y, 0);
        }
    }

    /// <summary>A dashed horizontal reference (a rating, a warning temperature), labelled at its right end.</summary>
    public static void Reference(Canvas canvas, double y, double plotLeft, double plotRight, string label, Brush brush)
    {
        canvas.Children.Add(new Line { X1 = plotLeft, X2 = plotRight, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 1.5, StrokeDashArray = [5, 4] });
        ChartKit.Label(canvas, label, plotRight - 4, y - 11, 1, 12.5, brush);
    }

    /// <summary>Grows a layer up from the baseline.</summary>
    public static void GrowUp(UIElement layer, double baseline, int ms = 800)
    {
        var scale = new ScaleTransform { ScaleY = 0, CenterY = baseline };
        layer.RenderTransform = scale;
        var story = new Storyboard();
        var grow = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(ms)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(grow, scale);
        Storyboard.SetTargetProperty(grow, "ScaleY");
        story.Children.Add(grow);
        story.Begin();
    }

    /// <summary>Reveals a layer from the left.</summary>
    public static void RevealLeft(UIElement layer, double width, int ms = 900)
    {
        var transform = new ScaleTransform { ScaleX = 0, CenterX = 0 };
        layer.Clip = new RectangleGeometry { Rect = new Rect(0, -10, width + 20, 10_000), Transform = transform };
        var story = new Storyboard();
        var grow = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(ms)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(grow, transform);
        Storyboard.SetTargetProperty(grow, "ScaleX");
        story.Children.Add(grow);
        story.Begin();
    }

    /// <summary>A rectangle with only its top corners rounded.</summary>
    public static Geometry RoundedTop(Rect r, double radius)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width / 2, r.Height)));
        var figure = new PathFigure { StartPoint = new Point(r.Left, r.Bottom), IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = new Point(r.Left, r.Top + radius) });
        figure.Segments.Add(new ArcSegment { Point = new Point(r.Left + radius, r.Top), Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = new Point(r.Right - radius, r.Top) });
        figure.Segments.Add(new ArcSegment { Point = new Point(r.Right, r.Top + radius), Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = new Point(r.Right, r.Bottom) });
        return new PathGeometry { Figures = { figure } };
    }

    public static Ellipse Dot(Point at, double r, Brush fill)
    {
        var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = Palette.SurfaceBrush, StrokeThickness = 2 };
        Canvas.SetLeft(dot, at.X - r);
        Canvas.SetTop(dot, at.Y - r);
        return dot;
    }

    /// <summary>"No reading": a day nothing was collected and a quiet day are different answers.</summary>
    public static TextBlock NoData(string text = "No reading") => Ui.Text(text, 14, 400, Palette.TextMutedBrush);
}

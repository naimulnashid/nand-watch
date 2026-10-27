using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NandWatch.App.Theme;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using Windows.Foundation;
using Color = global::Windows.UI.Color;

namespace NandWatch.App.Charts;

/// <summary>One line: its name and colour, and its points cut into runs where readings stopped.</summary>
public sealed record LineSeries(string Name, Color Color, IReadOnlyList<List<TimePoint>> Runs, bool Fill = false);

/// <summary>A dashed horizontal reference: a rating, a warning temperature.</summary>
public sealed record Reference(double Value, string Label, Brush Brush);

/// <summary>A dashed straight segment ahead of the data: a projection at one pace.</summary>
public sealed record Projected(TimePoint From, TimePoint To, Brush Brush);

/// <summary>
/// Lines over real time, for spans from two days to a decade: temperature,
/// free space, and the life projection. A gap in the readings is a break in
/// the line; references and projections are dashed, so nothing measured is
/// ever drawn the way a guess is.
/// </summary>
public sealed class TimeLineChart : ChartSurface
{
    private const double Top = 10, Right = 10, Bottom = 4, Left = 4, YAxisWidth = 64, XAxisHeight = 28;

    private readonly IReadOnlyList<LineSeries> _series;
    private readonly IReadOnlyList<Reference> _references;
    private readonly IReadOnlyList<Projected> _projections;
    private readonly DateTimeOffset _start, _end;
    private readonly double _yMax;
    private readonly Func<double, string> _tick, _value;
    private readonly bool _daily;
    private Line? _cursor;
    private readonly List<Ellipse> _dots = [];
    private double _plotLeft, _plotRight, _plotBottom, _top;

    /// <param name="value">A point's value as the tooltip writes it.</param>
    /// <param name="daily">Points are a day's last reading: the tooltip names the day, not the minute.</param>
    public TimeLineChart(IReadOnlyList<LineSeries> series, DateTimeOffset start, DateTimeOffset end, double yMax,
        Func<double, string> tick, Func<double, string> value, IReadOnlyList<Reference>? references = null,
        IReadOnlyList<Projected>? projections = null, bool daily = false, double height = 280, bool animate = true) : base(height, animate)
    {
        _series = series;
        _start = start;
        _end = end > start ? end : start.AddHours(1);
        _yMax = yMax;
        _tick = tick;
        _value = value;
        _references = references ?? [];
        _projections = projections ?? [];
        _daily = daily;
    }

    private double X(DateTimeOffset t) => _plotLeft + (t - _start).TotalSeconds / (_end - _start).TotalSeconds * (_plotRight - _plotLeft);

    private double Y(double v) => _plotBottom - (_top > 0 ? Math.Clamp(v / _top, 0, 1.05) : 0) * (_plotBottom - Top);

    protected override void Draw(bool animate)
    {
        _plotLeft = Left + YAxisWidth;
        _plotRight = W - Right;
        _plotBottom = H - Bottom - XAxisHeight;
        _top = Axes.YGrid(Canvas, _yMax, _plotLeft, _plotRight, Top, _plotBottom, _tick);
        Axes.TimeLabels(Canvas, _start, _end, X, _plotBottom + 6 + 11, W);

        foreach (var r in _references) Axes.Reference(Canvas, Y(r.Value), _plotLeft, _plotRight, r.Label, r.Brush);

        var layer = new Microsoft.UI.Xaml.Controls.Canvas();
        foreach (var s in _series)
        {
            var stroke = new SolidColorBrush(s.Color);
            foreach (var run in s.Runs)
            {
                var pts = run.Select(p => new Point(X(p.At), Y(p.Value))).ToList();
                if (pts.Count == 1)
                {
                    layer.Children.Add(Axes.Dot(pts[0], 2.5, stroke));
                    continue;
                }
                if (s.Fill)
                {
                    var fill = ChartKit.MonotoneFigure(pts);
                    fill.Segments.Add(new LineSegment { Point = new Point(pts[^1].X, _plotBottom) });
                    fill.Segments.Add(new LineSegment { Point = new Point(pts[0].X, _plotBottom) });
                    fill.IsClosed = true;
                    layer.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                    {
                        Data = new PathGeometry { Figures = { fill } },
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Point(0, 0),
                            EndPoint = new Point(0, 1),
                            GradientStops =
                            {
                                new GradientStop { Color = Palette.WithAlpha(s.Color, 0.45), Offset = 0 },
                                new GradientStop { Color = Palette.WithAlpha(s.Color, 0.03), Offset = 1 },
                            },
                        },
                    });
                }
                layer.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = new PathGeometry { Figures = { ChartKit.MonotoneFigure(pts) } },
                    Stroke = stroke,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                });
            }
        }
        foreach (var p in _projections)
            layer.Children.Add(new Line
            {
                X1 = X(p.From.At), Y1 = Y(p.From.Value), X2 = X(p.To.At), Y2 = Y(p.To.Value),
                Stroke = p.Brush, StrokeThickness = 2, StrokeDashArray = [4, 3],
            });
        Canvas.Children.Add(layer);
        if (animate) Axes.RevealLeft(layer, W);

        _cursor = new Line { Stroke = Palette.ChartCursorBrush, StrokeThickness = 1, StrokeDashArray = [4, 4], Visibility = Visibility.Collapsed, Y1 = Top, Y2 = _plotBottom };
        Canvas.Children.Add(_cursor);
        _dots.Clear();
        foreach (var s in _series)
        {
            var dot = Axes.Dot(new Point(0, 0), 5, new SolidColorBrush(s.Color));
            dot.Visibility = Visibility.Collapsed;
            _dots.Add(dot);
            Canvas.Children.Add(dot);
        }
    }

    protected override void OnHover(Point at)
    {
        if (_cursor is null || at.X < _plotLeft || at.X > _plotRight)
        {
            ClearHover();
            return;
        }
        // The pointer's moment, then each series' reading nearest to it.
        var t = _start + TimeSpan.FromSeconds((at.X - _plotLeft) / (_plotRight - _plotLeft) * (_end - _start).TotalSeconds);
        var nearest = _series.Select(s => s.Runs.SelectMany(r => r).OrderBy(p => Math.Abs((p.At - t).TotalSeconds)).FirstOrDefault()).ToList();
        var anchor = nearest.Where(p => p is not null).OrderBy(p => Math.Abs((p!.At - t).TotalSeconds)).FirstOrDefault();
        // Too far from any reading: a gap, or the projection ahead of today.
        var tolerance = _daily ? TimeSpan.FromHours(36) : TimeSpan.FromHours(2);
        if (anchor is null || Math.Abs((anchor.At - t).TotalSeconds) > tolerance.TotalSeconds)
        {
            ClearHover();
            return;
        }

        var x = X(anchor.At);
        _cursor.X1 = _cursor.X2 = x;
        _cursor.Visibility = Visibility.Visible;
        var body = ChartTooltip.Stack();
        var local = TimeZoneInfo.ConvertTime(anchor.At, TimeZoneInfo.Local);
        body.Children.Add(ChartTooltip.Title(_daily ? Format.DayLong(DateOnly.FromDateTime(local.DateTime)) : Format.Moment(anchor.At, TimeZoneInfo.Local)));
        for (var i = 0; i < _series.Count; i++)
        {
            var p = nearest[i];
            var close = p is not null && Math.Abs((p.At - anchor.At).TotalSeconds) <= tolerance.TotalSeconds;
            _dots[i].Visibility = close ? Visibility.Visible : Visibility.Collapsed;
            if (!close) continue;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_dots[i], X(p!.At) - 5);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(_dots[i], Y(p.Value) - 5);
            body.Children.Add(_series.Count == 1
                ? ChartTooltip.Big(_value(p.Value), new SolidColorBrush(_series[i].Color))
                : ChartTooltip.Row(_series[i].Color, _series[i].Name, _value(p.Value)));
        }
        Tooltip.Show(body, at);
    }

    protected override void ClearHover()
    {
        base.ClearHover();
        if (_cursor is not null) _cursor.Visibility = Visibility.Collapsed;
        foreach (var d in _dots) d.Visibility = Visibility.Collapsed;
    }
}

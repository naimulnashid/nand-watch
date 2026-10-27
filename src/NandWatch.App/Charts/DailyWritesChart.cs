using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NandWatch.App.Theme;
using NandWatch.Core.Nvme;
using NandWatch.Core.Query;
using NandWatch.Core.View;
using Windows.Foundation;

namespace NandWatch.App.Charts;

/// <summary>
/// Bytes written a day, one bar per day. The three kinds of day stay apart:
/// a known day is a bar (a quiet one, none); a day the drive was off is a
/// true zero; a day it ran with nothing collecting is an outlined slot, never
/// a zero. A bar whose total includes an unrecorded stretch is paler.
/// </summary>
public sealed class DailyWritesChart : ChartSurface
{
    private const double Top = 8, Right = 8, Bottom = 4, Left = 4, YAxisWidth = 64, XAxisHeight = 28;

    private readonly IReadOnlyList<DayWrites> _days;
    private readonly IReadOnlyDictionary<DateOnly, DayWrites> _reads;
    private readonly List<Rect> _slots = [];
    private Rectangle? _highlight;

    public DailyWritesChart(IReadOnlyList<DayWrites> days, IReadOnlyList<DayWrites> reads, double height = 280, bool animate = true) : base(height, animate)
    {
        _days = days;
        _reads = reads.ToDictionary(d => d.Date);
    }

    private static double Bytes(long? units) => (units ?? 0) * (double)HealthLog.BytesPerDataUnit;

    protected override void Draw(bool animate)
    {
        _slots.Clear();
        var plotLeft = Left + YAxisWidth;
        var plotRight = W - Right;
        var plotBottom = H - Bottom - XAxisHeight;
        var plotW = Math.Max(1, plotRight - plotLeft);
        var n = Math.Max(1, _days.Count);
        var slot = plotW / n;
        var barW = Math.Clamp(slot * (slot > 6 ? 0.7 : 0.85), 1, 48);

        var max = _days.Count == 0 ? 0 : _days.Max(d => Bytes(d.Units));
        var top = Axes.YGrid(Canvas, max, plotLeft, plotRight, Top, plotBottom, Axes.ByteTick);
        double Y(double v) => plotBottom - (top > 0 ? v / top : 0) * (plotBottom - Top);

        _highlight = new Rectangle { Fill = Palette.SurfaceHoverBrush, Visibility = Visibility.Collapsed, RadiusX = 3, RadiusY = 3 };
        Canvas.Children.Add(_highlight);

        var bars = new Canvas();
        var centres = new List<double>();
        for (var i = 0; i < _days.Count; i++)
        {
            var d = _days[i];
            var x = plotLeft + i * slot;
            centres.Add(x + slot / 2);
            _slots.Add(new Rect(x, Top, slot, plotBottom - Top));
            switch (d.State)
            {
                case DayState.Unknown:
                    // Outlined, the height of the plot's lowest band: a slot with no answer in it.
                    var ring = new Rectangle
                    {
                        Width = barW, Height = Math.Min(14, plotBottom - Top), RadiusX = 2, RadiusY = 2,
                        Stroke = Palette.HeatNoneRingBrush, StrokeThickness = 1, StrokeDashArray = [2, 2], Fill = Palette.TransparentBrush,
                    };
                    Canvas.SetLeft(ring, x + (slot - barW) / 2);
                    Canvas.SetTop(ring, plotBottom - ring.Height);
                    bars.Children.Add(ring);
                    break;
                case DayState.Known when d.Units > 0:
                    var y = Y(Bytes(d.Units));
                    var rect = new Rect(x + (slot - barW) / 2, y, barW, Math.Max(1, plotBottom - y));
                    bars.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                    {
                        Data = Axes.RoundedTop(rect, Math.Min(4, barW / 2)),
                        Fill = d.IncludesGap ? Palette.AccentDimBrush : Palette.AccentBrush,
                    });
                    break;
            }
        }
        Canvas.Children.Add(bars);
        if (animate) Axes.GrowUp(bars, plotBottom);
        Axes.DayLabels(Canvas, _days.Select(d => d.Date).ToList(), centres, plotBottom + 6 + 11, W);
    }

    protected override void OnHover(Point at)
    {
        var index = _slots.FindIndex(r => at.X >= r.Left && at.X < r.Right);
        if (index < 0 || _highlight is null)
        {
            ClearHover();
            return;
        }
        var r = _slots[index];
        _highlight.Width = r.Width;
        _highlight.Height = r.Height;
        Canvas.SetLeft(_highlight, r.Left);
        Canvas.SetTop(_highlight, r.Top);
        _highlight.Visibility = Visibility.Visible;

        var d = _days[index];
        var body = ChartTooltip.Stack();
        body.Children.Add(ChartTooltip.Title(Format.DayLong(d.Date)));
        switch (d.State)
        {
            case DayState.Unknown:
                body.Children.Add(Axes.NoData("No reading: the drive ran and nothing was collected"));
                break;
            case DayState.Off:
                body.Children.Add(ChartTooltip.Big("0 B", Palette.TextBrush));
                body.Children.Add(ChartTooltip.Muted("The drive was off all day"));
                break;
            default:
                body.Children.Add(ChartTooltip.Big(Format.BytesText(Bytes(d.Units)) + " written", Palette.AccentBrightBrush));
                if (_reads.TryGetValue(d.Date, out var read) && read.State == DayState.Known)
                    body.Children.Add(ChartTooltip.Muted($"{Format.BytesText(Bytes(read.Units))} read"));
                if (d.IncludesGap) body.Children.Add(ChartTooltip.Muted("Includes writes from the unrecorded stretch before it"));
                break;
        }
        Tooltip.Show(body, at);
    }

    protected override void ClearHover()
    {
        base.ClearHover();
        if (_highlight is not null) _highlight.Visibility = Visibility.Collapsed;
    }
}

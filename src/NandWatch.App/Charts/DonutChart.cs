using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NandWatch.App.Theme;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace NandWatch.App.Charts;

/// <summary>
/// A ring for one share of a whole (a volume's used space), with whatever
/// the caller puts in the hole. The sector geometry and the entry are AI
/// Usage Native's DonutChart, cut down to one slice on a track.
/// </summary>
public sealed class DonutChart : Grid
{
    private readonly Canvas _canvas = new();
    private readonly double _fraction;
    private readonly Brush _fill;
    private readonly Brush _track;
    private readonly bool _animate;

    /// <param name="track">The rest of the ring: the fill's dim variant, so the whole reads as one ring in both themes.</param>
    public DonutChart(double fraction, Brush fill, Brush track, UIElement centre, double size = 168, bool animate = true)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        _fill = fill;
        _track = track;
        _animate = animate && Motion.Enabled;
        Width = size;
        Height = size;
        Children.Add(_canvas);
        if (centre is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.IsHitTestVisible = false;
        }
        Children.Add(centre);
        Loaded += (_, _) => Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        var radius = Math.Min(Width, Height) / 2;
        var outer = radius * 0.96;
        var inner = radius * 0.74;
        var cx = Width / 2;
        var cy = Height / 2;

        // The track is the whole; the share runs clockwise from twelve o'clock.
        _canvas.Children.Add(new Path { Data = Sector(cx, cy, inner, outer, 0, 360), Fill = _track });
        if (_fraction > 0)
            _canvas.Children.Add(new Path { Data = Sector(cx, cy, inner, outer, 0, _fraction * 360), Fill = _fill });

        if (_animate)
        {
            var scale = new ScaleTransform { CenterX = cx, CenterY = cy, ScaleX = 0.86, ScaleY = 0.86 };
            _canvas.RenderTransform = scale;
            _canvas.Opacity = 0;
            var story = new Storyboard();
            foreach (var (target, property, from) in new (DependencyObject, string, double)[] { (scale, "ScaleX", 0.86), (scale, "ScaleY", 0.86), (_canvas, "Opacity", 0) })
            {
                var anim = new DoubleAnimation { From = from, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(850)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, property);
                story.Children.Add(anim);
            }
            story.Begin();
        }
    }

    /// <summary>A ring sector between two angles, in degrees clockwise from twelve o'clock.</summary>
    private static Geometry Sector(double cx, double cy, double inner, double outer, double startDeg, double endDeg)
    {
        Point At(double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
        }
        // An arc cannot end where it starts: a full ring stops just short.
        if (endDeg - startDeg >= 359.99) endDeg = startDeg + 359.99;
        var large = endDeg - startDeg > 180;
        var figure = new PathFigure { StartPoint = At(outer, startDeg), IsClosed = true };
        figure.Segments.Add(new ArcSegment { Point = At(outer, endDeg), Size = new Size(outer, outer), IsLargeArc = large, SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = At(inner, endDeg) });
        figure.Segments.Add(new ArcSegment { Point = At(inner, startDeg), Size = new Size(inner, inner), IsLargeArc = large, SweepDirection = SweepDirection.Counterclockwise });
        return new PathGeometry { Figures = { figure } };
    }
}

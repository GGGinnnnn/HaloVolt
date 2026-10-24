using System.Windows;
using System.Windows.Media;

namespace HaloVolt.Controls;

public sealed class BatteryRing : FrameworkElement
{
    public static readonly DependencyProperty OuterValueProperty = DependencyProperty.Register(
        nameof(OuterValue), typeof(double), typeof(BatteryRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceValue));

    public static readonly DependencyProperty InnerValueProperty = DependencyProperty.Register(
        nameof(InnerValue), typeof(double), typeof(BatteryRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceValue));

    public static readonly DependencyProperty OuterBrushProperty = DependencyProperty.Register(
        nameof(OuterBrush), typeof(Brush), typeof(BatteryRing),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InnerBrushProperty = DependencyProperty.Register(
        nameof(InnerBrush), typeof(Brush), typeof(BatteryRing),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(BatteryRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(BatteryRing),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SystemValueProperty = DependencyProperty.Register(
        nameof(SystemValue), typeof(double), typeof(BatteryRing),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceValue));

    public static readonly DependencyProperty SystemBrushProperty = DependencyProperty.Register(
        nameof(SystemBrush), typeof(Brush), typeof(BatteryRing),
        new FrameworkPropertyMetadata(Brushes.LightGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowSystemDotProperty = DependencyProperty.Register(
        nameof(ShowSystemDot), typeof(bool), typeof(BatteryRing),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public double OuterValue
    {
        get => (double)GetValue(OuterValueProperty);
        set => SetValue(OuterValueProperty, value);
    }

    public double InnerValue
    {
        get => (double)GetValue(InnerValueProperty);
        set => SetValue(InnerValueProperty, value);
    }

    public Brush OuterBrush
    {
        get => (Brush)GetValue(OuterBrushProperty);
        set => SetValue(OuterBrushProperty, value);
    }

    public Brush InnerBrush
    {
        get => (Brush)GetValue(InnerBrushProperty);
        set => SetValue(InnerBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double SystemValue
    {
        get => (double)GetValue(SystemValueProperty);
        set => SetValue(SystemValueProperty, value);
    }

    public Brush SystemBrush
    {
        get => (Brush)GetValue(SystemBrushProperty);
        set => SetValue(SystemBrushProperty, value);
    }

    public bool ShowSystemDot
    {
        get => (bool)GetValue(ShowSystemDotProperty);
        set => SetValue(ShowSystemDotProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 8 || height <= 8)
        {
            return;
        }

        var center = new Point(width / 2, height / 2);
        var maxRadius = Math.Min(width, height) / 2 - StrokeThickness / 2 - 2;
        if (maxRadius <= 4)
        {
            return;
        }

        var outerRadius = maxRadius;
        var innerRadius = Math.Max(6, outerRadius * 0.67);
        DrawTrack(drawingContext, center, outerRadius);
        DrawTrack(drawingContext, center, innerRadius);
        DrawValue(drawingContext, center, outerRadius, OuterValue, OuterBrush);
        DrawValue(drawingContext, center, innerRadius, InnerValue, InnerBrush);

        if (ShowSystemDot && !double.IsNaN(SystemValue))
        {
            var dotRadius = Math.Max(2.2, StrokeThickness * 0.32);
            drawingContext.DrawEllipse(SystemBrush, null, center, dotRadius, dotRadius);
        }
    }

    private void DrawTrack(DrawingContext dc, Point center, double radius)
    {
        var pen = new Pen(TrackBrush, Math.Max(1, StrokeThickness * 0.9));
        pen.Freeze();
        dc.DrawEllipse(null, pen, center, radius, radius);
    }

    private void DrawValue(DrawingContext dc, Point center, double radius, double value, Brush brush)
    {
        var clamped = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 100);
        var sweep = clamped * 3.6;
        if (sweep <= 0.01)
        {
            return;
        }

        if (sweep >= 359.5)
        {
            var fullPen = new Pen(brush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            fullPen.Freeze();
            dc.DrawEllipse(null, fullPen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, -90);
        var end = PointOnCircle(center, radius, -90 + sweep);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        var pen = new Pen(brush, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var value = (double)baseValue;
        return double.IsNaN(value) ? value : Math.Clamp(value, 0, 100);
    }
}

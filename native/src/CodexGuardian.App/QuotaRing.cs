using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace CodexGuardian.App;

public sealed class QuotaRing : FrameworkElement
{
    public static readonly DependencyProperty RiskColorProperty = DependencyProperty.Register(
        nameof(RiskColor), typeof(Color), typeof(QuotaRing),
        new FrameworkPropertyMetadata(Color.FromRgb(121, 185, 232), FrameworkPropertyMetadataOptions.AffectsRender));
    public Color RiskColor { get => (Color)GetValue(RiskColorProperty); set => SetValue(RiskColorProperty, value); }
    public static readonly DependencyProperty RemainingProperty = DependencyProperty.Register(
        nameof(Remaining), typeof(double), typeof(QuotaRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Remaining
    {
        get => (double)GetValue(RemainingProperty);
        set => SetValue(RemainingProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var track = new Pen(new SolidColorBrush(Color.FromRgb(48, 65, 89)), 7);
        var progress = new Pen(new SolidColorBrush(RiskColor), 7)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, track, center, radius, radius);
        var value = double.IsFinite(Remaining) ? Math.Clamp(Remaining, 0, 100) : 0;
        if (value <= 0 || radius <= 0) return;
        if (value >= 100)
        {
            drawingContext.DrawEllipse(null, progress, center, radius, radius);
            return;
        }
        var angle = value / 100 * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            context.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, progress, geometry);
    }
}

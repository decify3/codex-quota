using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using CodexGuardian.Core;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfPen = System.Windows.Media.Pen;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfCursors = System.Windows.Input.Cursors;

namespace CodexGuardian.App;

public sealed class TimelineSelectionChangedEventArgs(DateTimeOffset time, double remainingPercent) : EventArgs
{
    public DateTimeOffset Time { get; } = time;
    public double RemainingPercent { get; } = remainingPercent;
}

public sealed class QuotaTimelineControl : FrameworkElement
{
    private IReadOnlyList<TimelinePoint> _history = [];
    private IReadOnlyList<TimelinePoint> _forecast = [];
    private DateTimeOffset _resetAt;
    private DateTimeOffset? _selectedTime;
    private bool _isDragging;

    public int ReservePercent { get; set; } = 15;
    public double ZoomFactor { get; private set; } = 1;
    public event EventHandler<TimelineSelectionChangedEventArgs>? SelectedTimeChanged;

    public QuotaTimelineControl()
    {
        Focusable = true;
        Cursor = WpfCursors.Cross;
    }

    public void SetData(
        IReadOnlyList<TimelinePoint> history,
        IReadOnlyList<TimelinePoint> forecast,
        DateTimeOffset resetAt)
    {
        _history = history;
        _forecast = forecast;
        _resetAt = resetAt;
        if (_selectedTime is null || _selectedTime > resetAt) _selectedTime = null;
        InvalidateVisual();
    }

    public void SetZoom(double zoomFactor)
    {
        ZoomFactor = Math.Clamp(zoomFactor, 1, 6);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(MediaColor.FromRgb(17, 20, 27)),
            new WpfPen(new SolidColorBrush(MediaColor.FromRgb(41, 46, 58)), 1),
            area,
            12,
            12);

        if (ActualWidth < 120 || ActualHeight < 100) return;

        var plot = new Rect(44, 20, ActualWidth - 64, ActualHeight - 58);
        DrawGrid(drawingContext, plot);
        DrawReserve(drawingContext, plot);

        var all = _history.Concat(_forecast).OrderBy(point => point.Time).ToArray();
        if (all.Length == 0)
        {
            DrawLabel(drawingContext, Localization.Text("TimelineWaiting"), new WpfPoint(plot.Left + 12, plot.Top + 12), 12, MediaColor.FromRgb(140, 147, 163));
            return;
        }

        var start = all[0].Time;
        var fullEnd = _resetAt > start ? _resetAt : all[^1].Time;
        var end = start + TimeSpan.FromTicks((long)((fullEnd - start).Ticks / ZoomFactor));
        if (end <= start) end = start.AddMinutes(1);

        DrawSeries(drawingContext, _history.Where(point => point.Time <= end).ToArray(), plot, start, end, MediaColor.FromRgb(244, 247, 255), dashed: false);
        var visibleForecast = _forecast.Where(point => point.Time <= end).ToArray();
        DrawUncertainty(drawingContext, visibleForecast, plot, start, end);
        DrawSeries(drawingContext, visibleForecast, plot, start, end, MediaColor.FromRgb(124, 131, 255), dashed: true);
        DrawLabel(drawingContext, start.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), new WpfPoint(plot.Left, plot.Bottom + 9), 10, MediaColor.FromRgb(116, 123, 140));
        var endLabel = Localization.Text(
            ZoomFactor <= 1.01 ? "TimelineReset" : "TimelineTo",
            (ZoomFactor <= 1.01 ? _resetAt : end).ToLocalTime());
        DrawLabel(drawingContext, endLabel, new WpfPoint(plot.Right - 70, plot.Bottom + 9), 10, MediaColor.FromRgb(116, 123, 140));
        DrawSelection(drawingContext, plot, start, end);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _isDragging = true;
        SelectAt(e.GetPosition(this));
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed) SelectAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _isDragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (WpfKey.Left or WpfKey.Right)) return;
        var all = _history.Concat(_forecast).OrderBy(point => point.Time).ToArray();
        if (all.Length == 0) return;
        var start = all[0].Time;
        var end = start + TimeSpan.FromTicks((long)((_resetAt - start).Ticks / ZoomFactor));
        var current = _selectedTime ?? start;
        var delta = e.Key == WpfKey.Right ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(-15);
        SetSelection(current + delta, start, end);
        e.Handled = true;
    }

    private void SelectAt(WpfPoint point)
    {
        var all = _history.Concat(_forecast).OrderBy(item => item.Time).ToArray();
        if (all.Length == 0 || ActualWidth < 120 || ActualHeight < 100) return;
        var plot = new Rect(44, 20, ActualWidth - 64, ActualHeight - 58);
        var start = all[0].Time;
        var fullEnd = _resetAt > start ? _resetAt : all[^1].Time;
        var end = start + TimeSpan.FromTicks((long)((fullEnd - start).Ticks / ZoomFactor));
        var ratio = Math.Clamp((point.X - plot.Left) / plot.Width, 0, 1);
        SetSelection(start + TimeSpan.FromTicks((long)((end - start).Ticks * ratio)), start, end);
    }

    private void SetSelection(DateTimeOffset time, DateTimeOffset start, DateTimeOffset end)
    {
        _selectedTime = time < start ? start : time > end ? end : time;
        var remaining = InterpolateRemaining(_selectedTime.Value);
        SelectedTimeChanged?.Invoke(this, new(_selectedTime.Value, remaining));
        InvalidateVisual();
    }

    private double InterpolateRemaining(DateTimeOffset time)
    {
        var points = _history.Concat(_forecast).OrderBy(point => point.Time).ToArray();
        if (points.Length == 0) return 0;
        var before = points.LastOrDefault(point => point.Time <= time) ?? points[0];
        var after = points.FirstOrDefault(point => point.Time >= time) ?? points[^1];
        if (after.Time <= before.Time) return before.RemainingPercent;
        var ratio = (time - before.Time).TotalSeconds / (after.Time - before.Time).TotalSeconds;
        return before.RemainingPercent + (after.RemainingPercent - before.RemainingPercent) * ratio;
    }

    private void DrawSelection(DrawingContext context, Rect plot, DateTimeOffset start, DateTimeOffset end)
    {
        if (_selectedTime is not { } selected || selected < start || selected > end) return;
        var ratio = (selected - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds);
        var x = plot.Left + plot.Width * ratio;
        var pen = new WpfPen(new SolidColorBrush(MediaColor.FromRgb(184, 166, 255)), 1.5);
        context.DrawLine(pen, new WpfPoint(x, plot.Top), new WpfPoint(x, plot.Bottom));
    }

    private static void DrawGrid(DrawingContext context, Rect plot)
    {
        var gridPen = new WpfPen(new SolidColorBrush(MediaColor.FromRgb(39, 44, 56)), 1);
        foreach (var percent in new[] { 0, 25, 50, 75, 100 })
        {
            var y = plot.Bottom - plot.Height * percent / 100d;
            context.DrawLine(gridPen, new WpfPoint(plot.Left, y), new WpfPoint(plot.Right, y));
            DrawLabel(context, percent.ToString(System.Globalization.CultureInfo.InvariantCulture), new WpfPoint(10, y - 7), 9, MediaColor.FromRgb(116, 123, 140));
        }
    }

    private void DrawReserve(DrawingContext context, Rect plot)
    {
        var reserve = Math.Clamp(ReservePercent, 0, 100);
        var y = plot.Bottom - plot.Height * reserve / 100d;
        var pen = new WpfPen(new SolidColorBrush(MediaColor.FromRgb(184, 166, 255)), 1)
        {
            DashStyle = new DashStyle([2d, 3d], 0),
        };
        context.DrawLine(pen, new WpfPoint(plot.Left, y), new WpfPoint(plot.Right, y));
        DrawLabel(context, Localization.Text("TimelineReserve", reserve), new WpfPoint(plot.Right - 90, y - 15), 9, MediaColor.FromRgb(184, 166, 255));
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<TimelinePoint> points,
        Rect plot,
        DateTimeOffset start,
        DateTimeOffset end,
        MediaColor color,
        bool dashed)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var index = 0; index < points.Count; index++)
            {
                var point = ToPoint(points[index], plot, start, end);
                if (index == 0) stream.BeginFigure(point, false, false);
                else stream.LineTo(point, true, false);
            }
        }
        geometry.Freeze();

        var pen = new WpfPen(new SolidColorBrush(color), 2);
        if (dashed) pen.DashStyle = new DashStyle([4d, 4d], 0);
        pen.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawUncertainty(
        DrawingContext context,
        IReadOnlyList<TimelinePoint> points,
        Rect plot,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (points.Count < 2 || points.All(point => point.UncertaintyPercent <= 0)) return;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var index = 0; index < points.Count; index++)
            {
                var upper = points[index] with
                {
                    RemainingPercent = Math.Min(100, points[index].RemainingPercent + points[index].UncertaintyPercent),
                };
                var point = ToPoint(upper, plot, start, end);
                if (index == 0) stream.BeginFigure(point, true, true);
                else stream.LineTo(point, true, false);
            }
            for (var index = points.Count - 1; index >= 0; index--)
            {
                var lower = points[index] with
                {
                    RemainingPercent = Math.Max(0, points[index].RemainingPercent - points[index].UncertaintyPercent),
                };
                stream.LineTo(ToPoint(lower, plot, start, end), true, false);
            }
        }
        geometry.Freeze();
        context.DrawGeometry(new SolidColorBrush(MediaColor.FromArgb(45, 124, 131, 255)), null, geometry);
    }

    private static WpfPoint ToPoint(TimelinePoint point, Rect plot, DateTimeOffset start, DateTimeOffset end)
    {
        var total = Math.Max(1, (end - start).TotalSeconds);
        var x = plot.Left + plot.Width * Math.Clamp((point.Time - start).TotalSeconds / total, 0, 1);
        var y = plot.Bottom - plot.Height * Math.Clamp(point.RemainingPercent / 100d, 0, 1);
        return new(x, y);
    }

    private static void DrawLabel(DrawingContext context, string text, WpfPoint origin, double size, MediaColor color)
    {
        var label = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip);
        context.DrawText(label, origin);
    }
}

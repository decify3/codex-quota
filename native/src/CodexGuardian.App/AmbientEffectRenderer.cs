using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexGuardian.App;

internal sealed class AmbientEffectRenderer(Canvas canvas) : IDisposable
{
    private readonly Canvas _canvas = canvas;

    public void Apply(string effect, bool reduceMotion, bool isVisible)
    {
        Clear();
        if (reduceMotion || !isVisible || effect == "None" ||
            !SystemParameters.ClientAreaAnimation || (RenderCapability.Tier >> 16) == 0)
            return;

        _canvas.Visibility = Visibility.Visible;
        var width = Math.Max(_canvas.ActualWidth, 440);
        var height = Math.Max(_canvas.ActualHeight, 82);
        switch (effect)
        {
            case "Petals": AddPetals(width, height); break;
            case "Snow": AddSnow(width, height); break;
            case "Fireflies": AddFireflies(width, height); break;
            case "Starfall": AddStarfall(width, height); break;
        }
    }

    private void AddPetals(double width, double height)
    {
        for (var index = 0; index < 9; index++)
        {
            var petal = new ShapePath
            {
                Data = Geometry.Parse("M0,0 C4,-5 9,-3 8,2 C6,7 1,8 0,0Z"),
                Fill = new SolidColorBrush(MediaColor.FromArgb((byte)(80 + index * 8), 242, 151, 187)),
                Width = 9,
                Height = 9,
            };
            AddFallingParticle(petal, width, height, index, 9 + index % 4, 12, 100 + index * 31);
        }
    }

    private void AddSnow(double width, double height)
    {
        for (var index = 0; index < 14; index++)
        {
            var size = 2d + index % 4;
            var snow = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(MediaColor.FromArgb((byte)(95 + index * 5), 226, 242, 255)),
            };
            AddFallingParticle(snow, width, height, index, 11 + index % 5, 7, 0);
        }
    }

    private void AddFireflies(double width, double height)
    {
        for (var index = 0; index < 7; index++)
        {
            var glow = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(MediaColor.FromRgb(222, 242, 132)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 7,
                    ShadowDepth = 0,
                    Color = MediaColor.FromRgb(202, 242, 112),
                    Opacity = 0.65,
                },
            };
            Canvas.SetLeft(glow, 24 + (index * 67) % Math.Max(40, width - 48));
            Canvas.SetTop(glow, 14 + (index * 29) % Math.Max(30, height - 28));
            var transform = new TranslateTransform();
            glow.RenderTransform = transform;
            glow.BeginAnimation(UIElement.OpacityProperty, Repeat(0.15, 0.8, 2.4 + index * 0.23, index * 0.31, true));
            transform.BeginAnimation(TranslateTransform.XProperty, Repeat(-8, 10, 6 + index % 3, index * 0.4, true));
            transform.BeginAnimation(TranslateTransform.YProperty, Repeat(7, -9, 7 + index % 4, index * 0.35, true));
            _canvas.Children.Add(glow);
        }
    }

    private void AddStarfall(double width, double height)
    {
        for (var index = 0; index < 6; index++)
        {
            var star = new ShapeRectangle
            {
                Width = 18,
                Height = 1,
                RadiusX = 1,
                RadiusY = 1,
                Fill = new LinearGradientBrush(
                    MediaColor.FromArgb(0, 190, 213, 255),
                    MediaColor.FromArgb(145, 190, 213, 255),
                    0),
                RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection { new RotateTransform(-24), new TranslateTransform() },
                },
            };
            Canvas.SetLeft(star, 20 + index * width / 6);
            Canvas.SetTop(star, 8 + (index % 3) * 14);
            var translate = (TranslateTransform)((TransformGroup)star.RenderTransform).Children[1];
            translate.BeginAnimation(TranslateTransform.XProperty, Repeat(0, 95, 3.8 + index * 0.4, index * 0.8));
            translate.BeginAnimation(TranslateTransform.YProperty, Repeat(0, 45, 3.8 + index * 0.4, index * 0.8));
            star.BeginAnimation(UIElement.OpacityProperty, Repeat(0, 0.7, 1.9 + index * 0.2, index * 0.8, true));
            _canvas.Children.Add(star);
        }
    }

    private void AddFallingParticle(
        FrameworkElement particle,
        double width,
        double height,
        int index,
        double seconds,
        double drift,
        double rotation)
    {
        Canvas.SetLeft(particle, 12 + (index * 61) % Math.Max(30, width - 24));
        Canvas.SetTop(particle, -12);
        var transforms = new TransformGroup();
        var rotate = new RotateTransform();
        var translate = new TranslateTransform();
        transforms.Children.Add(rotate);
        transforms.Children.Add(translate);
        particle.RenderTransform = transforms;
        particle.Opacity = 0.55;
        translate.BeginAnimation(TranslateTransform.YProperty, Repeat(0, height + 24, seconds, index * 0.63));
        translate.BeginAnimation(TranslateTransform.XProperty, Repeat(-drift, drift, seconds / 2, index * 0.4, true));
        if (rotation > 0)
            rotate.BeginAnimation(RotateTransform.AngleProperty, Repeat(0, rotation, seconds, index * 0.63));
        _canvas.Children.Add(particle);
    }

    private static DoubleAnimation Repeat(
        double from,
        double to,
        double seconds,
        double delay,
        bool autoReverse = false) => new(from, to, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            AutoReverse = autoReverse,
            RepeatBehavior = RepeatBehavior.Forever,
        };

    private void Clear()
    {
        _canvas.Children.Clear();
        _canvas.Visibility = Visibility.Collapsed;
    }

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }
}

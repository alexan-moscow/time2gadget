using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Time2Gadget.Models;

namespace Time2Gadget.Controls;

/// <summary>
/// Эффект на строке виджета быстрых таймеров (докладка 2026-10-01): те же приёмы, что у главного таймера
/// (Views/MainWindow.xaml.cs → ApplyDialEffect), но по контуру одной строки — её размер меняется (ширина виджета по
/// содержимому), поэтому контуры пересчитываются при каждом изменении размера. Цвет — цвет эффекта таймера
/// (у радужных — палитра Models/RainbowPalette). Kind: Flash, Breathe, Strobe, Rainbow, Waves, Snake, RainbowSnake.
/// </summary>
public sealed class PanelRowEffect : Grid
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(PanelRowEffect), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty ColorHexProperty = DependencyProperty.Register(
        nameof(ColorHex), typeof(string), typeof(PanelRowEffect), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(PanelRowEffect), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty FastProperty = DependencyProperty.Register(
        nameof(Fast), typeof(bool), typeof(PanelRowEffect), new PropertyMetadata(false, OnChanged));

    public string? Kind { get => (string?)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? ColorHex { get => (string?)GetValue(ColorHexProperty); set => SetValue(ColorHexProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    /// <summary>Эффект окончания — быстрее, как сигнал (как у главного таймера).</summary>
    public bool Fast { get => (bool)GetValue(FastProperty); set => SetValue(FastProperty, value); }

    private const double Radius = 9;
    private readonly Border _overlay = new() { CornerRadius = new CornerRadius(Radius), Opacity = 0 };
    private readonly Rectangle _a = NewContour(), _b = NewContour();

    public PanelRowEffect()
    {
        IsHitTestVisible = false;
        Children.Add(_overlay);
        Children.Add(_a);
        Children.Add(_b);
        SizeChanged += (_, _) => Restart();
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => Restart();
    }

    private static Rectangle NewContour() => new()
    {
        RadiusX = Radius, RadiusY = Radius, Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, StrokeDashCap = PenLineCap.Round,
    };

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PanelRowEffect)d).Restart();

    private void Stop()
    {
        _overlay.BeginAnimation(OpacityProperty, null);
        _overlay.Opacity = 0;
        foreach (var r in new[] { _a, _b })
        {
            r.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            r.Visibility = Visibility.Collapsed;
            r.Effect = null;
        }
    }

    private void Restart()
    {
        Stop();
        if (!IsActive || !IsLoaded || ActualWidth < 8 || ActualHeight < 8 || string.IsNullOrEmpty(Kind)) return;
        var color = ParseColor(ColorHex);
        double k = Fast ? 0.6 : 1;
        switch (Kind)
        {
            case nameof(QuickPanelProgress.Flash):
                _overlay.Background = new SolidColorBrush(color);
                _overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(0.6 * k))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                break;
            case nameof(QuickPanelProgress.Breathe):
                _overlay.Background = new SolidColorBrush(color);
                _overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0.08, 0.32, TimeSpan.FromSeconds(1.8))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                break;
            case nameof(QuickPanelFinish.Strobe):
                _overlay.Background = new SolidColorBrush(color);
                var strobe = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                strobe.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                strobe.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.07))));
                strobe.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.16))));
                strobe.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
                _overlay.BeginAnimation(OpacityProperty, strobe);
                break;
            case nameof(QuickPanelFinish.Rainbow):
                var rainbow = new SolidColorBrush(Colors.Red);
                rainbow.BeginAnimation(SolidColorBrush.ColorProperty, RainbowAnimation(1));
                _overlay.Background = rainbow;
                _overlay.Opacity = 0.6;
                break;
            case nameof(QuickPanelFinish.Waves):
            {
                var brush = new SolidColorBrush(color);
                Contour(_a, 2.5);
                Contour(_b, 2.5);
                double len = Length(_a);
                RunDashLoop(_a, brush, 2, 0.09 * len, len / 260 * k, reverse: false);
                RunDashLoop(_b, brush, 1, 0.13 * len, len / 160 * k, reverse: true);
                break;
            }
            case nameof(QuickPanelFinish.Snake):
            case nameof(QuickPanelFinish.RainbowSnake):
            {
                bool colorful = Kind == nameof(QuickPanelFinish.RainbowSnake);
                var brush = new SolidColorBrush(color);
                if (colorful) brush.BeginAnimation(SolidColorBrush.ColorProperty, RainbowAnimation(2));
                Contour(_a, colorful ? 3 : 2.2);
                double len = Length(_a);
                _a.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = colorful ? Colors.White : color, BlurRadius = colorful ? 10 : 7, ShadowDepth = 0, Opacity = colorful ? 0.55 : 0.8,
                };
                RunDashLoop(_a, brush, 1, (colorful ? 0.2 : 0.14) * len, len / 280 * k, reverse: false);
                break;
            }
        }
    }

    private void Contour(Rectangle r, double thickness)
    {
        r.Width = ActualWidth;
        r.Height = ActualHeight;
        r.StrokeThickness = thickness;
    }

    /// <summary>Длина контура по средней линии штриха (как у главного таймера).</summary>
    private static double Length(Rectangle r)
    {
        double t = r.StrokeThickness, w = r.Width - t, h = r.Height - t;
        double rad = Math.Clamp(r.RadiusX - t / 2, 0, Math.Min(w, h) / 2);
        return 2 * (w + h) - 8 * rad + 2 * Math.PI * rad;
    }

    /// <summary>count штрихов длиной dashPx бегут по контуру; один круг за seconds.</summary>
    private static void RunDashLoop(Rectangle shape, Brush stroke, int count, double dashPx, double seconds, bool reverse)
    {
        double t = shape.StrokeThickness;
        double length = Length(shape) / t;
        double period = length / count, dash = Math.Min(dashPx / t, period * 0.85);
        var dashes = new DoubleCollection();
        for (int i = 0; i < count; i++) { dashes.Add(dash); dashes.Add(period - dash); }
        shape.StrokeDashArray = dashes;
        shape.Stroke = stroke;
        shape.Visibility = Visibility.Visible;
        shape.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, reverse ? -length : length, TimeSpan.FromSeconds(Math.Max(0.6, seconds))) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static ColorAnimationUsingKeyFrames RainbowAnimation(double speed)
    {
        var a = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        for (int i = 0; i <= RainbowPalette.Colors.Length; i++)
        {
            var (r, g, b) = RainbowPalette.Colors[i % RainbowPalette.Colors.Length];
            a.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(r, g, b), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * RainbowPalette.StepSeconds / speed))));
        }
        return a;
    }

    private static Color ParseColor(string? hex)
    {
        try { return hex is null ? Color.FromRgb(0x3D, 0x8B, 0xFF) : (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Color.FromRgb(0x3D, 0x8B, 0xFF); }
    }
}

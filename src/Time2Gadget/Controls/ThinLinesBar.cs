using System.Windows;
using System.Windows.Media;

namespace Time2Gadget.Controls;

/// <summary>
/// Эффект хода «тонкие линии под строкой» (докладка 2026-10-01): вертикальные полоски шириной 2 и с промежутками 2 на всю
/// ширину строки, высота 6 (вдвое выше «делений»); пройденная доля — цветом таймера, остальные — приглушённые.
/// </summary>
public sealed class ThinLinesBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ThinLinesBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorHexProperty = DependencyProperty.Register(
        nameof(ColorHex), typeof(string), typeof(ThinLinesBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public string? ColorHex { get => (string?)GetValue(ColorHexProperty); set => SetValue(ColorHexProperty, value); }

    private const double LineWidth = 2, Gap = 2, LineHeight = 6;

    public ThinLinesBar()
    {
        Height = LineHeight;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        if (width < LineWidth) return;
        var lit = new SolidColorBrush(ParseColor(ColorHex));
        var dim = (Brush)FindResource("Brush.BorderSubtle");
        double litTo = Math.Clamp(Fraction, 0, 1) * width;
        for (double x = 0; x + LineWidth <= width + 0.01; x += LineWidth + Gap)
            dc.DrawRectangle(x < litTo ? lit : dim, null, new Rect(x, 0, LineWidth, LineHeight));
    }

    private static Color ParseColor(string? hex)
    {
        try { return hex is null ? Color.FromRgb(0x3D, 0x8B, 0xFF) : (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Color.FromRgb(0x3D, 0x8B, 0xFF); }
    }
}

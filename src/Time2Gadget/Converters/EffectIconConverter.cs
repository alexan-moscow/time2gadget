using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Time2Gadget.Models;

namespace Time2Gadget.Converters;

/// <summary>
/// Мини-иконка эффекта завершения (быстрые таймеры, 2026-09-28): «—» нет эффекта, двойной круг — пульсация,
/// молния — строб, радужный круг — радужная волна. Каждый вызов — новый элемент (иконка стоит в нескольких местах).
/// </summary>
public sealed class EffectIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var finish = (Brush)Application.Current.FindResource("Brush.Finish");
        var secondary = (Brush)Application.Current.FindResource("Brush.TextSecondary");
        var path = new Path { Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 1.3 };
        switch (value)
        {
            case FinishVisualEffect.Pulse:
                path.Data = Geometry.Parse("M6,3.6 A2.4,2.4 0 1 1 5.99,3.6 Z M6,0.8 A5.2,5.2 0 1 1 5.99,0.8 Z");
                path.Stroke = finish;
                break;
            case FinishVisualEffect.Flash:
                path.Data = Geometry.Parse("M7,0.5 L2.5,7 L5.5,7 L4.5,11.5 L9.5,4.5 L6.5,4.5 Z");
                path.Fill = finish;
                break;
            case FinishVisualEffect.ColorCycle:
                path.Data = Geometry.Parse("M6,0.8 A5.2,5.2 0 1 1 5.99,0.8 Z");
                var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                for (int i = 0; i < RainbowPalette.Colors.Length; i++)
                {
                    var (r, g, b) = RainbowPalette.Colors[i];
                    rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / (double)(RainbowPalette.Colors.Length - 1)));
                }
                path.Fill = rainbow;
                break;
            default: // нет эффекта — прочерк
                path.Data = Geometry.Parse("M1.5,6 L10.5,6");
                path.Stretch = Stretch.None;
                path.Stroke = secondary;
                path.StrokeThickness = 1.6;
                break;
        }
        return path;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Time2Gadget.Models;

namespace Time2Gadget.Controls;

/// <summary>
/// Превью «картинка на мониторе» (докладка 2026-10-01): монитор в своих пропорциях, картинка лежит так, как ляжет на экран —
/// растянуть / по размеру / заполнить / по центру в исходном размере; поля и монитор без картинки — сплошным цветом;
/// ничего не задано — надпись (по умолчанию «не выбрано»). Для плиток мониторов статичной заставки и окна выбора картинки.
/// </summary>
public sealed class MonitorPreview : Border
{
    public static readonly DependencyProperty ImagePathProperty = Register(nameof(ImagePath), typeof(string), null);
    public static readonly DependencyProperty FitProperty = Register(nameof(Fit), typeof(WallpaperFit), WallpaperFit.Stretch);
    public static readonly DependencyProperty ColorHexProperty = Register(nameof(ColorHex), typeof(string), null);
    public static readonly DependencyProperty MonitorWidthProperty = Register(nameof(MonitorWidth), typeof(double), 1920.0);
    public static readonly DependencyProperty MonitorHeightProperty = Register(nameof(MonitorHeight), typeof(double), 1080.0);
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), typeof(string), "не выбрано");

    public string? ImagePath { get => (string?)GetValue(ImagePathProperty); set => SetValue(ImagePathProperty, value); }
    public WallpaperFit Fit { get => (WallpaperFit)GetValue(FitProperty); set => SetValue(FitProperty, value); }
    public string? ColorHex { get => (string?)GetValue(ColorHexProperty); set => SetValue(ColorHexProperty, value); }
    public double MonitorWidth { get => (double)GetValue(MonitorWidthProperty); set => SetValue(MonitorWidthProperty, value); }
    public double MonitorHeight { get => (double)GetValue(MonitorHeightProperty); set => SetValue(MonitorHeightProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(MonitorPreview), new PropertyMetadata(defaultValue, (d, _) => ((MonitorPreview)d).Rebuild()));

    private static readonly Dictionary<string, (BitmapSource? Thumb, Size Native)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public MonitorPreview()
    {
        ClipToBounds = true;
        Rebuild();
    }

    private void Rebuild()
    {
        double w = Math.Max(1, MonitorWidth), h = Math.Max(1, MonitorHeight);
        var screen = new Grid { Width = w, Height = h, ClipToBounds = true };
        var color = ParseBrush(ColorHex);
        if (ImagePath is { Length: > 0 } path && Load(path) is { Thumb: { } thumb } info)
        {
            screen.Background = color ?? Brushes.Black;
            var image = new Image { Source = thumb, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            switch (Fit)
            {
                case WallpaperFit.Fit: image.Stretch = Stretch.Uniform; break;
                case WallpaperFit.Fill: image.Stretch = Stretch.UniformToFill; break;
                case WallpaperFit.Center:
                    image.Stretch = Stretch.Fill;
                    image.Width = info.Native.Width;     // исходный размер картинки в пикселях монитора
                    image.Height = info.Native.Height;
                    break;
                default: image.Stretch = Stretch.Fill; break;
            }
            screen.Children.Add(image);
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = screen };
        }
        else if (color is not null)
        {
            screen.Background = color;
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = screen };
        }
        else
        {
            Child = new TextBlock
            {
                Text = EmptyText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Foreground = TryFindResource("Brush.TextSecondary") as Brush ?? Brushes.Gray,
            };
        }
    }

    private static Brush? ParseBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
        catch { return null; }
    }

    /// <summary>Уменьшенная копия (файл не держится) и исходный размер картинки — из заголовка файла.</summary>
    private static (BitmapSource? Thumb, Size Native) Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        (BitmapSource?, Size) result = (null, default);
        try
        {
            using (var stream = System.IO.File.OpenRead(path))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                result.Item2 = new Size(frame.PixelWidth, frame.PixelHeight);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 480;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            result.Item1 = bitmap;
        }
        catch { /* битый файл — без превью */ }
        return Cache[path] = result;
    }

    /// <summary>Картинку удалили — превью больше не нужно.</summary>
    public static void Forget(string path) => Cache.Remove(path);
}

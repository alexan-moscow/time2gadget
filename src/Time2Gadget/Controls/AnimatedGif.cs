using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Time2Gadget.Controls;

/// <summary>
/// Проигрывание анимированного GIF в обычном Image (докладка 2026-09-28, аватарка в «Об авторе»): WPF показывает у GIF
/// только первый кадр. Кадры GIF бывают частичными (смещение /imgdesc/Left,Top) и рисуются поверх предыдущих
/// (disposal «не стирать») — поэтому каждый кадр собирается накоплением на холсте полного размера, а смена кадров —
/// ObjectAnimationUsingKeyFrames по свойству Source с задержками из файла (/grctlext/Delay, сотые доли секунды).
/// </summary>
public static class AnimatedGif
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(AnimatedGif), new PropertyMetadata(null, OnSourceChanged));

    public static string? GetSource(DependencyObject d) => (string?)d.GetValue(SourceProperty);
    public static void SetSource(DependencyObject d, string? value) => d.SetValue(SourceProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image || e.NewValue is not string uri) return;
        try
        {
            var decoder = new GifBitmapDecoder(new Uri(uri, UriKind.RelativeOrAbsolute),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var first = decoder.Frames[0];
            int width = first.PixelWidth, height = first.PixelHeight;

            var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var time = TimeSpan.Zero;
            BitmapSource? previous = null;
            foreach (var frame in decoder.Frames)
            {
                var (left, top, delay) = ReadFrameInfo(frame);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    if (previous is not null) dc.DrawImage(previous, new Rect(0, 0, width, height));
                    dc.DrawImage(frame, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                }
                var composed = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                composed.Render(visual);
                composed.Freeze();
                animation.KeyFrames.Add(new DiscreteObjectKeyFrame(composed, KeyTime.FromTimeSpan(time)));
                time += delay;
                previous = composed;
            }
            animation.Duration = time; // последний кадр держится свою задержку, потом — снова первый

            image.Source = (ImageSource)animation.KeyFrames[0].Value;
            if (animation.KeyFrames.Count > 1) image.BeginAnimation(Image.SourceProperty, animation);
        }
        catch
        {
            // Нет файла/не GIF — просто без картинки, окно настроек не роняем.
        }
    }

    private static (double Left, double Top, TimeSpan Delay) ReadFrameInfo(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata meta) return (0, 0, TimeSpan.FromMilliseconds(100));
        double left = Convert.ToDouble(meta.GetQuery("/imgdesc/Left") ?? 0);
        double top = Convert.ToDouble(meta.GetQuery("/imgdesc/Top") ?? 0);
        int delay = Convert.ToInt32(meta.GetQuery("/grctlext/Delay") ?? 10);
        return (left, top, TimeSpan.FromMilliseconds(Math.Max(delay, 2) * 10)); // задержка 0–1 в файле — не быстрее 20 мс
    }
}

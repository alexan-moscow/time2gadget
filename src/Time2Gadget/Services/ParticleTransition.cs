using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Time2Gadget.Services;

/// <summary>
/// Эффекты смены подложки из частиц (докладка 2026-10-01) — без шейдеров и графических движков: старая картинка режется на
/// кусочки (у каждого — своя кисть с участком картинки), слой рисует их поверх новой картинки, пока смена идёт, и убирается.
/// Рисование — только во время смены (CompositionTarget.Rendering), дальше подложка снова ничего не делает.
/// Пиксельное растворение — квадраты исчезают в случайном порядке; дисперсия — крупные куски сдувает волной слева направо
/// с вращением; распад — мелкие частицы уносит вверх-вправо по дуге, они уменьшаются и гаснут (как пепел).
/// </summary>
internal sealed class ParticleTransition : FrameworkElement
{
    public enum Kind { Pixels, Dispersion, Disintegration }

    private struct Particle
    {
        public Rect Rect;          // место на экране (в единицах WPF)
        public Brush Brush;        // участок старой картинки
        public double Delay, Life; // когда начинает двигаться и сколько живёт (доли общей длительности)
        public double Dx, Dy, Spin, Curve;
    }

    private readonly Particle[] _particles;
    private readonly Kind _kind;
    private readonly TimeSpan _duration;
    private readonly Stopwatch _clock = new();
    private readonly Action _done;
    private bool _running;

    public ParticleTransition(BitmapSource oldImage, double width, double height, Kind kind, Action done)
    {
        _kind = kind;
        _done = done;
        IsHitTestVisible = false;
        _duration = kind switch
        {
            Kind.Pixels => TimeSpan.FromMilliseconds(900),
            Kind.Dispersion => TimeSpan.FromMilliseconds(1300),
            _ => TimeSpan.FromMilliseconds(1500),
        };
        int columns = kind switch { Kind.Pixels => 64, Kind.Dispersion => 40, _ => 72 }; // распад: 72 — вдвое легче 96, мелкость на глаз та же
        int rows = Math.Max(1, (int)Math.Round(columns * height / width));
        var random = new Random();
        double cw = width / columns, ch = height / rows;
        double pw = oldImage.PixelWidth / (double)columns, ph = oldImage.PixelHeight / (double)rows;
        _particles = new Particle[columns * rows];
        int i = 0;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            var brush = new ImageBrush(oldImage)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(x * pw, y * ph, pw, ph),
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            double across = x / (double)columns;
            var p = new Particle { Rect = new Rect(x * cw, y * ch, cw + 0.5, ch + 0.5), Brush = brush }; // +0.5 — без щелей между кусочками
            switch (kind)
            {
                case Kind.Pixels:
                    p.Delay = random.NextDouble() * 0.85;
                    p.Life = 0.12;
                    break;
                case Kind.Dispersion:
                    p.Delay = across * 0.45 + random.NextDouble() * 0.15;
                    p.Life = 0.45;
                    p.Dx = width * (0.15 + random.NextDouble() * 0.25);
                    p.Dy = height * (random.NextDouble() - 0.5) * 0.25;
                    p.Spin = (random.NextDouble() - 0.5) * 240;
                    break;
                default: // распад
                    p.Delay = across * 0.5 + random.NextDouble() * 0.25;
                    p.Life = 0.35 + random.NextDouble() * 0.1;
                    p.Dx = width * (0.04 + random.NextDouble() * 0.12);
                    p.Dy = -height * (0.05 + random.NextDouble() * 0.15);
                    p.Curve = (random.NextDouble() - 0.5) * height * 0.08;
                    break;
            }
            _particles[i++] = p;
        }
    }

    public void Start()
    {
        _running = true;
        _clock.Start();
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>Остановить сразу (новая смена пришла раньше, окно закрывается).</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
        _done();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_clock.Elapsed >= _duration) { Stop(); return; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds);
        foreach (var p in _particles)
        {
            double k = (t - p.Delay) / p.Life; // 0 — ещё на месте, 1 — исчез
            if (k >= 1) continue;
            if (k <= 0) { dc.DrawRectangle(p.Brush, null, p.Rect); continue; }
            switch (_kind)
            {
                case Kind.Pixels:
                    dc.PushOpacity(1 - k);
                    dc.DrawRectangle(p.Brush, null, p.Rect);
                    dc.Pop();
                    break;
                case Kind.Dispersion:
                {
                    double e = k * k; // разгон
                    var cx = p.Rect.X + p.Rect.Width / 2 + p.Dx * e;
                    var cy = p.Rect.Y + p.Rect.Height / 2 + p.Dy * e;
                    var m = Matrix.Identity;
                    m.RotateAt(p.Spin * k, p.Rect.X + p.Rect.Width / 2, p.Rect.Y + p.Rect.Height / 2);
                    m.Translate(cx - (p.Rect.X + p.Rect.Width / 2), cy - (p.Rect.Y + p.Rect.Height / 2));
                    dc.PushTransform(new MatrixTransform(m));
                    dc.PushOpacity(1 - k);
                    dc.DrawRectangle(p.Brush, null, p.Rect);
                    dc.Pop();
                    dc.Pop();
                    break;
                }
                default: // распад: вверх-вправо по дуге, уменьшается и гаснет
                {
                    double e = k * k;
                    double scale = 1 - 0.7 * k;
                    double cx = p.Rect.X + p.Rect.Width / 2 + p.Dx * e + p.Curve * Math.Sin(k * Math.PI);
                    double cy = p.Rect.Y + p.Rect.Height / 2 + p.Dy * e;
                    var r = new Rect(cx - p.Rect.Width * scale / 2, cy - p.Rect.Height * scale / 2, p.Rect.Width * scale, p.Rect.Height * scale);
                    dc.PushOpacity(1 - k);
                    dc.DrawRectangle(p.Brush, null, r);
                    dc.Pop();
                    break;
                }
            }
        }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Time2Gadget.Services;

/// <summary>
/// Эффекты смены подложки из частиц (докладка 2026-10-01) — без шейдеров и графических движков: старая картинка режется на
/// кусочки (у каждого — своя кисть с участком картинки), слой рисует их поверх новой картинки, пока смена идёт, и убирается.
/// Пиксельное растворение — квадраты исчезают в случайном порядке; дисперсия — крупные куски сдувает волной слева направо
/// с вращением; распад — мелкие частицы уносит вверх-вправо по дуге, они уменьшаются и гаснут (как пепел).
/// Экономия (замер 2026-10-01: распад на двух 4K-мониторах — 39% ядра при смене каждые 3 с, почти всё — отрисовка частиц):
/// (1) у дисперсии и распада волна идёт слева направо — всё, до чего она ещё не дошла, рисуется одним куском, а не частицами;
/// (2) не больше 30 кадров в секунду; (3) частицы берут участки из уменьшенной копии картинки (не шире 1600 точек) —
/// движущиеся мелкие кусочки на глаз не отличить, а текстура в разы меньше.
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

    private static readonly TimeSpan FrameStep = TimeSpan.FromMilliseconds(1000.0 / 30);

    private readonly Particle[][] _columns; // частицы по столбцам — для волны слева направо
    private readonly Kind _kind;
    private readonly TimeSpan _duration;
    private readonly Stopwatch _clock = new();
    private readonly Action _done;
    private readonly BitmapSource _source;  // уменьшенная старая картинка
    private readonly double _width, _height, _sweep;
    private TimeSpan _lastFrame = -FrameStep; // первый кадр — сразу (не MinValue: вычитание из него переполняется)
    private bool _running;

    public ParticleTransition(BitmapSource oldImage, double width, double height, Kind kind, Action done)
    {
        _kind = kind;
        _done = done;
        _width = width;
        _height = height;
        IsHitTestVisible = false;
        _duration = kind switch
        {
            Kind.Pixels => TimeSpan.FromMilliseconds(900),
            Kind.Dispersion => TimeSpan.FromMilliseconds(1300),
            _ => TimeSpan.FromMilliseconds(1500),
        };
        // Доля длительности, за которую волна проходит экран: столбец x начинает двигаться не раньше чем x/столбцов × _sweep.
        _sweep = kind switch { Kind.Dispersion => 0.45, Kind.Disintegration => 0.5, _ => 0 };

        double scale = Math.Min(1, 1600.0 / oldImage.PixelWidth);
        _source = scale < 1 ? new TransformedBitmap(oldImage, new ScaleTransform(scale, scale)) : oldImage;
        if (_source.CanFreeze) _source.Freeze();

        int columns = kind switch { Kind.Pixels => 64, Kind.Dispersion => 40, _ => 72 };
        int rows = Math.Max(1, (int)Math.Round(columns * height / width));
        var random = new Random();
        double cw = width / columns, ch = height / rows;
        double pw = _source.PixelWidth / (double)columns, ph = _source.PixelHeight / (double)rows;
        _columns = new Particle[columns][];
        for (int x = 0; x < columns; x++)
        {
            var column = new Particle[rows];
            double across = x / (double)columns;
            for (int y = 0; y < rows; y++)
            {
                var brush = new ImageBrush(_source)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(x * pw, y * ph, pw, ph),
                    Stretch = Stretch.Fill,
                };
                brush.Freeze();
                var p = new Particle { Rect = new Rect(x * cw, y * ch, cw + 0.5, ch + 0.5), Brush = brush }; // +0.5 — без щелей
                switch (kind)
                {
                    case Kind.Pixels:
                        p.Delay = random.NextDouble() * 0.85;
                        p.Life = 0.12;
                        break;
                    case Kind.Dispersion:
                        p.Delay = across * _sweep + random.NextDouble() * 0.15;
                        p.Life = 0.45;
                        p.Dx = width * (0.15 + random.NextDouble() * 0.25);
                        p.Dy = height * (random.NextDouble() - 0.5) * 0.25;
                        p.Spin = (random.NextDouble() - 0.5) * 240;
                        break;
                    default: // распад
                        p.Delay = across * _sweep + random.NextDouble() * 0.25;
                        p.Life = 0.35 + random.NextDouble() * 0.1;
                        p.Dx = width * (0.04 + random.NextDouble() * 0.12);
                        p.Dy = -height * (0.05 + random.NextDouble() * 0.15);
                        p.Curve = (random.NextDouble() - 0.5) * height * 0.08;
                        break;
                }
                column[y] = p;
            }
            _columns[x] = column;
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
        try
        {
            var now = _clock.Elapsed;
            if (now >= _duration) { Stop(); return; }
            if (now - _lastFrame < FrameStep) return; // не больше 30 кадров в секунду
            _lastFrame = now;
            InvalidateVisual();
        }
        catch { Stop(); } // эффект не должен ронять программу — смена просто доводится
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { Draw(dc); }
        catch { Dispatcher.BeginInvoke(Stop); } // эффект не должен ронять программу — смена просто доводится
    }

    private void Draw(DrawingContext dc)
    {
        double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds);
        int columns = _columns.Length;

        // Волна: столбцы, до которых она не дошла, — одним куском старой картинки.
        int untouched = columns;
        if (_sweep > 0)
        {
            untouched = Math.Clamp((int)Math.Ceiling(t / _sweep * columns), 0, columns); // столбцы правее ещё не тронулись
            if (untouched < columns)
            {
                double x0 = untouched * _width / columns;
                double sx = _source.PixelWidth * untouched / (double)columns;
                var rest = new ImageBrush(_source)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(sx, 0, _source.PixelWidth - sx, _source.PixelHeight),
                    Stretch = Stretch.Fill,
                };
                rest.Freeze();
                dc.DrawRectangle(rest, null, new Rect(x0, 0, _width - x0, _height));
            }
        }

        for (int x = 0; x < untouched; x++)
        foreach (var p in _columns[x])
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
                    double ox = p.Rect.X + p.Rect.Width / 2, oy = p.Rect.Y + p.Rect.Height / 2;
                    var m = Matrix.Identity;
                    m.RotateAt(p.Spin * k, ox, oy);
                    m.Translate(p.Dx * e, p.Dy * e);
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

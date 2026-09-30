using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>
/// Подложка слайдшоу (докладка 2026-10-01, основной способ показа): своё окно за значками рабочего стола — как у Wallpaper Engine
/// и Lively. Встраивается в окно WorkerW, которое Проводник создаёт между фоном и значками (сообщение 0x052C окну Progman;
/// в Windows 11 24H2+ WorkerW — внутри Progman). На каждый монитор — одно дочернее окно с двумя картинками: нынешней и новой;
/// эффект смены — короткая анимация между ними (≈0,7 с). Экономно: картинки уже готовы под размер монитора (кэш
/// WallpaperService), рисование — только во время смены, в остальное время окно ничего не делает. Проводник перезапустился
/// (WorkerW пропал) — <see cref="IsHostAlive"/> = false, следующий показ встраивается заново. Не получилось встроиться —
/// <see cref="Show"/> возвращает false, и слайдшоу показывается средствами Windows.
/// Если ядер больше одного, окно каждого монитора — в своём потоке (загрузка картинки и эффект там же): смены на мониторах
/// идут одновременно на разных ядрах.
/// </summary>
public sealed class WallpaperUnderlay : IDisposable
{
    private static readonly Duration TransitionTime = new(TimeSpan.FromMilliseconds(700));

    private sealed class Surface
    {
        public required HwndSource Source;
        public required Grid Root;
        public required Image Back;   // что показано сейчас
        public required Image Front;  // что проявляется
        public required System.Drawing.Rectangle Bounds;
        public required System.Windows.Threading.Dispatcher Dispatcher; // поток окна монитора
        public bool OwnThread;        // окно в своём потоке (есть свободные ядра)
        public string? File;
        public Action? Finish;        // завершить идущую смену (только в потоке окна)
    }

    /// <summary>
    /// Каждому монитору — свой поток, если ядер больше одного (решение пользователя 2026-10-01): смены на мониторах идут
    /// одновременно, Windows ставит потоки на разные ядра. Одно ядро — все окна в основном потоке, как раньше.
    /// </summary>
    private static readonly bool UseThreads = Environment.ProcessorCount > 1;

    private readonly Dictionary<string, Surface> _surfaces = new();
    private IntPtr _host;

    /// <summary>Подложка встроена и окно Проводника, в которое она встроена, живо.</summary>
    public bool IsHostAlive => _host != IntPtr.Zero && IsWindow(_host) && _surfaces.Count > 0;

    /// <summary>Показать картинки на мониторах с эффектом. false — встроиться не удалось (показывать средствами Windows).</summary>
    public bool Show(IReadOnlyList<(WallpaperMonitor Monitor, string File)> frames, SlideshowEffect effect)
    {
        try
        {
            if (!EnsureHost()) return false;
            // мониторы поменялись — окна заново
            if (_surfaces.Count != frames.Count || frames.Any(f => !_surfaces.TryGetValue(f.Monitor.Id, out var s) || s.Bounds != f.Monitor.Bounds))
                DisposeSurfaces();
            foreach (var (monitor, file) in frames)
            {
                if (!_surfaces.TryGetValue(monitor.Id, out var surface))
                {
                    surface = CreateSurface(monitor);
                    _surfaces[monitor.Id] = surface;
                }
                if (surface.File == file) continue;
                var target = surface;
                var fx = surface.File is null ? SlideshowEffect.Instant : effect;
                surface.File = file;
                // загрузка картинки и смена — в потоке окна этого монитора (у каждого монитора свой)
                target.Dispatcher.BeginInvoke(() =>
                {
                    try { if (Load(file) is { } image) Transition(target, image, fx); }
                    catch { /* смена не должна ронять программу */ }
                });
            }
            return true;
        }
        catch
        {
            Hide();
            return false;
        }
    }

    /// <summary>Убрать подложку (слайдшоу выключено, способ — Windows, выход из программы).</summary>
    public void Hide()
    {
        DisposeSurfaces();
        _host = IntPtr.Zero;
    }

    public void Dispose() => Hide();

    private bool EnsureHost()
    {
        if (_host != IntPtr.Zero && IsWindow(_host)) return true;
        DisposeSurfaces();
        _host = FindWorkerW();
        return _host != IntPtr.Zero;
    }

    private Surface CreateSurface(WallpaperMonitor monitor)
    {
        GetWindowRect(_host, out var host);
        int x = monitor.Bounds.Left - host.Left, y = monitor.Bounds.Top - host.Top;
        var parent = _host;
        if (!UseThreads) return BuildSurface(parent, monitor, x, y);

        Surface? made = null;
        Exception? error = null;
        var ready = new ManualResetEventSlim(); // не using: поток может отметиться уже после ожидания
        var thread = new Thread(() =>
        {
            try { made = BuildSurface(parent, monitor, x, y); }
            catch (Exception e) { error = e; }
            ready.Set();
            if (made is null) return;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => e.Handled = true; // эффект не роняет программу
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true, Name = $"Подложка — монитор {monitor.Number}" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (made is null) throw error ?? new TimeoutException("Окно подложки не создалось");
        made.OwnThread = true;
        return made;
    }

    private static Surface BuildSurface(IntPtr parent, WallpaperMonitor monitor, int x, int y)
    {
        var parameters = new HwndSourceParameters("Time2GadgetUnderlay")
        {
            ParentWindow = parent,
            WindowStyle = WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            PositionX = x, PositionY = y, Width = monitor.Bounds.Width, Height = monitor.Bounds.Height,
        };
        var source = new HwndSource(parameters);
        var back = new Image { Stretch = Stretch.Fill };
        var front = new Image { Stretch = Stretch.Fill, Opacity = 0 };
        var root = new Grid { Background = Brushes.Black, ClipToBounds = true };
        root.Children.Add(back);
        root.Children.Add(front);
        source.RootVisual = root;
        // размер и место — в физических пикселях (окно-родитель чужого процесса, масштаб WPF здесь не помогает)
        SetWindowPos(source.Handle, IntPtr.Zero, x, y, monitor.Bounds.Width, monitor.Bounds.Height, SwpNoZOrder | SwpNoActivate);
        return new Surface
        {
            Source = source, Root = root, Back = back, Front = front, Bounds = monitor.Bounds,
            Dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher,
        };
    }

    private static BitmapSource? Load(string file)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // прочитать и отпустить файл (кэш могут убирать)
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static void Transition(Surface s, BitmapSource image, SlideshowEffect effect)
    {
        s.Finish?.Invoke(); // предыдущая смена ещё идёт — довести мгновенно
        if (effect == SlideshowEffect.Instant)
        {
            s.Back.Source = image;
            return;
        }

        double width = s.Root.ActualWidth > 0 ? s.Root.ActualWidth : s.Bounds.Width;
        double height = s.Root.ActualHeight > 0 ? s.Root.ActualHeight : s.Bounds.Height;

        // Эффекты из частиц: новая картинка сразу под низом, старая рассыпается слоем поверх (ParticleTransition).
        if (effect is SlideshowEffect.Pixels or SlideshowEffect.Dispersion or SlideshowEffect.Disintegration)
        {
            if (s.Back.Source is not BitmapSource old) { s.Back.Source = image; return; }
            var kind = effect switch
            {
                SlideshowEffect.Pixels => ParticleTransition.Kind.Pixels,
                SlideshowEffect.Dispersion => ParticleTransition.Kind.Dispersion,
                _ => ParticleTransition.Kind.Disintegration,
            };
            s.Back.Source = image;
            ParticleTransition? layer = null;
            layer = new ParticleTransition(old, width, height, kind, () =>
            {
                s.Root.Children.Remove(layer);
                s.Finish = null;
            });
            s.Root.Children.Add(layer);
            s.Finish = layer.Stop;
            layer.Start();
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var front = s.Front;
        front.Source = image;
        front.Opacity = 1;
        AnimationTimeline main;
        DependencyObject target;
        DependencyProperty property;

        switch (effect)
        {
            case SlideshowEffect.Slide:
                var inMove = new TranslateTransform(width, 0);
                var outMove = new TranslateTransform(0, 0);
                front.RenderTransform = inMove;
                s.Back.RenderTransform = outMove;
                outMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -width, TransitionTime) { EasingFunction = ease });
                (main, target, property) = (new DoubleAnimation(width, 0, TransitionTime) { EasingFunction = ease }, inMove, TranslateTransform.XProperty);
                break;
            case SlideshowEffect.Curtain:
                var clip = new RectangleGeometry(new Rect(0, 0, width, 0));
                front.Clip = clip;
                (main, target, property) = (new RectAnimation(new Rect(0, 0, width, 0), new Rect(0, 0, width, height), TransitionTime) { EasingFunction = ease }, clip, RectangleGeometry.RectProperty);
                break;
            case SlideshowEffect.Zoom:
                var scale = new ScaleTransform(1.08, 1.08);
                front.RenderTransformOrigin = new Point(0.5, 0.5);
                front.RenderTransform = scale;
                front.Opacity = 0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1, TransitionTime) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, 1, TransitionTime) { EasingFunction = ease });
                (main, target, property) = (new DoubleAnimation(0, 1, TransitionTime) { EasingFunction = ease }, front, UIElement.OpacityProperty);
                break;
            default: // Fade
                front.Opacity = 0;
                (main, target, property) = (new DoubleAnimation(0, 1, TransitionTime) { EasingFunction = ease }, front, UIElement.OpacityProperty);
                break;
        }

        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            s.Finish = null;
            s.Back.Source = image;          // новая — теперь «нынешняя»
            s.Back.RenderTransform = null;
            front.BeginAnimation(UIElement.OpacityProperty, null);
            front.Opacity = 0;
            front.Source = null;
            front.Clip = null;
            front.RenderTransform = null;
        }
        s.Finish = Finish;
        main.Completed += (_, _) => Finish();
        ((IAnimatable)target).BeginAnimation(property, main);
    }

    private void DisposeSurfaces()
    {
        foreach (var s in _surfaces.Values)
        {
            try
            {
                // окно закрывается в своём потоке; поток монитора потом завершается
                s.Dispatcher.Invoke(() => { s.Finish?.Invoke(); s.Source.Dispose(); },
                    System.Windows.Threading.DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            catch { /* окно уже уничтожено вместе с Проводником */ }
            if (s.OwnThread) s.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
        }
        _surfaces.Clear();
    }

    /// <summary>Окно Проводника между фоном и значками (WorkerW); 0 — не нашлось.</summary>
    private static IntPtr FindWorkerW()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        // Windows 11 24H2 и новее: WorkerW — дочернее окно Progman.
        var inner = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (inner != IntPtr.Zero) return inner;

        // Windows 10/11 до 24H2: WorkerW — следующее окно верхнего уровня после окна со значками (SHELLDLL_DefView).
        IntPtr worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        return worker;
    }

    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000, WsClipChildren = 0x02000000;
    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

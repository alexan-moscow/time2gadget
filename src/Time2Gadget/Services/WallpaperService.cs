using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>Монитор для фона: путь устройства (IDesktopWallpaper), место и размер в пикселях, номер как в Windows/окне профилей.</summary>
public sealed record WallpaperMonitor(string Id, Rectangle Bounds, int Number);

/// <summary>Что поставить на монитор: картинку в режиме <see cref="Fit"/> (поля — цветом <see cref="Fill"/>) или, без картинки/«не отображать», сплошной <see cref="Fill"/> (нет — чёрный).</summary>
/// <remarks><see cref="Area"/> — общий прямоугольник всех мониторов (одна картинка на все): картинка ложится в него, монитор берёт свой кусок.</remarks>
public sealed record MonitorWallpaperPlan(WallpaperMonitor Monitor, string? Image, WallpaperFit Fit, Color? Fill, Rectangle? Area = null);

/// <summary>
/// Фон рабочего стола (докладка 2026-09-29/30).
/// (1) «Закрепить фоны»: разные картинки на разных мониторах пользователь получает слайд-шоу Windows, а у него интервал
/// не больше суток и паузы нет. Закрепление: каждому монитору ставится его текущая картинка слайд-шоу обычным фоном
/// (IDesktopWallpaper.SetWallpaper по монитору) — слайд-шоу выключается само (Windows стирает slideshow.ini), держится
/// это без Тайм2гаджета и переживает гашение мониторов. Откуда шло слайд-шоу запоминается, чтобы вернуть его.
/// (2) Своя картинка на все мониторы, у каждого монитора свой режим (растянуть/по размеру/заполнить/по центру).
/// У Windows режим один на все мониторы, поэтому картинка для каждого монитора готовится здесь точно под его
/// разрешение и ставится «растянуть» (при точном размере — пиксель в пиксель).
/// Вызывать из потока интерфейса (STA).
/// </summary>
public static class WallpaperService
{
    private const int DssSlideshow = 0x2;
    private const int PositionStretch = 2;

    /// <summary>Сейчас идёт слайд-шоу Windows.</summary>
    public static bool IsSlideshow()
    {
        try { return (Create().GetStatus() & DssSlideshow) != 0; }
        catch { return false; }
    }

    /// <summary>Остановить слайд-шоу, оставив на каждом мониторе его текущую картинку. Возвращает, откуда шло слайд-шоу, или null.</summary>
    public static WallpaperSlideshowBackup? Pin()
    {
        try
        {
            var w = Create();
            if ((w.GetStatus() & DssSlideshow) == 0) return null;
            var backup = ReadSlideshow(w);

            var current = new List<(string Id, string Image)>();
            foreach (var id in ActiveMonitorIds(w))
                if (w.GetWallpaper(id) is { Length: > 0 } image) current.Add((id, image));
            if (current.Count == 0) return null;
            foreach (var (id, image) in current) w.SetWallpaper(id, image);
            return backup;
        }
        catch { return null; }
    }

    /// <summary>Вернуть слайд-шоу, как было до закрепления. false — не удалось (папки нет и т.п.).</summary>
    public static bool Restore(WallpaperSlideshowBackup backup)
    {
        var pidls = new List<IntPtr>();
        try
        {
            foreach (var path in backup.Paths)
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0) pidls.Add(pidl);
            if (pidls.Count == 0) return false;
            if (SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out var items) != 0) return false;
            var w = Create();
            w.SetSlideshow(items);
            w.SetSlideshowOptions(backup.Options, backup.Tick);
            return true;
        }
        catch { return false; }
        finally { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); }
    }

    // ---------------- Своя картинка ----------------

    /// <summary>Подключённые мониторы по номерам (номер — как в Windows и в окне «Размер и положение окон программ»).</summary>
    public static List<WallpaperMonitor> Monitors()
    {
        var list = new List<WallpaperMonitor>();
        try
        {
            var w = Create();
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var id in ActiveMonitorIds(w))
            {
                w.GetMonitorRECT(id, out var r);
                var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                int index = Array.FindIndex(screens, s => s.DeviceName == DeviceNameAt(bounds));
                list.Add(new WallpaperMonitor(id, bounds, index >= 0 ? index + 1 : 0));
            }
            // Номер не нашёлся — по порядку после найденных.
            int next = list.Count == 0 ? 1 : list.Max(m => m.Number) + 1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Number == 0) list[i] = list[i] with { Number = next++ };
        }
        catch { /* нет доступа к фону — пустой список */ }
        return list.OrderBy(m => m.Number).ToList();
    }

    /// <summary>Папка своих картинок: у портативной копии — рядом с программой, у установленной — в %APPDATA% (как свои звуки).</summary>
    public static string ImagesDir
    {
        get
        {
            var appDir = Path.Combine(AppContext.BaseDirectory, "Wallpapers");
            if (!SoundService.IsInstalledByVelopack())
            {
                try { Directory.CreateDirectory(appDir); return appDir; }
                catch { /* нельзя писать рядом с программой */ }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "Wallpapers");
        }
    }

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };

    /// <summary>Картинки в папке программы, по имени.</summary>
    public static List<string> ListImages()
    {
        try
        {
            var dir = ImagesDir;
            if (!Directory.Exists(dir)) return new();
            return Directory.GetFiles(dir)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Сколько картинок может быть в папке программы (решение пользователя, 2026-09-30: 500).</summary>
    public const int LibraryLimit = 500;

    /// <summary>Сколько картинок можно добавить за раз (выбором файлов) и сколько шагов у слайдшоу.</summary>
    public const int BatchLimit = 30;

    /// <summary>
    /// Скопировать выбранные картинки в папку программы, не больше <see cref="LibraryLimit"/> всего. Возвращает пути копий
    /// (тот же файл, что уже есть, — берётся он; одноимённый другой — «имя (2)») и сколько не влезло.
    /// </summary>
    public static (List<string> Imported, int Skipped) ImportImages(IEnumerable<string> sources)
    {
        var result = new List<string>();
        int skipped = 0;
        var dir = ImagesDir;
        try { Directory.CreateDirectory(dir); } catch { return (result, sources.Count()); }
        int count = ListImages().Count;
        foreach (var source in sources)
        {
            try
            {
                var dest = Path.Combine(dir, Path.GetFileName(source));
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(dest);
                    continue;
                }
                dest = SoundService.FreeName(dest, source);
                if (File.Exists(dest)) { result.Add(dest); continue; } // та же картинка уже есть
                if (count >= LibraryLimit) { skipped++; continue; }
                File.Copy(source, dest);
                count++;
                result.Add(dest);
            }
            catch { skipped++; }
        }
        return (result, skipped);
    }

    /// <summary>Удалить картинку из папки программы безвозвратно.</summary>
    public static bool DeleteImage(string path)
    {
        try
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(ImagesDir), StringComparison.OrdinalIgnoreCase))
                return false; // только свои копии
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }
    /// <summary>Как фон выглядит сейчас — чтобы потом вернуть (слайд-шоу — вместе с его папкой).</summary>
    public static WallpaperBackupState? Capture()
    {
        try
        {
            var w = Create();
            var state = new WallpaperBackupState { Position = w.GetPosition() };
            if ((w.GetStatus() & DssSlideshow) != 0) state.Slideshow = ReadSlideshow(w);
            foreach (var id in ActiveMonitorIds(w))
                if (w.GetWallpaper(id) is { Length: > 0 } image && File.Exists(image)) state.Images[id] = image;
            return state;
        }
        catch { return null; }
    }

    /// <summary>Вернуть фон, как был: слайд-шоу — из его папки, иначе картинки по мониторам и прежний режим.</summary>
    public static void RestoreState(WallpaperBackupState state)
    {
        try
        {
            if (state.Slideshow is { } slideshow && Restore(slideshow)) return;
            var w = Create();
            foreach (var (id, image) in state.Images)
                if (File.Exists(image)) w.SetWallpaper(id, image);
            w.SetPosition(state.Position);
            CleanupGenerated(keep: Array.Empty<string>());
        }
        catch { /* фон не трогаем */ }
    }

    /// <summary>
    /// Поставить фон по плану для каждого монитора: картинка в своём режиме, сплошной цвет или прежний фон монитора.
    /// Картинки готовятся под разрешение каждого монитора (<see cref="PrepareFiles"/>), старые готовые убираются.
    /// </summary>
    public static bool Apply(IReadOnlyList<MonitorWallpaperPlan> plans)
    {
        try
        {
            if (plans.Count == 0) return false;
            var w = Create();
            var made = PrepareFiles(plans, ColorFromBgr(w.GetBackgroundColor()));
            if (made.Count == 0) return false;
            w.SetPosition(PositionStretch);
            foreach (var (m, file) in made) w.SetWallpaper(m.Id, file);
            CleanupGenerated(keep: made.Select(x => x.File).ToArray());
            return true;
        }
        catch { return false; }
    }

    /// <summary>Цвет фона Windows (поля у «по размеру»/«по центру»).</summary>
    public static Color WindowsBackground()
    {
        try { return ColorFromBgr(Create().GetBackgroundColor()); }
        catch { return Color.Black; }
    }

    /// <summary>
    /// Готовые картинки для мониторов (без установки фона) — для подложки слайдшоу (Services/WallpaperUnderlay) и для Apply.
    /// Готовая картинка — по ключу (файл и его дата, размер монитора, режим, цвет): слайдшоу по кругу не рисует её заново
    /// (докладка 2026-10-01: смена раз в секунду не успевала — каждая смена рисовала и сжимала 4K-картинки).
    /// </summary>
    public static List<(WallpaperMonitor Monitor, string File)> PrepareFiles(IReadOnlyList<MonitorWallpaperPlan> plans, Color windowsBackground)
    {
        var dir = Path.Combine(ImagesDir, "Generated");
        Directory.CreateDirectory(dir);
        var made = new List<(WallpaperMonitor, string)>();
        var cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var plan in plans)
            {
                var m = plan.Monitor;
                var fill = plan.Fill ?? windowsBackground;
                bool picture = plan.Image is { } p && File.Exists(p) && plan.Fit != WallpaperFit.None;
                var key = picture
                    ? $"{plan.Image}|{File.GetLastWriteTimeUtc(plan.Image!).Ticks}|{m.Bounds.Width}x{m.Bounds.Height}|{plan.Fit}|{fill.ToArgb()}" +
                      (plan.Area is { } a ? $"|span{a.X},{a.Y},{a.Width},{a.Height}|at{m.Bounds.X},{m.Bounds.Y}" : "")
                    : $"solid|{m.Bounds.Width}x{m.Bounds.Height}|{(plan.Fill ?? Color.Black).ToArgb()}";
                var file = Path.Combine(dir, $"mon{m.Number}-{StableHash(key)}{(picture ? ".jpg" : ".png")}");
                if (File.Exists(file)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow); // свежая — не удалится при уборке
                else
                {
                    using var bitmap = picture
                        ? plan.Area is { } area
                            ? RenderSpan(cache.TryGetValue(plan.Image!, out var spanImg) ? spanImg : cache[plan.Image!] = LoadImage(plan.Image!), m.Bounds, area, plan.Fit, fill)
                            : Render(cache.TryGetValue(plan.Image!, out var img) ? img : cache[plan.Image!] = LoadImage(plan.Image!), m.Bounds.Width, m.Bounds.Height, plan.Fit, fill)
                        : Solid(m.Bounds.Width, m.Bounds.Height, plan.Fill ?? Color.Black);
                    if (picture) SaveJpeg(bitmap, file, 95); else bitmap.Save(file, ImageFormat.Png);
                }
                made.Add((m, file));
            }
        }
        finally { foreach (var img in cache.Values) img.Dispose(); }
        return made;
    }

    /// <summary>Уборка готовых картинок после показа подложкой (стоящие сейчас — не трогать).</summary>
    public static void CleanupGeneratedExcept(IEnumerable<string> keep) => CleanupGenerated(keep.ToArray());
    private static string StableHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>JPEG с заданным качеством: в разы быстрее PNG для 4K-картинок, на глаз без потерь при 95.</summary>
    private static void SaveJpeg(Bitmap bitmap, string file, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bitmap.Save(file, codec, parameters);
    }

    private static Bitmap Solid(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
        return bitmap;
    }

    /// <summary>Режим, которым Windows показывала прежний фон (DESKTOP_WALLPAPER_POSITION) — чтобы нарисовать его так же.</summary>
    public static WallpaperFit FitFromPosition(int position) => position switch
    {
        0 => WallpaperFit.Center,
        3 => WallpaperFit.Fit,
        4 or 5 => WallpaperFit.Fill, // «заполнить» и «расширение» (на все мониторы) — ближе всего «заполнить»
        1 => WallpaperFit.Center,    // «замостить» — не поддерживаем, по центру
        _ => WallpaperFit.Stretch,
    };

    public static Color ParseColor(string? hex)
    {
        try { return hex is { Length: > 0 } ? ColorTranslator.FromHtml(hex) : Color.Black; }
        catch { return Color.Black; }
    }
    /// <summary>Картинка под монитор ширины×высоты в режиме <paramref name="fit"/>; поля — цветом фона Windows.</summary>
    public static Bitmap Render(Image source, int width, int height, WallpaperFit fit, Color background)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(background);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        double sw = source.Width, sh = source.Height;
        double scale = fit switch
        {
            WallpaperFit.Fit => Math.Min(width / sw, height / sh),
            WallpaperFit.Fill => Math.Max(width / sw, height / sh),
            _ => 1,
        };
        RectangleF dest = fit == WallpaperFit.Stretch
            ? new RectangleF(0, 0, width, height)
            : new RectangleF((float)((width - sw * scale) / 2), (float)((height - sh * scale) / 2), (float)(sw * scale), (float)(sh * scale));
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // без светлой каймы по краям при масштабировании
        g.DrawImage(source, Rectangle.Round(dest), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    /// <summary>
    /// Кусок одной картинки на все мониторы: картинка ложится в общий прямоугольник <paramref name="area"/> (координаты экрана в
    /// пикселях) в режиме <paramref name="fit"/>, монитор получает то, что попало на него.
    /// </summary>
    public static Bitmap RenderSpan(Image source, Rectangle monitor, Rectangle area, WallpaperFit fit, Color background)
    {
        var bitmap = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(background);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        double sw = source.Width, sh = source.Height;
        double scale = fit == WallpaperFit.Fit ? Math.Min(area.Width / sw, area.Height / sh) : Math.Max(area.Width / sw, area.Height / sh);
        RectangleF dest = fit == WallpaperFit.Stretch
            ? new RectangleF(area.X, area.Y, area.Width, area.Height)
            : new RectangleF((float)(area.X + (area.Width - sw * scale) / 2), (float)(area.Y + (area.Height - sh * scale) / 2), (float)(sw * scale), (float)(sh * scale));
        dest.Offset(-monitor.X, -monitor.Y);
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(source, Rectangle.Round(dest), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    /// <summary>Картинка с учётом поворота из EXIF (снимки с телефона), файл не держится открытым.</summary>
    private static Image LoadImage(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        var image = Image.FromStream(stream);
        var copy = new Bitmap(image);
        const int OrientationId = 0x0112;
        if (Array.IndexOf(image.PropertyIdList, OrientationId) >= 0)
        {
            var flip = image.GetPropertyItem(OrientationId)?.Value?[0] switch
            {
                3 => RotateFlipType.Rotate180FlipNone,
                6 => RotateFlipType.Rotate90FlipNone,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            };
            copy.RotateFlip(flip);
        }
        image.Dispose();
        return copy;
    }

    /// <summary>
    /// Уборка готовых картинок: <paramref name="keep"/> (стоят сейчас) и ещё до <see cref="GeneratedCacheSize"/> последних
    /// использованных остаются — это кэш слайдшоу; остальные удаляются. Пустой <paramref name="keep"/> (фон вернули) — удалить все.
    /// </summary>
    private static void CleanupGenerated(string[] keep)
    {
        try
        {
            var dir = Path.Combine(ImagesDir, "Generated");
            if (!Directory.Exists(dir)) return;
            var old = Directory.GetFiles(dir)
                .Where(f => !keep.Contains(f, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep.Length == 0 ? 0 : GeneratedCacheSize);
            foreach (var f in old)
                try { File.Delete(f); } catch { /* занят — удалится в следующий раз */ }
        }
        catch { }
    }

    /// <summary>Сколько готовых картинок держать про запас: 30 шагов × до 4 мониторов.</summary>
    private const int GeneratedCacheSize = 120;

    private static Color ColorFromBgr(uint bgr) => Color.FromArgb((int)(bgr & 0xFF), (int)((bgr >> 8) & 0xFF), (int)((bgr >> 16) & 0xFF));

    /// <summary>Мониторы, подключённые сейчас (Windows помнит и отключённые — у них нет места на экране).</summary>
    private static List<string> ActiveMonitorIds(IDesktopWallpaper w)
    {
        var ids = new List<string>();
        uint count = w.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            try
            {
                string id = w.GetMonitorDevicePathAt(i);
                w.GetMonitorRECT(id, out var r);
                if (r.Right > r.Left && r.Bottom > r.Top) ids.Add(id);
            }
            catch { /* монитор отключён */ }
        }
        return ids;
    }

    /// <summary>Имя устройства («\\.\DISPLAY1») монитора, на который приходится центр прямоугольника.</summary>
    private static string? DeviceNameAt(Rectangle bounds)
    {
        var hmon = MonitorFromPoint(new PointStruct { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }, 2);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return hmon != IntPtr.Zero && GetMonitorInfo(hmon, ref info) ? info.DeviceName : null;
    }

    private static WallpaperSlideshowBackup ReadSlideshow(IDesktopWallpaper w)
    {
        w.GetSlideshowOptions(out int options, out uint tick);
        return new WallpaperSlideshowBackup { Options = options, Tick = tick, Paths = SlideshowPaths(w) };
    }

    private static List<string> SlideshowPaths(IDesktopWallpaper w)
    {
        var paths = new List<string>();
        var items = w.GetSlideshow();
        uint n = items.GetCount();
        for (uint i = 0; i < n; i++)
        {
            try
            {
                var item = items.GetItemAt(i);
                string path = item.GetDisplayName(SigdnFileSysPath);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            catch { /* элемент не в файловой системе — пропускаем */ }
        }
        return paths;
    }

    private static IDesktopWallpaper Create() =>
        (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"))!)!;

    private const uint SigdnFileSysPath = 0x80058000;

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Rect displayRect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IShellItemArray items);
        IShellItemArray GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        uint GetCount();
        IShellItem GetItemAt(uint index);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        IShellItem GetParent();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetDisplayName(uint sigdnName);
        uint GetAttributes(uint sfgaoMask);
        int Compare(IShellItem psi, uint hint);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointStruct { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(PointStruct pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfoEx info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray items);
}

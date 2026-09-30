using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>Монитор для фона: путь устройства (IDesktopWallpaper), место и размер в пикселях, номер как в Windows/окне профилей.</summary>
public sealed record WallpaperMonitor(string Id, Rectangle Bounds, int Number);

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

    /// <summary>Скопировать выбранные картинки в папку программы; возвращает пути копий (одноимённый другой файл — «имя (2)»).</summary>
    public static List<string> ImportImages(IEnumerable<string> sources)
    {
        var result = new List<string>();
        var dir = ImagesDir;
        try { Directory.CreateDirectory(dir); } catch { return result; }
        foreach (var source in sources)
        {
            try
            {
                var dest = Path.Combine(dir, Path.GetFileName(source));
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                {
                    dest = SoundService.FreeName(dest, source);
                    File.Copy(source, dest, overwrite: true);
                }
                result.Add(dest);
            }
            catch { /* файл занят/нет прав — пропускаем */ }
        }
        return result;
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
    /// Поставить картинку на все мониторы, у каждого — свой режим. Для каждого монитора картинка готовится под его
    /// разрешение (папка Generated, новые имена — иначе Windows может не перечитать файл), старые готовые удаляются.
    /// </summary>
    public static bool Apply(string imagePath, IReadOnlyList<WallpaperMonitor> monitors, Func<string, WallpaperFit> fitFor)
    {
        try
        {
            if (!File.Exists(imagePath) || monitors.Count == 0) return false;
            var w = Create();
            var background = ColorFromBgr(w.GetBackgroundColor());
            var dir = Path.Combine(ImagesDir, "Generated");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var made = new List<(string Id, string File)>();
            using (var source = LoadImage(imagePath))
            {
                foreach (var m in monitors)
                {
                    var file = Path.Combine(dir, $"mon{m.Number}-{stamp}.png");
                    using var bitmap = Render(source, m.Bounds.Width, m.Bounds.Height, fitFor(m.Id), background);
                    bitmap.Save(file, ImageFormat.Png);
                    made.Add((m.Id, file));
                }
            }
            w.SetPosition(PositionStretch);
            foreach (var (id, file) in made) w.SetWallpaper(id, file);
            CleanupGenerated(keep: made.Select(x => x.File).ToArray());
            return true;
        }
        catch { return false; }
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

    private static void CleanupGenerated(string[] keep)
    {
        try
        {
            var dir = Path.Combine(ImagesDir, "Generated");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
                if (!keep.Contains(f, StringComparer.OrdinalIgnoreCase))
                    try { File.Delete(f); } catch { /* занят — удалится в следующий раз */ }
        }
        catch { }
    }

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

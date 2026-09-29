using System.Runtime.InteropServices;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>
/// «Закрепить фоны рабочего стола» (докладка 2026-09-29). Разные картинки на разных мониторах пользователь получает слайд-шоу
/// Windows, а у него интервал не больше суток и паузы нет — картинки уезжают. Закрепление: каждому монитору ставится его
/// текущая картинка слайд-шоу обычным фоном (IDesktopWallpaper.SetWallpaper по монитору) — слайд-шоу выключается само,
/// и держится это без Тайм2гаджета. Откуда шло слайд-шоу (папки, интервал, порядок) запоминается в настройках, чтобы вернуть его,
/// когда закрепление снимут. Проверено: в режиме слайд-шоу GetWallpaper(монитор) отдаёт текущую картинку именно этого монитора.
/// Вызывать из потока интерфейса (STA).
/// </summary>
public static class WallpaperService
{
    private const int DssSlideshow = 0x2;

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
            w.GetSlideshowOptions(out int options, out uint tick);
            var backup = new WallpaperSlideshowBackup { Options = options, Tick = tick, Paths = SlideshowPaths(w) };

            uint count = w.GetMonitorDevicePathCount();
            var current = new List<(string Id, string Image)>();
            for (uint i = 0; i < count; i++)
            {
                string id = w.GetMonitorDevicePathAt(i);
                if (w.GetWallpaper(id) is { Length: > 0 } image) current.Add((id, image));
            }
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray items);
}

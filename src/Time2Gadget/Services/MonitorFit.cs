using System.Runtime.InteropServices;

namespace Time2Gadget.Services;

/// <summary>
/// Окно на стыке мониторов с разным масштабом (докладка 2026-10-25): при каждой установке места Windows пересчитывает его размер
/// под масштаб — окно растёт шаг за шагом (Telegram после каждого гашения мониторов: левый край 2051 → 1685 → 1392 → 1158 → 971).
/// Такое место переносится целиком на монитор, где окно лежит больше всего (и уменьшается до его рабочей области); окна на одном
/// мониторе или на мониторах одного масштаба не трогаются. Координаты — пиксели экрана.
/// </summary>
internal static class MonitorFit
{
    public static (int Left, int Top, int Right, int Bottom) FitIfMixedDpi(int left, int top, int right, int bottom)
    {
        var monitors = new List<(RECT Work, RECT Area, uint Dpi)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr data) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref info))
            {
                uint dpi = GetDpiForMonitor(m, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 ? dx : 96;
                monitors.Add((info.rcWork, info.rcMonitor, dpi));
            }
            return true;
        }, IntPtr.Zero);

        var touched = monitors.Where(m => Overlap(m.Area, left, top, right, bottom) > 0).ToList();
        if (touched.Count < 2 || touched.Select(m => m.Dpi).Distinct().Count() < 2) return (left, top, right, bottom);

        var main = touched.OrderByDescending(m => Overlap(m.Area, left, top, right, bottom)).First().Work;
        int w = Math.Min(right - left, main.Right - main.Left), h = Math.Min(bottom - top, main.Bottom - main.Top);
        int x = Math.Clamp(left, main.Left, main.Right - w), y = Math.Clamp(top, main.Top, main.Bottom - h);
        return (x, y, x + w, y + h);
    }

    private static long Overlap(RECT a, int left, int top, int right, int bottom)
    {
        long w = Math.Min(a.Right, right) - Math.Max(a.Left, left), h = Math.Min(a.Bottom, bottom) - Math.Max(a.Top, top);
        return w > 0 && h > 0 ? w * h : 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}

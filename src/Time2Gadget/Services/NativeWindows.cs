using System.Runtime.InteropServices;
using System.Text;

namespace Time2Gadget.Services;

/// <summary>Окно другой программы для списка в «Профилях размера окон».</summary>
public sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, string? ExePath, uint ProcessId)
{
    public string ExeName => ExePath is null ? "?" : System.IO.Path.GetFileName(ExePath);
    /// <summary>Ключ программы «exe + вид окна» — тот же, что у памяти окон и назначений профилей.</summary>
    public string? ProgramKey => NativeWindows.MakeProgramKey(ExePath, ClassName);
}

/// <summary>Положение и размер окна в пикселях экрана + есть ли у окна рамка/заголовок.</summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height, bool Borderless);

/// <summary>
/// Работа с окнами других программ (докладка 2026-09-28/29): список окон, программа окна, снятие рамки, размер и
/// положение, сигнал «размер изменён» — то же, что делает Simple Runtime Window Editor, но из Тайм2гаджета.
/// Координаты — физические пиксели экрана (программа PerMonitorV2, как и SetWindowPos).
/// </summary>
public static class NativeWindows
{
    /// <summary>Окна верхнего уровня. allWindows = false — только «обычные»: видимые, с заголовком, без владельца, не служебные.</summary>
    public static List<WindowInfo> EnumerateWindows(bool allWindows)
    {
        var list = new List<WindowInfo>();
        uint ownPid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid || IsCloaked(hwnd)) return true;
            string title = GetTitle(hwnd), cls = GetClassName(hwnd);
            if (!allWindows)
            {
                if (title.Length == 0 || GetWindow(hwnd, GwOwner) != IntPtr.Zero) return true;
                if ((GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return true;
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            }
            list.Add(new WindowInfo(hwnd, title, cls, ProcessPath(pid), pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Окно верхнего уровня, к которому относится hwnd (активное окно бывает дочерним у «хитрых» игр/движков).</summary>
    public static WindowInfo? Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        var root = GetAncestor(hwnd, GaRoot);
        if (root != IntPtr.Zero) hwnd = root;
        GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowInfo(hwnd, GetTitle(hwnd), GetClassName(hwnd), ProcessPath(pid), pid);
    }

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    public static WindowBounds? GetBounds(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        return new WindowBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (style & WsCaption) != WsCaption && (style & WsThickFrame) == 0);
    }

    /// <summary>
    /// Применить положение/размер: развёрнутое окно сначала «восстанавливается», рамка снимается (если borderless),
    /// затем SetWindowPos с FRAMECHANGED. notifyResized — послать WM_EXITSIZEMOVE: игры (Elite и др.) перестраивают картинку
    /// только по сигналу «перетаскивание закончено», которого при программной смене размера не бывает.
    /// </summary>
    public static bool Apply(IntPtr hwnd, WindowBounds bounds, bool notifyResized)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        if (bounds.Borderless) RemoveBorder(hwnd);
        bool ok = SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoOwnerZOrder);
        if (ok && notifyResized) PostMessage(hwnd, WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
        return ok;
    }

    /// <summary>Убрать заголовок и рамку (как «Remove borders» в SRWE): WS_CAPTION, WS_THICKFRAME, кнопки; края-рамки из exstyle.</summary>
    private static void RemoveBorder(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        long newStyle = style & ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        if (newStyle != style) SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(newStyle));
        long ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        long newEx = ex & ~(WsExDlgModalFrame | WsExClientEdge | WsExStaticEdge | WsExWindowEdge);
        if (newEx != ex) SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(newEx));
    }

    // ---------------- Мониторы ----------------

    /// <summary>Мониторы: границы и рабочая область (без панели задач), физические пиксели.</summary>
    public static IReadOnlyList<(System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea, bool Primary)> Monitors() =>
        System.Windows.Forms.Screen.AllScreens.Select(s => (s.Bounds, s.WorkingArea, s.Primary)).ToList();

    /// <summary>Номер монитора (в порядке Monitors()), где центр окна.</summary>
    public static int MonitorIndexOf(WindowBounds b)
    {
        var center = new System.Drawing.Point(b.X + b.Width / 2, b.Y + b.Height / 2);
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = System.Windows.Forms.Screen.FromPoint(center);
        return Math.Max(0, Array.IndexOf(screens, screens.FirstOrDefault(s => s.DeviceName == screen.DeviceName)));
    }

    // ---------------- Программа окна ----------------

    public static string? MakeProgramKey(string? exePath, string className) =>
        exePath is null ? null : $"{exePath.ToLowerInvariant()}|{StableClassName(className)}";

    /// <summary>
    /// Имя класса без частей, меняющихся от запуска к запуску: WPF — «HwndWrapper[Программа;;GUID]», WinForms —
    /// «WindowsForms10.Window.8.app.0.хэш_r..._ad1». Иначе память/профиль никогда не совпали бы с новым окном.
    /// </summary>
    public static string StableClassName(string className) =>
        className.StartsWith("HwndWrapper[", StringComparison.Ordinal) ? "HwndWrapper"
        : className.StartsWith("WindowsForms10.", StringComparison.Ordinal) ? "WindowsForms10"
        : className;

    public static string? ProcessPath(uint pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string GetTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len == 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    // ---------------- WinAPI ----------------

    private const int GwOwner = 4, GwlStyle = -16, GwlExStyle = -20, DwmwaCloaked = 14, SwRestore = 9;
    private const uint GaRoot = 2, ProcessQueryLimitedInformation = 0x1000, WmExitSizeMove = 0x0232;
    private const long WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsSysMenu = 0x00080000,
        WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000;
    private const long WsExToolWindow = 0x80, WsExDlgModalFrame = 0x1, WsExClientEdge = 0x200, WsExStaticEdge = 0x20000, WsExWindowEdge = 0x100;
    private const uint SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20, SwpNoOwnerZOrder = 0x200;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}

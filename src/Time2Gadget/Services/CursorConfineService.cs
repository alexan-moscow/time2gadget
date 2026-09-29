using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Time2Gadget.Services;

/// <summary>
/// «Не выпускать указатель мыши из окна» (докладка 2026-09-29): пока отмеченное окно активно, указатель ограничен его
/// границами; переключились на другое окно — свободен. Две защиты: (1) ClipCursor, повторяемый 4 раза в секунду (игры сами
/// сбрасывают его при Alt+Tab и смене режима); (2) низкоуровневый хук мыши — каждое движение проверяется до того, как
/// курсор сдвинется, и уводящее за край ставит курсор на край (найдено на Elite: при резком рывке вниз указатель успевал
/// выскочить на панель задач между повторами ClipCursor). Хук стоит, только пока ограничение действует.
/// Глобальная клавиша (AppSettings.CursorConfineKey) временно выключает/включает ограничение (<see cref="Toggle"/>).
/// </summary>
public sealed class CursorConfineService : IDisposable
{
    private readonly HashSet<IntPtr> _windows = new();
    private readonly DispatcherTimer _timer;
    private readonly LowLevelMouseProc _hookProc; // держим ссылку — иначе делегат соберёт GC, а хук останется
    private IntPtr _hook;
    private RECT _rect;
    private bool _clipping;

    public CursorConfineService()
    {
        _hookProc = MouseHook;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Update();
    }

    /// <summary>Ограничение выключено клавишей (до следующего нажатия).</summary>
    public bool Paused { get; private set; }

    /// <summary>Отметить окно (confine = true) или снять отметку.</summary>
    public void Set(IntPtr hwnd, bool confine)
    {
        if (confine) _windows.Add(hwnd); else _windows.Remove(hwnd);
        if (_windows.Count > 0) _timer.Start();
        Update();
    }

    public bool IsConfined(IntPtr hwnd) => _windows.Contains(hwnd);

    /// <summary>Клавиша: выключить/включить ограничение. Возвращает true — ограничение снова действует.</summary>
    public bool Toggle()
    {
        Paused = !Paused;
        Update();
        return !Paused;
    }

    public bool HasWindows => _windows.Count > 0;

    private void Update()
    {
        _windows.RemoveWhere(h => !NativeWindows.IsAlive(h));
        var foreground = NativeWindows.ForegroundWindow;
        if (!Paused && _windows.Contains(foreground) && !NativeWindows.IsMinimized(foreground)
            && NativeWindows.GetBounds(foreground) is { } b)
        {
            _rect = new RECT { Left = b.X, Top = b.Y, Right = b.X + b.Width, Bottom = b.Y + b.Height };
            ClipCursor(ref _rect);
            _clipping = true;
            if (_hook == IntPtr.Zero) _hook = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
        }
        else if (_clipping)
        {
            Release();
        }
        if (_windows.Count == 0) _timer.Stop();
    }

    /// <summary>Снять только своё ограничение: ClipCursor и хук.</summary>
    private void Release()
    {
        ClipCursor(IntPtr.Zero);
        _clipping = false;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    /// <summary>Движение за край окна — поставить курсор на край и «съесть» исходное движение.</summary>
    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        const int WmMouseMove = 0x0200;
        if (code >= 0 && _clipping && wParam.ToInt32() == WmMouseMove)
        {
            var p = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
            int x = Math.Clamp(p.X, _rect.Left, _rect.Right - 1), y = Math.Clamp(p.Y, _rect.Top, _rect.Bottom - 1);
            if (x != p.X || y != p.Y)
            {
                SetCursorPos(x, y);
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_clipping || _hook != IntPtr.Zero) Release();
    }

    private const int WhMouseLl = 14;
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extraInfo; }

    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}

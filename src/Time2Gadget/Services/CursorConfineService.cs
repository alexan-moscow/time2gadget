using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Time2Gadget.Services;

/// <summary>
/// «Не выпускать указатель мыши из окна» (докладка 2026-09-29): пока отмеченное окно активно, указатель ограничен его
/// границами (ClipCursor); переключились на другое окно — свободен. Ограничение повторяется 4 раза в секунду: игры сами
/// сбрасывают его при Alt+Tab и смене режима. Глобальная клавиша (AppSettings.CursorConfineKey) временно
/// выключает/включает ограничение для всех окон (<see cref="Toggle"/>). Таймер работает, только пока есть отмеченные окна.
/// </summary>
public sealed class CursorConfineService : IDisposable
{
    private readonly HashSet<IntPtr> _windows = new();
    private readonly DispatcherTimer _timer;
    private bool _clipping;

    public CursorConfineService()
    {
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
            var rect = new RECT { Left = b.X, Top = b.Y, Right = b.X + b.Width, Bottom = b.Y + b.Height };
            ClipCursor(ref rect);
            _clipping = true;
        }
        else if (_clipping)
        {
            ClipCursor(IntPtr.Zero); // сняли только своё ограничение
            _clipping = false;
        }
        if (_windows.Count == 0) _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_clipping) ClipCursor(IntPtr.Zero);
        _clipping = false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
}

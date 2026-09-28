using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>
/// Глобальные клавиши (докладка 2026-09-28): работают, даже когда программа свёрнута в трей.
/// Клавиатура — RegisterHotKey (Windows сообщает, если сочетание занято другой программой).
/// Кнопки мыши (средняя/«Назад»/«Вперёд») — низкоуровневый хук мыши; ставится, только если такие назначены;
/// назначенное нажатие «съедается» (иначе «Назад» ещё и листал бы страницу в браузере).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly Window _owner;
    private HwndSource? _source;
    private readonly List<int> _registeredIds = new();
    private readonly Dictionary<int, HotkeyBinding> _mouseBindings = new();
    private Dictionary<int, HotkeyBinding> _requested = new();
    private IntPtr _mouseHook;
    private readonly LowLevelMouseProc _mouseProc; // держим ссылку — иначе делегат соберёт GC
    private bool _suspended;
    private readonly HashSet<int> _swallowUpFor = new();

    /// <summary>Сработала клавиша с этим id.</summary>
    public event EventHandler<int>? Pressed;

    public GlobalHotkeyService(Window owner)
    {
        _owner = owner;
        _mouseProc = MouseHookProc;
    }

    /// <summary>
    /// Зарегистрировать набор (id → сочетание) вместо прежнего. Возвращает id, которые НЕ удалось
    /// зарегистрировать (сочетание занято другой программой/системой).
    /// </summary>
    public IReadOnlyCollection<int> Apply(IReadOnlyDictionary<int, HotkeyBinding> bindings)
    {
        _requested = bindings.Where(kv => !kv.Value.IsEmpty).ToDictionary(kv => kv.Key, kv => kv.Value);
        return _suspended ? Array.Empty<int>() : RegisterAll();
    }

    /// <summary>Приостановить (идёт ввод сочетания в настройках) / возобновить.</summary>
    public void Suspend(bool suspend)
    {
        if (_suspended == suspend) return;
        _suspended = suspend;
        if (suspend) UnregisterAll(); else RegisterAll();
    }

    private IReadOnlyCollection<int> RegisterAll()
    {
        UnregisterAll();
        var failed = new List<int>();
        var hwnd = EnsureSource();

        foreach (var (id, b) in _requested)
        {
            if (b.IsMouse) { _mouseBindings[id] = b; continue; }
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(b.Key);
            if (vk == 0 || !RegisterHotKey(hwnd, id, ToModFlags(b.Modifiers) | ModNoRepeat, vk)) failed.Add(id);
            else _registeredIds.Add(id);
        }

        if (_mouseBindings.Count > 0 && _mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        return failed;
    }

    private void UnregisterAll()
    {
        if (_source is not null)
            foreach (var id in _registeredIds) UnregisterHotKey(_source.Handle, id);
        _registeredIds.Clear();
        _mouseBindings.Clear();
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private IntPtr EnsureSource()
    {
        if (_source is null)
        {
            var hwnd = new WindowInteropHelper(_owner).EnsureHandle();
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(WndProc);
        }
        return _source.Handle;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg == WmHotkey && !_suspended)
        {
            Pressed?.Invoke(this, wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !_suspended)
        {
            int msg = wParam.ToInt32();
            HotkeyMouseButton button = HotkeyMouseButton.None;
            bool down = false, up = false;
            if (msg is WmMButtonDown or WmMButtonUp) { button = HotkeyMouseButton.Middle; down = msg == WmMButtonDown; up = !down; }
            else if (msg is WmXButtonDown or WmXButtonUp)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                button = (data.mouseData >> 16) == 1 ? HotkeyMouseButton.XButton1 : HotkeyMouseButton.XButton2;
                down = msg == WmXButtonDown; up = !down;
            }

            if (button != HotkeyMouseButton.None)
            {
                if (down)
                {
                    var mods = CurrentModifiers();
                    foreach (var (id, b) in _mouseBindings)
                    {
                        if (b.MouseButton != button || b.Modifiers != mods) continue;
                        _swallowUpFor.Add((int)button);
                        _owner.Dispatcher.BeginInvoke(() => Pressed?.Invoke(this, id));
                        return new IntPtr(1); // «съесть» нажатие
                    }
                }
                else if (up && _swallowUpFor.Remove((int)button))
                {
                    return new IntPtr(1); // и парное отпускание
                }
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private static ModifierKeys CurrentModifiers()
    {
        var m = ModifierKeys.None;
        if (IsDown(0x11)) m |= ModifierKeys.Control;
        if (IsDown(0x10)) m |= ModifierKeys.Shift;
        if (IsDown(0x12)) m |= ModifierKeys.Alt;
        if (IsDown(0x5B) || IsDown(0x5C)) m |= ModifierKeys.Windows;
        return m;
        static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static uint ToModFlags(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Alt) ? 0x1u : 0) | (m.HasFlag(ModifierKeys.Control) ? 0x2u : 0) |
        (m.HasFlag(ModifierKeys.Shift) ? 0x4u : 0) | (m.HasFlag(ModifierKeys.Windows) ? 0x8u : 0);

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private const uint ModNoRepeat = 0x4000;
    private const int WhMouseLl = 14;
    private const int WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}

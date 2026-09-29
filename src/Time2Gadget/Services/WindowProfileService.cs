using System.Runtime.InteropServices;
using System.Windows.Threading;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>
/// Автоприменение профилей размера окон (докладка 2026-09-29): программа с назначенным профилем открыла окно — профиль
/// применяется (и ещё несколько раз в первые секунды: игры переставляют себя при запуске); программа сама изменила размер
/// или место (например, сменили разрешение в игре) — профиль применяется снова. Не спорит с человеком (пока окно тащат
/// мышью — ждём) и с программой, которая упорно возвращает своё (не больше <see cref="MaxAppliesPerWindow"/> раз
/// за <see cref="ApplyWindow"/>). Без опроса — по событиям Windows.
/// </summary>
public sealed class WindowProfileService : IDisposable
{
    private static readonly TimeSpan[] StartAttemptDelays = { TimeSpan.Zero, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3) };
    private static readonly TimeSpan ReapplyDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ApplyWindow = TimeSpan.FromSeconds(30);
    private const int MaxAppliesPerWindow = 6;

    private readonly Func<string, WindowSizeProfile?> _findProfile;
    private readonly Action<IntPtr, bool>? _setCursorConfine;
    private readonly WinEventDelegate _winEventProc; // держим ссылку — иначе делегат соберёт GC, а хук останется
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<IntPtr, string?> _keys = new();          // кэш «окно → ключ программы» (OpenProcess — не на каждое событие)
    private readonly Dictionary<IntPtr, DispatcherTimer> _debounce = new();
    private readonly Dictionary<IntPtr, List<DateTime>> _applies = new();
    private IntPtr _hook, _hookMove;
    private readonly HashSet<IntPtr> _movedByUser = new(); // окно перетащил человек — профиль его больше не тянет назад
    private bool _enabled;

    /// <param name="setCursorConfine">Отметить окно «не выпускать указатель мыши» по профилю (MainViewModel.SetCursorConfine).</param>
    public WindowProfileService(Func<string, WindowSizeProfile?> findProfile, Action<IntPtr, bool>? setCursorConfine = null)
    {
        _findProfile = findProfile;
        _setCursorConfine = setCursorConfine;
        _winEventProc = OnWinEvent;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                // 0x8001 уничтожено, 0x8002 показано … 0x800B координаты изменились.
                _hook = SetWinEventHook(0x8001, 0x800B, IntPtr.Zero, _winEventProc, 0, 0, 0);
                _hookMove = SetWinEventHook(0x000A, 0x000A, IntPtr.Zero, _winEventProc, 0, 0, 0); // начали тащить окно
                ApplyToOpenWindows();
            }
            else
            {
                if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
                if (_hookMove != IntPtr.Zero) UnhookWinEvent(_hookMove);
                _hookMove = IntPtr.Zero;
                _movedByUser.Clear();
                _hook = IntPtr.Zero;
                foreach (var t in _debounce.Values) t.Stop();
                _debounce.Clear();
                _keys.Clear();
                _applies.Clear();
            }
        }
    }

    public void Dispose() => Enabled = false;

    /// <summary>Профили или назначения изменились — применить к уже открытым окнам (назначили профиль запущенной игре).</summary>
    public void ApplyToOpenWindows()
    {
        if (!_enabled) return;
        _keys.Clear();
        _applies.Clear();
        _movedByUser.Clear(); // профиль только что назначили/изменили — это явное желание, применяем и к сдвинутым руками
        foreach (var w in NativeWindows.EnumerateWindows(allWindows: false))
            if (w.ProgramKey is { } key && _findProfile(key) is { } profile)
                Apply(w.Handle, profile);
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // только сами окна
        const uint EventSystemMoveSizeStart = 0x000A, EventObjectDestroy = 0x8001, EventObjectShow = 0x8002, EventObjectLocationChange = 0x800B;
        switch (evt)
        {
            case EventObjectDestroy:
                _keys.Remove(hwnd);
                _applies.Remove(hwnd);
                _movedByUser.Remove(hwnd);
                if (_debounce.Remove(hwnd, out var t)) t.Stop();
                break;
            case EventSystemMoveSizeStart:
                // Окно тащит человек — профиль до повторного открытия окна его не тянет назад (докладка 2026-09-29).
                _movedByUser.Add(hwnd);
                break;
            case EventObjectShow:
                _keys.Remove(hwnd); // номер окна мог достаться новому окну
                _movedByUser.Remove(hwnd);
                if (ProfileFor(hwnd) is not null) ScheduleStart(hwnd);
                break;
            case EventObjectLocationChange:
                if (ProfileFor(hwnd) is not null) ScheduleReapply(hwnd);
                break;
        }
    }

    private WindowSizeProfile? ProfileFor(IntPtr hwnd)
    {
        if (!_keys.TryGetValue(hwnd, out var key))
            _keys[hwnd] = key = NativeWindows.Describe(hwnd) is { } info && info.Handle == hwnd ? info.ProgramKey : null;
        return key is null ? null : _findProfile(key);
    }

    private void ScheduleStart(IntPtr hwnd)
    {
        foreach (var delay in StartAttemptDelays)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = delay == TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay };
            timer.Tick += (_, _) => { timer.Stop(); if (ProfileFor(hwnd) is { } p) Apply(hwnd, p); };
            timer.Start();
        }
    }

    private void ScheduleReapply(IntPtr hwnd)
    {
        if (!_debounce.TryGetValue(hwnd, out var timer))
        {
            timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = ReapplyDebounce };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (IsMouseButtonDown()) { timer.Start(); return; } // окно тащат/тянут — дождаться, пока отпустят
                if (ProfileFor(hwnd) is { } p) Apply(hwnd, p);
            };
            _debounce[hwnd] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    private void Apply(IntPtr hwnd, WindowSizeProfile profile)
    {
        if (_movedByUser.Contains(hwnd)) return; // перетащили руками — ручное положение главнее
        if (!NativeWindows.IsAlive(hwnd) || NativeWindows.GetBounds(hwnd) is not { } b) return;
        _setCursorConfine?.Invoke(hwnd, profile.ConfineCursor); // и когда окно уже на месте
        var target = new WindowBounds(profile.X, profile.Y, profile.Width, profile.Height, profile.Borderless);
        bool matches = b.X == target.X && b.Y == target.Y && b.Width == target.Width && b.Height == target.Height
                       && (!profile.Borderless || b.Borderless);
        if (matches) return;

        // Программа упорно возвращает своё — не устраивать бесконечное перетягивание.
        var now = DateTime.UtcNow;
        var times = _applies.TryGetValue(hwnd, out var list) ? list : _applies[hwnd] = new List<DateTime>();
        times.RemoveAll(t => now - t > ApplyWindow);
        if (times.Count >= MaxAppliesPerWindow) return;
        times.Add(now);

        NativeWindows.Apply(hwnd, target, profile.NotifyResize);
    }

    private static bool IsMouseButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}

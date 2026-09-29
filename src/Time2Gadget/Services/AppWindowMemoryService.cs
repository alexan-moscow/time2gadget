using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace Time2Gadget.Services;

/// <summary>
/// «Открывать окна программ там, где их закрыли» (докладка 2026-09-28; по умолчанию выкл). Для оконных программ и игр:
/// последнее место и размер окна запоминаются по программе (exe + класс окна), а когда программа снова открывает окно —
/// оно сразу ставится туда же (развёрнутое — развёрнутым) плюс контрольные попытки, если программа переставит себя сама.
/// <para>Не трогаем: окна во весь монитор (полноэкранные и «полный экран в окне»), служебные/диалоговые окна, свои окна,
/// и программы, у которых открыто несколько окон одного вида (папки проводника) — иначе все встали бы в одно место.</para>
/// <para>Без опроса — по событиям Windows. Пока Windows сама переставляет окна (смена мониторов, пробуждение), запоминание
/// приостановлено. Память — %APPDATA%\Time2Gadget\window-memory.json.</para>
/// </summary>
public sealed class AppWindowMemoryService : IDisposable
{
    private static readonly string MemoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "window-memory.json");

    /// <summary>Контрольные попытки после появления окна: программы и игры часто переставляют себя в первые секунды.</summary>
    private static readonly TimeSpan[] ApplyAttemptDelays = { TimeSpan.Zero, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(2.5) };
    private static readonly TimeSpan RecordDebounce = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan PauseAfterDisplayChange = TimeSpan.FromMinutes(1);
    private const int MaxEntries = 300;

    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly WinEventDelegate _winEventProc; // держим ссылку — иначе делегат соберёт GC, а хук останется
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _recordTimer, _saveTimer;
    private readonly HashSet<IntPtr> _pendingRecord = new();
    private readonly HashSet<IntPtr> _known = new();              // окна, уже бывшие видимыми, — новыми не считаем
    private readonly Dictionary<IntPtr, Rect32> _placedByUs = new(); // куда поставили — чтобы попытки не спорили с человеком
    private Dictionary<string, SavedPlacement> _memory = new();
    private DateTime _pausedUntil;
    private IntPtr _hookSystem, _hookObject;
    private bool _enabled;

    public AppWindowMemoryService()
    {
        _winEventProc = OnWinEvent;
        _recordTimer = new DispatcherTimer { Interval = RecordDebounce };
        _recordTimer.Tick += (_, _) => { _recordTimer.Stop(); RecordPending(); };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) Start(); else Stop();
        }
    }

    public void Dispose() => Enabled = false;

    /// <summary>У программы есть профиль размера окна — её окна ведёт профиль, память их не трогает (докладка 2026-09-29).</summary>
    public Func<string, bool>? IsProfiled { get; set; }

    private void Start()
    {
        Load();
        // Уже открытые окна — не «новые»: их не двигаем, только запоминаем.
        EnumWindows((hwnd, _) => { if (IsWindowVisible(hwnd)) { _known.Add(hwnd); Record(hwnd); } return true; }, IntPtr.Zero);
        // 0x000B перемещение/размер закончены … 0x0017 развёрнуто из свёрнутого; 0x8001 уничтожено, 0x8002 показано … 0x800B координаты изменились.
        _hookSystem = SetWinEventHook(0x000B, 0x0017, IntPtr.Zero, _winEventProc, 0, 0, WineventOutOfContext);
        _hookObject = SetWinEventHook(0x8001, 0x800B, IntPtr.Zero, _winEventProc, 0, 0, WineventOutOfContext);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChange;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void Stop()
    {
        if (_hookSystem != IntPtr.Zero) UnhookWinEvent(_hookSystem);
        if (_hookObject != IntPtr.Zero) UnhookWinEvent(_hookObject);
        _hookSystem = _hookObject = IntPtr.Zero;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChange;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _recordTimer.Stop();
        if (_saveTimer.IsEnabled) { _saveTimer.Stop(); Save(); }
        _pendingRecord.Clear();
        _known.Clear();
        _placedByUs.Clear();
    }

    // Windows переставляет окна при смене мониторов и после сна — такие места не запоминаем.
    private void OnDisplayChange(object? sender, EventArgs e) => _pausedUntil = DateTime.UtcNow + PauseAfterDisplayChange;

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume)
            _pausedUntil = DateTime.UtcNow + PauseAfterDisplayChange;
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // только сами окна, не их содержимое/курсор
        const uint EventObjectDestroy = 0x8001, EventObjectShow = 0x8002;
        if (evt == EventObjectDestroy)
        {
            // Номер окна Windows может выдать новому окну — забываем закрытое, иначе новое сочли бы «знакомым».
            _known.Remove(hwnd);
            _placedByUs.Remove(hwnd);
            _pendingRecord.Remove(hwnd);
            return;
        }
        if (evt == EventObjectShow && _known.Add(hwnd))
        {
            // Новое окно: сразу поставить на запомненное место (и проверить ещё несколько раз).
            _dispatcher.BeginInvoke(() => ScheduleApply(hwnd), DispatcherPriority.Send);
            return;
        }
        _pendingRecord.Add(hwnd);
        _recordTimer.Stop();
        _recordTimer.Start();
    }

    // ---------------- Запоминание ----------------

    private void RecordPending()
    {
        foreach (var hwnd in _pendingRecord) Record(hwnd);
        _pendingRecord.Clear();
    }

    private void Record(IntPtr hwnd)
    {
        if (DateTime.UtcNow < _pausedUntil || !IsWindow(hwnd) || !IsWindowVisible(hwnd)) return;
        if (GetKey(hwnd) is not { } key || IsFullScreen(hwnd) || IsProfiled?.Invoke(key) == true) return;
        var p = NewPlacement();
        if (!GetWindowPlacement(hwnd, ref p)) return;
        if (p.showCmd == SwShowMinimized && (p.flags & WpfRestoreToMaximized) == 0) p.showCmd = SwShowNormal; // свёрнутое — открыть обычным
        bool maximized = p.showCmd == SwShowMaximized || (p.flags & WpfRestoreToMaximized) != 0;
        var saved = new SavedPlacement(p.rcNormalPosition.Left, p.rcNormalPosition.Top, p.rcNormalPosition.Right, p.rcNormalPosition.Bottom, maximized);
        if (_memory.TryGetValue(key, out var old) && old == saved) return;
        _memory[key] = saved with { UsedUtc = DateTime.UtcNow };
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------------- Возврат ----------------

    private void ScheduleApply(IntPtr hwnd)
    {
        foreach (var delay in ApplyAttemptDelays)
        {
            if (delay == TimeSpan.Zero) { Apply(hwnd, first: true); continue; }
            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => { timer.Stop(); Apply(hwnd, first: false); };
            timer.Start();
        }
    }

    private void Apply(IntPtr hwnd, bool first)
    {
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return;
        if (GetKey(hwnd) is not { } key || IsProfiled?.Invoke(key) == true || !_memory.TryGetValue(key, out var saved)) return;
        if (IsFullScreen(hwnd) || HasSiblingOfSameKind(hwnd, key)) return;

        var current = NewPlacement();
        if (!GetWindowPlacement(hwnd, ref current)) return;
        var target = new Rect32(saved.Left, saved.Top, saved.Right, saved.Bottom);
        bool curMax = current.showCmd == SwShowMaximized;
        if (current.rcNormalPosition.Equals(target) && curMax == saved.Maximized) return; // уже на месте

        // Контрольная попытка: окно успел сдвинуть человек (не программа при запуске) — не спорим.
        if (!first && _placedByUs.TryGetValue(hwnd, out var ours) && !current.rcNormalPosition.Equals(ours) && IsMouseButtonDown()) return;
        if (!IsOnSomeMonitor(target)) return; // монитор, где окно было, сейчас отключён

        var p = current;
        p.rcNormalPosition = target;
        p.showCmd = saved.Maximized ? SwShowMaximized : SwShowNoActivate;
        p.flags = 0;
        if (SetWindowPlacement(hwnd, ref p)) _placedByUs[hwnd] = target;
    }

    /// <summary>У программы открыто ещё одно видимое окно того же вида — непонятно, какое куда: не трогаем.</summary>
    private bool HasSiblingOfSameKind(IntPtr hwnd, string key)
    {
        bool found = false;
        EnumWindows((other, _) =>
        {
            if (other != hwnd && IsWindowVisible(other) && GetKey(other) == key) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // ---------------- Какие окна ----------------

    /// <summary>Ключ «программа + вид окна» или null, если окно не наше дело (служебное, диалог, своё).</summary>
    private string? GetKey(IntPtr hwnd)
    {
        if (GetWindow(hwnd, GwOwner) != IntPtr.Zero) return null; // диалоги и дочерние окна программ
        long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((exStyle & WsExToolWindow) != 0) return null;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        if ((style & WsChild) != 0) return null;
        if (GetWindowTextLength(hwnd) == 0) return null;
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid) return null;
        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, cls.Capacity);
        string className = cls.ToString();
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Dwm" or "#32770"
            or "Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow") return null; // рабочий стол, панель задач, диалоги, UWP
        return NativeWindows.MakeProgramKey(NativeWindows.ProcessPath(pid), className); // общий ключ с профилями размера окон
    }

    /// <summary>Окно во весь монитор — полноэкранная игра/видео или «полный экран в окне»: не трогаем.</summary>
    private static bool IsFullScreen(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var m = info.rcMonitor;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        bool maximizedWithFrame = (style & WsMaximize) != 0 && (style & WsCaption) == WsCaption;
        return !maximizedWithFrame && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static bool IsOnSomeMonitor(Rect32 r) => MonitorFromRect(ref r, MonitorDefaultToNull) != IntPtr.Zero;

    private static bool IsMouseButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0; // левая кнопка — окно тащат

    // ---------------- Файл памяти ----------------

    private void Load()
    {
        try
        {
            if (File.Exists(MemoryPath))
                _memory = JsonSerializer.Deserialize<Dictionary<string, SavedPlacement>>(File.ReadAllText(MemoryPath)) ?? new();
        }
        catch
        {
            _memory = new(); // повреждён — начинаем заново
        }
    }

    private void Save()
    {
        try
        {
            if (_memory.Count > MaxEntries) // давно не открывавшиеся программы забываем
                _memory = _memory.OrderByDescending(kv => kv.Value.UsedUtc).Take(MaxEntries).ToDictionary(kv => kv.Key, kv => kv.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(MemoryPath)!);
            File.WriteAllText(MemoryPath, JsonSerializer.Serialize(_memory, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // нет доступа к папке — память просто не сохранится между запусками
        }
    }

    /// <summary>Запомненное место окна: прямоугольник обычного (не развёрнутого) состояния + было ли развёрнуто.</summary>
    public sealed record SavedPlacement(int Left, int Top, int Right, int Bottom, bool Maximized)
    {
        public DateTime UsedUtc { get; init; }
        // Сравнение мест — без даты.
        public bool Equals(SavedPlacement? other) => other is not null && Left == other.Left && Top == other.Top
            && Right == other.Right && Bottom == other.Bottom && Maximized == other.Maximized;
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom, Maximized);
    }

    // ---------------- WinAPI ----------------

    private static WINDOWPLACEMENT NewPlacement() => new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };

    private const int SwShowNormal = 1, SwShowMinimized = 2, SwShowMaximized = 3, SwShowNoActivate = 4;
    private const int WpfRestoreToMaximized = 0x2;
    private const uint WineventOutOfContext = 0;
    private const int GwOwner = 4, GwlStyle = -16, GwlExStyle = -20, DwmwaCloaked = 14;
    private const long WsExToolWindow = 0x80, WsChild = 0x40000000, WsMaximize = 0x01000000, WsCaption = 0x00C00000;
    private const uint MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
        public Rect32(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public Rect32 rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public Rect32 rcMonitor, rcWork; public uint dwFlags; }

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect32 rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}

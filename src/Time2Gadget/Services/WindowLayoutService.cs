using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Time2Gadget.Services;

/// <summary>
/// «Возвращать окна других программ на их мониторы после сна» (docs/DECISIONS.md, 2026-09-28; по умолчанию выкл).
/// Когда монитор гаснет/уходит в сон, он (особенно по DisplayPort) для Windows «отключается», и Windows переносит
/// окна с него на оставшийся монитор, а обратно не возвращает (в Windows 10 нет встроенной настройки).
/// <para>Без опроса: снимок расстановки обновляется по событиям Windows (окно перемещено/изменено/свёрнуто/
/// развёрнуто/показано — с задержкой 1 с после последнего), плюс в последний момент — при уведомлении «экран
/// гаснет» и перед сном. Пока какого-то монитора нет, снимок заморожен (иначе запомнили бы уже «съехавшие» окна).
/// Когда мониторы вернулись — несколько попыток расставить окна по снимку.</para>
/// <para>Окна программ, запущенных с правами администратора, двигаются, только если Тайм2гаджет запущен так же.</para>
/// </summary>
public sealed class WindowLayoutService : IDisposable
{
    private static readonly TimeSpan SnapshotDebounce = TimeSpan.FromSeconds(1);
    /// <summary>
    /// Попытки расставить окна от момента «экран включился/пробуждение/смена мониторов» (докладка 2026-09-28:
    /// было 1/3/6/12/25 с — ощутимая пауза). Частые в начале: монитор обычно появляется за 1–4 с.
    /// Плюс на каждое «монитор вернулся» расписание запускается заново — реакция почти мгновенная.
    /// </summary>
    internal static readonly TimeSpan[] RestoreAttemptDelays =
        new[] { 0.3, 0.8, 1.5, 2.5, 4, 6, 9, 13, 20, 30 }.Select(TimeSpan.FromSeconds).ToArray();

    /// <summary>После того как все мониторы на месте — ещё столько попыток (включая первую удачную): часть окон Windows двигает с опозданием.</summary>
    private const int AttemptsAfterMonitorsBack = 3;
    private int _attemptsSinceBack;
    /// <summary>Если монитор так и не вернулся (отключили насовсем) — через столько принимаем новую конфигурацию.</summary>
    private static readonly TimeSpan MaxFreeze = TimeSpan.FromMinutes(10);

    private readonly Window _owner;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _restoreTimer;
    private readonly WinEventDelegate _winEventProc; // держим ссылку — иначе делегат соберёт GC, а хук останется

    private Dictionary<IntPtr, WINDOWPLACEMENT> _snapshot = new();
    private int _snapshotMonitors;
    private DateTime? _frozenSince;
    private int _restoreAttempt;
    private bool _enabled;
    private IntPtr _hookSystem, _hookObject, _powerNotify;
    private HwndSource? _source;

    public WindowLayoutService(Window owner)
    {
        _owner = owner;
        _winEventProc = OnWinEvent;
        _debounce = new DispatcherTimer { Interval = SnapshotDebounce };
        _debounce.Tick += (_, _) => { _debounce.Stop(); TakeSnapshot(); };
        _restoreTimer = new DispatcherTimer();
        _restoreTimer.Tick += OnRestoreTick;
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

    private void Start()
    {
        // События: 0x000B–0x0017 (перемещение/размер закончены, свернуть/развернуть), 0x8002–0x800B (показано,
        // скрыто, изменились координаты — в т.ч. развернуть на весь экран и Win+стрелки).
        _hookSystem = SetWinEventHook(0x000B, 0x0017, IntPtr.Zero, _winEventProc, 0, 0, WineventOutOfContext);
        _hookObject = SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, _winEventProc, 0, 0, WineventOutOfContext);

        var hwnd = new WindowInteropHelper(_owner).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        var guid = GuidConsoleDisplayState;
        _powerNotify = RegisterPowerSettingNotification(hwnd, ref guid, 0);

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _frozenSince = null;
        TakeSnapshot();
    }

    private void Stop()
    {
        if (_hookSystem != IntPtr.Zero) UnhookWinEvent(_hookSystem);
        if (_hookObject != IntPtr.Zero) UnhookWinEvent(_hookObject);
        _hookSystem = _hookObject = IntPtr.Zero;
        if (_powerNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_powerNotify);
        _powerNotify = IntPtr.Zero;
        _source?.RemoveHook(WndProc);
        _source = null;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _debounce.Stop();
        _restoreTimer.Stop();
        _snapshot.Clear();
    }

    public void Dispose() => Enabled = false;

    // ---------------- Снимок ----------------

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // только сами окна, не их содержимое/курсор
        if (IsFrozen) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private bool IsFrozen
    {
        get
        {
            if (_frozenSince is not { } since) return false;
            if (DateTime.UtcNow - since < MaxFreeze) return true;
            _frozenSince = null; // монитор, видимо, отключили насовсем — принимаем новую конфигурацию
            return false;
        }
    }

    private void Freeze() => _frozenSince ??= DateTime.UtcNow;

    private void TakeSnapshot()
    {
        if (IsFrozen) return;
        var snapshot = new Dictionary<IntPtr, WINDOWPLACEMENT>();
        EnumWindows((hwnd, _) =>
        {
            if (IsCandidate(hwnd))
            {
                var p = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (GetWindowPlacement(hwnd, ref p)) snapshot[hwnd] = p;
            }
            return true;
        }, IntPtr.Zero);
        _snapshot = snapshot;
        _snapshotMonitors = MonitorCount;
    }

    /// <summary>Обычные окна программ: видимые или свёрнутые, без владельца, не служебные, не свои.</summary>
    private bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) && !IsIconic(hwnd)) return false;
        if (GetWindow(hwnd, GwOwner) != IntPtr.Zero) return false;
        if ((GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid) return false; // свои окна возвращает MainWindow
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false; // скрытые UWP/другой рабочий стол
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    // ---------------- Сон / мониторы ----------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmPowerBroadcast = 0x0218, PbtPowerSettingChange = 0x8013;
        if (msg == WmPowerBroadcast && wParam.ToInt32() == PbtPowerSettingChange && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == GuidConsoleDisplayState)
            {
                if (setting.Data == 0) // экран гаснет — запомнить в последний момент, пока мониторы ещё на месте
                {
                    _debounce.Stop();
                    TakeSnapshot();
                    Freeze();
                }
                else if (setting.Data == 1) // экран включился
                {
                    ScheduleRestore();
                }
            }
        }
        return IntPtr.Zero;
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend) { _debounce.Stop(); TakeSnapshot(); Freeze(); }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume) ScheduleRestore();
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _owner.Dispatcher.BeginInvoke(() =>
        {
            if (MonitorCount < _snapshotMonitors) Freeze(); // монитор пропал — снимок не трогаем
            ScheduleRestore();
        });
    }

    private void ScheduleRestore()
    {
        if (_snapshot.Count == 0) return;
        _restoreAttempt = 0;
        _attemptsSinceBack = 0;
        _restoreTimer.Stop();
        _restoreTimer.Interval = RestoreAttemptDelays[0];
        _restoreTimer.Start();
    }

    private void OnRestoreTick(object? sender, EventArgs e)
    {
        bool allMonitorsBack = MonitorCount >= _snapshotMonitors;
        if (allMonitorsBack)
        {
            RestoreAll();
            _attemptsSinceBack++;
        }

        _restoreAttempt++;
        if (_restoreAttempt >= RestoreAttemptDelays.Length || _attemptsSinceBack >= AttemptsAfterMonitorsBack)
        {
            // Несколько попыток после возвращения мониторов (окна некоторых программ Windows двигает с
            // опозданием) — потом снова обновляем снимок по событиям.
            _restoreTimer.Stop();
            if (allMonitorsBack)
            {
                _frozenSince = null;
                _debounce.Stop();
                _debounce.Start();
            }
            return;
        }
        _restoreTimer.Interval = RestoreAttemptDelays[_restoreAttempt] - RestoreAttemptDelays[_restoreAttempt - 1];
    }

    private void RestoreAll()
    {
        foreach (var (hwnd, saved) in _snapshot)
        {
            if (!IsWindow(hwnd)) continue; // окно закрыли
            var current = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref current)) continue;
            if (current.showCmd == saved.showCmd && current.rcNormalPosition.Equals(saved.rcNormalPosition)) continue;

            var p = saved;
            // Без перехвата фокуса: обычные — «показать, не активируя», свёрнутые — «свернуть, не активируя».
            // Свёрнутое окно остаётся свёрнутым, но развернётся уже на своём мониторе (rcNormalPosition).
            p.showCmd = saved.showCmd switch
            {
                SwShowMinimized => SwShowMinNoActive,
                SwShowNormal => SwShowNoActivate,
                _ => saved.showCmd // развёрнутые — SW_SHOWMAXIMIZED: разворачиваются на мониторе из rcNormalPosition
            };
            SetWindowPlacement(hwnd, ref p);
        }
    }

    private static int MonitorCount => GetSystemMetrics(80); // SM_CMONITORS

    // ---------------- WinAPI ----------------

    private static readonly Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private const uint WineventOutOfContext = 0;
    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const int DwmwaCloaked = 14;
    private const int SwShowNormal = 1, SwShowMinimized = 2, SwShowNoActivate = 4, SwShowMinNoActive = 7;

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public int Data; // для GUID_CONSOLE_DISPLAY_STATE: 0 — выкл, 1 — вкл, 2 — затемнён
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr hwnd, ref Guid setting, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
}

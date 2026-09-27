using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Time2Gadget.Services;

/// <summary>
/// Сон и мониторы (docs/DECISIONS.md, 2026-09-28). Сама программа НИЧЕГО не держит, пока таймер просто идёт:
/// компьютер и мониторы засыпают по своим настройкам. Вмешательство только по окончании таймера:
/// <list type="bullet">
/// <item>таймер пробуждения Windows на момент окончания — компьютер выйдет из сна (если в схеме питания
/// разрешены таймеры пробуждения);</item>
/// <item>по окончании — включить погасшие мониторы и подержать их включёнными минуту, потом отпустить.</item>
/// </list>
/// </summary>
public sealed class PowerService : IDisposable
{
    private static readonly TimeSpan DisplayHold = TimeSpan.FromMinutes(1);

    private IntPtr _wakeTimer;
    private DateTime? _scheduledWakeUtc;
    private readonly DispatcherTimer _releaseDisplayTimer;

    public PowerService()
    {
        _releaseDisplayTimer = new DispatcherTimer { Interval = DisplayHold };
        _releaseDisplayTimer.Tick += (_, _) => ReleaseDisplay();
    }

    /// <summary>
    /// Поставить/переставить/снять таймер пробуждения. null — снять. Переставляется только при заметном
    /// расхождении (>1.5 с): зовётся на каждом UI-тике.
    /// </summary>
    public void ScheduleWake(DateTime? dueUtc)
    {
        if (dueUtc is null)
        {
            if (_scheduledWakeUtc is null) return;
            if (_wakeTimer != IntPtr.Zero) CancelWaitableTimer(_wakeTimer);
            _scheduledWakeUtc = null;
            return;
        }

        if (_scheduledWakeUtc is { } current && Math.Abs((current - dueUtc.Value).TotalSeconds) < 1.5) return;

        if (_wakeTimer == IntPtr.Zero)
        {
            _wakeTimer = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TimerAllAccess);
            if (_wakeTimer == IntPtr.Zero) return; // не удалось — таймер отработает как обычно, просто без пробуждения
        }

        long due = dueUtc.Value.ToFileTimeUtc(); // положительное значение = абсолютное время UTC
        if (SetWaitableTimer(_wakeTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, fResume: true))
            _scheduledWakeUtc = dueUtc;
    }

    /// <summary>
    /// Включить мониторы (таймер закончился). Заявка «экран нужен» будит погасший дисплей и не даёт ему
    /// снова погаснуть минуту; лёгкий сдвиг курсора туда-обратно — как движение мыши, надёжно включает
    /// мониторы, погашенные по простою (одной заявки на части систем недостаточно).
    /// </summary>
    public void WakeDisplay()
    {
        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        mouse_event(MouseEventMove, 1, 0, 0, IntPtr.Zero);
        mouse_event(MouseEventMove, -1, 0, 0, IntPtr.Zero);
        _releaseDisplayTimer.Stop();
        _releaseDisplayTimer.Start();
    }

    /// <summary>Отпустить экран/систему — снова засыпают по своим настройкам.</summary>
    public void ReleaseDisplay()
    {
        _releaseDisplayTimer.Stop();
        SetThreadExecutionState(EsContinuous);
    }

    public void Dispose()
    {
        ReleaseDisplay();
        if (_wakeTimer != IntPtr.Zero)
        {
            CancelWaitableTimer(_wakeTimer);
            CloseHandle(_wakeTimer);
            _wakeTimer = IntPtr.Zero;
        }
    }

    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;
    private const uint MouseEventMove = 0x0001;

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint esFlags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool fResume);
    [DllImport("kernel32.dll")] private static extern bool CancelWaitableTimer(IntPtr timer);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
}

using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <inheritdoc cref="ITimerEngine"/>
public sealed class TimerEngine : ITimerEngine
{
    private DateTime? _endTimeUtc;
    private TimeSpan _pausedRemaining;
    private bool _finishedRaised;

    public TimerStatus Status { get; private set; } = TimerStatus.Ready;
    public TimeSpan TotalDuration { get; private set; } = TimeSpan.Zero;

    public event EventHandler? Finished;
    public event EventHandler? StatusChanged;

    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    private static TimeSpan Clamp(TimeSpan duration)
    {
        if (duration < MinDuration) return MinDuration;
        if (duration > MaxDuration) return MaxDuration;
        return duration;
    }

    public void SetDuration(TimeSpan duration)
    {
        if (Status == TimerStatus.Running) return; // нельзя менять время во время отсчёта (UI-CONTRACT.md)
        duration = Clamp(duration);
        TotalDuration = duration;
        _pausedRemaining = duration;
        if (Status == TimerStatus.Finished) SetStatus(TimerStatus.Ready);
    }

    public void AddDuration(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero) return;

        var newTotal = Clamp(TotalDuration + delta);
        var actualDelta = newTotal - TotalDuration;
        if (actualDelta <= TimeSpan.Zero) return; // уже на максимуме
        TotalDuration = newTotal;

        switch (Status)
        {
            case TimerStatus.Running when _endTimeUtc is not null:
                _endTimeUtc = _endTimeUtc.Value + actualDelta; // продлеваем дедлайн на лету
                break;
            case TimerStatus.Paused:
                _pausedRemaining += actualDelta;
                break;
            case TimerStatus.Finished:
                // Добавление времени к завершённому таймеру возвращает его в Ready с новой
                // полной длительностью — считать "заново поставленным", не автопродолжением.
                _pausedRemaining = TotalDuration;
                _finishedRaised = false;
                SetStatus(TimerStatus.Ready);
                break;
            default: // Ready
                _pausedRemaining = TotalDuration;
                break;
        }
    }

    public void Start()
    {
        if (Status == TimerStatus.Running) return;

        var baseRemaining = Status == TimerStatus.Paused ? _pausedRemaining : TotalDuration;
        if (baseRemaining <= TimeSpan.Zero) baseRemaining = TotalDuration;

        _endTimeUtc = DateTime.UtcNow + baseRemaining;
        _finishedRaised = false;
        SetStatus(TimerStatus.Running);
    }

    public void Restart(TimeSpan duration)
    {
        duration = Clamp(duration);
        TotalDuration = duration;
        _pausedRemaining = duration;
        _endTimeUtc = DateTime.UtcNow + duration;
        _finishedRaised = false;
        SetStatus(TimerStatus.Running);
    }

    public void Pause()
    {
        if (Status != TimerStatus.Running || _endTimeUtc is null) return;

        _pausedRemaining = _endTimeUtc.Value - DateTime.UtcNow;
        if (_pausedRemaining < TimeSpan.Zero) _pausedRemaining = TimeSpan.Zero;
        _endTimeUtc = null;
        SetStatus(TimerStatus.Paused);
    }

    /// <summary>
    /// Сброс к Ready c 00:00 (docs/DECISIONS.md, 2026-09-27: «при нажатии на сброс время должно
    /// быть 00:00 - и при запуске также») — в отличие от старого поведения, НЕ возвращает
    /// TotalDuration к последнему выбранному пресету: следующий отсчёт начинается только явным
    /// кликом по сектору (который сам вызывает SetDuration+Start).
    /// </summary>
    public void Reset()
    {
        _endTimeUtc = null;
        TotalDuration = TimeSpan.Zero;
        _pausedRemaining = TimeSpan.Zero;
        _finishedRaised = false;
        SetStatus(TimerStatus.Ready);
    }

    public TimeSpan GetRemaining()
    {
        TimeSpan remaining = Status switch
        {
            TimerStatus.Running => _endTimeUtc is null ? TimeSpan.Zero : _endTimeUtc.Value - DateTime.UtcNow,
            TimerStatus.Paused => _pausedRemaining,
            TimerStatus.Finished => TimeSpan.Zero,
            _ => TotalDuration
        };

        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        // Обнаружение завершения делается здесь (вызывается из UI-тика через GetRemaining()),
        // а не отдельным таймером — единая точка проверки, см. ARCHITECTURE.md.
        if (Status == TimerStatus.Running && remaining == TimeSpan.Zero && !_finishedRaised)
        {
            _finishedRaised = true;
            SetStatus(TimerStatus.Finished);
            Finished?.Invoke(this, EventArgs.Empty);
        }

        return remaining;
    }

    private void SetStatus(TimerStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

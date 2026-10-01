using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Time2Gadget.Models;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Панель быстрых таймеров (докладка 2026-10-01): отдельное окошко высотой с компактный вид, в нём до 5 быстрых таймеров
/// с галочкой «в панели». Таймеры панели идут сами по себе — главный таймер они не трогают (клавиша быстрого таймера,
/// как и раньше, запускает главный). Окно — Views/QuickPanelWindow, показ/скрытие — клавиша или кнопка в окне «Быстрые таймеры».
/// </summary>
public sealed partial class MainViewModel
{
    public const int QuickPanelHotkeyId = 9;

    /// <summary>Таймеры панели — в порядке строк окна «Быстрые таймеры».</summary>
    public ObservableCollection<QuickPanelTimer> PanelTimers { get; } = new();

    public bool HasPanelTimers => PanelTimers.Count > 0;

    public bool QuickPanelHotkeyEnabled
    {
        get => _settings.QuickPanelHotkeyEnabled;
        set => SetGlobal(value, _settings.QuickPanelHotkeyEnabled, v => _settings.QuickPanelHotkeyEnabled = v, nameof(QuickPanelKeyShown));
    }

    public HotkeyBinding QuickPanelKey
    {
        get => _settings.QuickPanelKey ?? HotkeyBinding.Empty;
        set => SetKey(value, (s, v) => s.QuickPanelKey = v, global: true);
    }

    /// <summary>Поле сочетания: выключено — «Не задано», включено — сочетание (по умолчанию Ctrl+Shift+Insert).</summary>
    public HotkeyBinding QuickPanelKeyShown
    {
        get => QuickPanelHotkeyEnabled ? QuickPanelKey : HotkeyBinding.Empty;
        set { if (QuickPanelHotkeyEnabled) { QuickPanelKey = value; OnPropertyChanged(); } }
    }

    /// <summary>Показать/скрыть панель (клавиша, кнопка в окне «Быстрые таймеры»).</summary>
    public event EventHandler? QuickPanelToggleRequested;

    /// <summary>Показать панель (таймер панели закончился, пока она скрыта).</summary>
    public event EventHandler? QuickPanelShowRequested;

    public RelayCommand ToggleQuickPanelCommand => _toggleQuickPanelCommand ??= new RelayCommand(() => QuickPanelToggleRequested?.Invoke(this, EventArgs.Empty));
    private RelayCommand? _toggleQuickPanelCommand;

    /// <summary>Размер панели (ползунок в окне «Быстрые таймеры»): 100% — высота компактного вида.</summary>
    public double QuickPanelScale
    {
        get => _settings.QuickPanelScale;
        set
        {
            var v = Math.Round(Math.Clamp(value, 1.0, 2.5), 2);
            if (Math.Abs(_settings.QuickPanelScale - v) < 0.001) return;
            _settings.QuickPanelScale = v;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickPanelScaleLabel));
            OnPropertyChanged(nameof(QuickPanelViewScale));
        }
    }

    public string QuickPanelScaleLabel => $"Размер панели: {Math.Round(QuickPanelScale * 100)}%";

    /// <summary>Масштаб окна панели: компактный вид × свой размер панели.</summary>
    public double QuickPanelViewScale => CompactViewScale * QuickPanelScale;

    internal bool QuickPanelOpen => _settings.QuickPanelOpen;

    internal void SetQuickPanelOpen(bool open)
    {
        if (_settings.QuickPanelOpen == open) return;
        _settings.QuickPanelOpen = open;
        _settingsService.Save(_settings);
    }

    internal (double Left, double Top)? QuickPanelPosition =>
        _settings.QuickPanelLeft is { } x && _settings.QuickPanelTop is { } y ? (x, y) : null;

    internal void SaveQuickPanelPosition(double left, double top)
    {
        _settings.QuickPanelLeft = left;
        _settings.QuickPanelTop = top;
        _settingsService.Save(_settings);
    }

    /// <summary>Список панели — заново по галочкам «в панели» (идущие таймеры не сбрасываются: состояние живёт в строке).</summary>
    internal void RefreshPanelTimers()
    {
        var wanted = QuickTimers.Where(q => q.Model.ShowInPanel).Select(q => q.Panel).ToList();
        for (int i = PanelTimers.Count - 1; i >= 0; i--)
            if (!wanted.Contains(PanelTimers[i])) { PanelTimers[i].Stop(); PanelTimers.RemoveAt(i); }
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = PanelTimers.IndexOf(wanted[i]);
            if (at < 0) PanelTimers.Insert(i, wanted[i]);
            else if (at != i) PanelTimers.Move(at, i);
        }
        OnPropertyChanged(nameof(HasPanelTimers));
    }

    // Один тик на все таймеры панели — только пока хоть один идёт или показывает эффект окончания.
    private DispatcherTimer? _panelTicker;

    internal void UpdatePanelTicker()
    {
        bool needed = QuickTimers.Any(q => q.Panel.NeedsTicks);
        if (needed && _panelTicker is null)
        {
            _panelTicker = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            _panelTicker.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                foreach (var q in QuickTimers.ToList()) q.Panel.Tick(now);
                UpdatePanelTicker();
            };
        }
        if (_panelTicker is null) return;
        if (needed && !_panelTicker.IsEnabled) _panelTicker.Start();
        else if (!needed && _panelTicker.IsEnabled) _panelTicker.Stop();
    }

    /// <summary>Таймер панели закончился: свой звук (если включён) один раз, панель — показать.</summary>
    internal void OnPanelTimerFinished(QuickTimer timer)
    {
        if (timer.SoundEnabled)
        {
            StopPreview();
            _soundService.PlayPreview(_settings, timer.Sound);
        }
        QuickPanelShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Длительность эффекта окончания (общая, «Длительность эффекта»); null — пока не нажмут.</summary>
    internal TimeSpan? PanelFinishDuration =>
        _settings.FinishEffectDurationSeconds > 0 ? TimeSpan.FromSeconds(_settings.FinishEffectDurationSeconds) : null;
}

public enum QuickPanelState { Ready, Running, Paused, Finished }

/// <summary>Отсчёт быстрого таймера в панели — свой у каждой строки, независимо от главного таймера.</summary>
public sealed class QuickPanelTimer : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private readonly QuickTimerItem _item;
    private DateTime? _endUtc;
    private TimeSpan _left;
    private DateTime? _finishUntil;
    private QuickPanelState _state;

    internal QuickPanelTimer(MainViewModel owner, QuickTimerItem item)
    {
        _owner = owner;
        _item = item;
        _left = Total;
        for (int i = 0; i < 10; i++) Segments.Add(new PanelSegment());
        PlayPauseCommand = new RelayCommand(PlayPause);
        ResetCommand = new RelayCommand(() => { bool finished = _state == QuickPanelState.Finished; Reset(); if (finished) AfterFinish(); });
    }

    private QuickTimer Model => _item.Model;
    private TimeSpan Total => Model.Duration;

    public string Name => _item.Name;
    public string Color => Model.PanelColor;
    public QuickPanelProgress Progress => Model.PanelProgress;
    public QuickPanelFinish Finish => Model.PanelFinish;

    public QuickPanelState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            Raise(nameof(State)); Raise(nameof(IsRunning)); Raise(nameof(IsFinishing)); Raise(nameof(PlayToolTip));
        }
    }

    public bool IsRunning => _state == QuickPanelState.Running;
    public bool IsFinishing => _state == QuickPanelState.Finished;
    internal bool NeedsTicks => _state == QuickPanelState.Running || _state == QuickPanelState.Finished;

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand ResetCommand { get; }

    public string PlayToolTip => _state switch
    {
        QuickPanelState.Running => "Клик ЛКМ — пауза",
        QuickPanelState.Paused => "Клик ЛКМ — продолжить",
        _ => "Клик ЛКМ — запустить",
    };

    /// <summary>Остаток (в покое — всё время таймера).</summary>
    public TimeSpan Remaining => _state == QuickPanelState.Running && _endUtc is { } end ? Max0(end - DateTime.UtcNow) : _left;

    /// <summary>Пройденная доля 0..1 (для эффекта хода).</summary>
    public double Fraction => Total <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining.TotalSeconds / Total.TotalSeconds, 0, 1);
    public double RemainingFraction => 1 - Fraction;

    /// <summary>Время только с нужными разделами: 40 секунд — «40», 5 минут — «05:00», с днями — «2:03:00:00».</summary>
    public string TimeText => Format(Remaining);
    public string GhostText => System.Text.RegularExpressions.Regex.Replace(TimeText, "[0-9]", "8");

    /// <summary>Десять делений полоски («деления»): горят пройденные.</summary>
    public List<PanelSegment> Segments { get; } = new();

    private int Sections => Model.Days > 0 ? 4 : Model.Hours > 0 ? 3 : Model.Minutes > 0 ? 2 : 1;

    private string Format(TimeSpan t)
    {
        t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds - 0.001)); // как у главного таймера: 40 в начале, 1 в последнюю секунду
        int s = Sections;
        var parts = new List<string>();
        if (s >= 4) parts.Add(((int)t.TotalDays).ToString());
        if (s >= 3) parts.Add((s >= 4 ? t.Hours : (int)t.TotalHours).ToString("00"));
        if (s >= 2) parts.Add((s >= 3 ? t.Minutes : (int)t.TotalMinutes).ToString("00"));
        parts.Add((s >= 2 ? t.Seconds : (int)t.TotalSeconds).ToString("00"));
        return string.Join(":", parts);
    }

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    private void PlayPause()
    {
        switch (_state)
        {
            case QuickPanelState.Running:
                _left = Remaining;
                _endUtc = null;
                State = QuickPanelState.Paused;
                break;
            case QuickPanelState.Paused:
                _endUtc = DateTime.UtcNow + _left;
                State = QuickPanelState.Running;
                break;
            default: // в покое или после окончания — заново на всё время
                if (Total <= TimeSpan.Zero) return;
                _finishUntil = null;
                _left = Total;
                _endUtc = DateTime.UtcNow + Total;
                State = QuickPanelState.Running;
                break;
        }
        RaiseTime();
        _owner.UpdatePanelTicker();
    }

    internal void Reset()
    {
        _endUtc = null;
        _finishUntil = null;
        _left = Total;
        State = QuickPanelState.Ready;
        RaiseTime();
        _owner.UpdatePanelTicker();
    }

    /// <summary>Строку убрали из панели — отсчёт останавливается.</summary>
    internal void Stop() => Reset();

    /// <summary>Время таймера поменяли в настройках — в покое сразу показать новое.</summary>
    internal void OnDurationChanged()
    {
        if (_state == QuickPanelState.Ready) _left = Total;
        RaiseTime();
    }

    internal void Tick(DateTime now)
    {
        if (_state == QuickPanelState.Running && _endUtc is { } end)
        {
            if (now >= end)
            {
                _endUtc = null;
                _left = TimeSpan.Zero;
                _finishUntil = _owner.PanelFinishDuration is { } d && Model.PanelFinish != QuickPanelFinish.None ? now + d
                             : Model.PanelFinish == QuickPanelFinish.None ? now : null;
                State = QuickPanelState.Finished;
                _owner.OnPanelTimerFinished(Model);
            }
            RaiseTime();
        }
        else if (_state == QuickPanelState.Finished && _finishUntil is { } until && now >= until)
        {
            Reset();
            AfterFinish();
        }
    }

    /// <summary>Эффект окончания отыграл (или нажали сброс): «сбросить и закрыть» — убрать из панели.</summary>
    private void AfterFinish()
    {
        if (Model.PanelResetAndClose) _item.ShowInPanel = false;
    }

    private void RaiseTime()
    {
        Raise(nameof(Remaining)); Raise(nameof(TimeText)); Raise(nameof(GhostText)); Raise(nameof(Fraction)); Raise(nameof(RemainingFraction));
        int lit = (int)Math.Floor(Fraction * Segments.Count + 0.0001);
        for (int i = 0; i < Segments.Count; i++) Segments[i].IsLit = i < lit;
    }

    /// <summary>Имя, цвет, эффекты поменяли в окне «Быстрые таймеры».</summary>
    internal void RaiseLook()
    {
        Raise(nameof(Name)); Raise(nameof(Color)); Raise(nameof(Progress)); Raise(nameof(Finish));
        RaiseTime();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Деление полоски «деления».</summary>
public sealed class PanelSegment : INotifyPropertyChanged
{
    private bool _isLit;
    public bool IsLit
    {
        get => _isLit;
        set { if (_isLit != value) { _isLit = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLit))); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

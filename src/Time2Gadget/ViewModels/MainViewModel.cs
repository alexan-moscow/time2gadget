using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Единственная ViewModel приложения (docs/ARCHITECTURE.md → MVVM-слои). Обслуживает и главное
/// окно, и окно настроек (SettingsWindow биндится на этот же экземпляр) — так изменения в
/// настройках мгновенно отражаются в рантайме без отдельного синхронизирующего механизма.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);
    private const int UiTickIntervalMs = 150;

    private readonly ITimerEngine _engine;
    private readonly ISettingsService _settingsService;
    private readonly ISoundService _soundService;
    private readonly ITrayService _trayService;
    private readonly DispatcherTimer _uiTimer;
    private readonly AppSettings _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Событие "закрыть окно совсем" (пункт трея "Выход") — обрабатывается View/App.</summary>
    public event EventHandler? ExitRequested;
    /// <summary>Событие "показать/восстановить окно" (пункт трея "Показать таймер"/двойной клик).</summary>
    public event EventHandler? ShowRequested;
    /// <summary>Событие "открыть настройки" (шестерёнка в окне / пункт трея) — обрабатывается MainWindow.</summary>
    public event EventHandler? SettingsRequested;

    public ObservableCollection<TimerPreset> Presets { get; } = new(TimerPreset.All);
    public IReadOnlyList<AudioDeviceInfo> AudioDevices { get; }

    public IReadOnlyList<EnumOption<RingtoneChoice>> RingtoneOptions { get; } = new[]
    {
        new EnumOption<RingtoneChoice>(RingtoneChoice.ClassicBell, "Классический звонок"),
        new EnumOption<RingtoneChoice>(RingtoneChoice.DigitalBeep, "Цифровой сигнал"),
        new EnumOption<RingtoneChoice>(RingtoneChoice.SoftChime, "Мягкий перезвон"),
        new EnumOption<RingtoneChoice>(RingtoneChoice.AlarmBuzz, "Будильник"),
        new EnumOption<RingtoneChoice>(RingtoneChoice.Custom, "Свой файл…"),
    };

    public IReadOnlyList<EnumOption<RunningVisualEffect>> RunningEffectOptions { get; } = new[]
    {
        new EnumOption<RunningVisualEffect>(RunningVisualEffect.None, "Отключено"),
        new EnumOption<RunningVisualEffect>(RunningVisualEffect.Pulse, "Пульсация"),
        new EnumOption<RunningVisualEffect>(RunningVisualEffect.Flash, "Вспышка акцентом"),
        new EnumOption<RunningVisualEffect>(RunningVisualEffect.ColorBreathe, "Дыхание цветом"),
    };

    public IReadOnlyList<EnumOption<FinishVisualEffect>> FinishEffectOptions { get; } = new[]
    {
        new EnumOption<FinishVisualEffect>(FinishVisualEffect.None, "Отключено"),
        new EnumOption<FinishVisualEffect>(FinishVisualEffect.Pulse, "Пульсация"),
        new EnumOption<FinishVisualEffect>(FinishVisualEffect.Flash, "Строб-вспышка"),
        new EnumOption<FinishVisualEffect>(FinishVisualEffect.ColorCycle, "Цветовая волна"),
    };

    public IReadOnlyList<EnumOption<CloseBehavior>> CloseBehaviorOptions { get; } = new[]
    {
        new EnumOption<CloseBehavior>(CloseBehavior.MinimizeToTray, "Сворачивать в трей"),
        new EnumOption<CloseBehavior>(CloseBehavior.Exit, "Закрывать приложение"),
    };

    public MainViewModel(ITimerEngine engine, ISettingsService settingsService, ISoundService soundService, ITrayService trayService)
    {
        _engine = engine;
        _settingsService = settingsService;
        _soundService = soundService;
        _trayService = trayService;

        _settings = _settingsService.Load();
        AudioDevices = _soundService.GetOutputDevices();

        _isMuted = _settings.IsMuted;
        _isAlwaysOnTop = _settings.AlwaysOnTop;
        _isCompactMode = _settings.CompactMode;
        _isLaunchAtStartup = _settings.LaunchAtStartup; // App.xaml.cs may reconcile this against the real registry state right after construction
        _closeBehavior = _settings.CloseBehavior;
        _alarmVolume = _settings.AlarmVolume;
        _selectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == _settings.AudioDeviceId) ?? AudioDeviceInfo.SystemDefault;
        _selectedRingtone = _settings.SelectedRingtone;
        _customSoundFilePath = _settings.CustomSoundFilePath;
        _alarmRepeatCount = _settings.AlarmRepeatCount;
        _runningEffect = _settings.RunningEffect;
        _finishEffect = _settings.FinishEffect;

        // Подсвечиваем последний использованный сектор, но НЕ скармливаем его время в engine —
        // при запуске приложения дисплей должен показывать 00:00 (docs/DECISIONS.md, 2026-09-27),
        // а не длительность последнего пресета. Отсчёт начинается только явным кликом по сектору.
        var lastMinutes = _settings.LastPresetMinutes;
        SelectedPreset = Presets.FirstOrDefault(p => p.Minutes == lastMinutes) ?? Presets[4]; // fallback: 30 мин

        _engine.Finished += OnEngineFinished;
        _engine.StatusChanged += (_, _) => RaiseStatusDependentChanges();

        SelectPresetCommand = new RelayCommand(p => SelectPreset((TimerPreset)p!));
        StartPauseCommand = new RelayCommand(StartPause);
        ResetCommand = new RelayCommand(ResetTimer);
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        AdjustTimeCommand = new RelayCommand(p => AdjustTime((int)p!));
        ToggleAlwaysOnTopCommand = new RelayCommand(() => IsAlwaysOnTop = !IsAlwaysOnTop);
        ToggleCompactModeCommand = new RelayCommand(() => IsCompactMode = !IsCompactMode);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        BrowseCustomSoundCommand = new RelayCommand(BrowseCustomSound);
        PreviewRingtoneCommand = new RelayCommand(() => _soundService.PlayPreview(_settings));
        OpenGitHubCommand = new RelayCommand(() => OpenUrl(_settings.GitHubUrl));
        OpenVirusTotalCommand = new RelayCommand(() => OpenUrl(_settings.VirusTotalUrl));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));

        _trayService.ShowRequested += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _trayService.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _trayService.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(UiTickIntervalMs)
        };
        _uiTimer.Tick += (_, _) => RefreshFromEngine();
        _uiTimer.Start();

        RefreshFromEngine();
    }

    // ============ Команды ============
    public RelayCommand SelectPresetCommand { get; }
    public RelayCommand StartPauseCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand AdjustTimeCommand { get; }
    public RelayCommand ToggleAlwaysOnTopCommand { get; }
    public RelayCommand ToggleCompactModeCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand BrowseCustomSoundCommand { get; }
    public RelayCommand PreviewRingtoneCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public RelayCommand OpenVirusTotalCommand { get; }
    public RelayCommand ExitCommand { get; }

    // ============ Состояние для биндингов ============
    private TimerPreset? _selectedPreset;
    public TimerPreset? SelectedPreset
    {
        get => _selectedPreset;
        private set { _selectedPreset = value; OnPropertyChanged(); }
    }

    private TimeSpan _remainingTime;
    public TimeSpan RemainingTime
    {
        get => _remainingTime;
        private set { _remainingTime = value; OnPropertyChanged(); }
    }

    private double _progressFraction;
    public double ProgressFraction
    {
        get => _progressFraction;
        private set { _progressFraction = value; OnPropertyChanged(); }
    }

    private bool _isColonLit = true;
    /// <summary>Двоеточие сегментного циферблата: при отсчёте мигает раз в секунду (горит в первой
    /// половине каждой секунды оставшегося времени), в остальных состояниях горит постоянно.</summary>
    public bool IsColonLit
    {
        get => _isColonLit;
        private set { if (_isColonLit == value) return; _isColonLit = value; OnPropertyChanged(); }
    }

    public TimerStatus Status => _engine.Status;

    public string StatusLabel => Status switch
    {
        TimerStatus.Ready => "ГОТОВ",
        TimerStatus.Running => "ОСТАЛОСЬ",
        TimerStatus.Paused => "ПАУЗА",
        TimerStatus.Finished => "ГОТОВО",
        _ => string.Empty
    };

    public bool IsRunning => Status == TimerStatus.Running;
    public bool IsFinished => Status == TimerStatus.Finished;

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            _settings.IsMuted = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private bool _isAlwaysOnTop;
    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set
        {
            if (_isAlwaysOnTop == value) return;
            _isAlwaysOnTop = value;
            _settings.AlwaysOnTop = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private bool _isCompactMode;
    public bool IsCompactMode
    {
        get => _isCompactMode;
        set
        {
            if (_isCompactMode == value) return;
            _isCompactMode = value;
            _settings.CompactMode = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private bool _isLaunchAtStartup;
    public bool IsLaunchAtStartup
    {
        get => _isLaunchAtStartup;
        set
        {
            if (_isLaunchAtStartup == value) return;
            _isLaunchAtStartup = value;
            _settings.LaunchAtStartup = value;
            _settingsService.Save(_settings);
            AutostartService.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    private CloseBehavior _closeBehavior;
    /// <summary>Поведение при закрытии окна — сворачивать (по умолчанию) или закрывать приложение.</summary>
    public CloseBehavior CloseBehavior
    {
        get => _closeBehavior;
        set
        {
            if (_closeBehavior == value) return;
            _closeBehavior = value;
            _settings.CloseBehavior = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private double _alarmVolume;
    /// <summary>Громкость звонка 0..1 — тонкий слайдер под кнопкой Mute (docs/UI-CONTRACT.md).</summary>
    public double AlarmVolume
    {
        get => _alarmVolume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_alarmVolume - clamped) < 0.001) return;
            _alarmVolume = clamped;
            _settings.AlarmVolume = clamped;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private AudioDeviceInfo _selectedAudioDevice;
    public AudioDeviceInfo SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set
        {
            if (_selectedAudioDevice == value) return;
            _selectedAudioDevice = value;
            _settings.AudioDeviceId = value.Id;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private RingtoneChoice _selectedRingtone;
    public RingtoneChoice SelectedRingtone
    {
        get => _selectedRingtone;
        set
        {
            if (_selectedRingtone == value) return;
            _selectedRingtone = value;
            _settings.SelectedRingtone = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomRingtoneSelected));
        }
    }

    public bool IsCustomRingtoneSelected => SelectedRingtone == RingtoneChoice.Custom;

    private string? _customSoundFilePath;
    public string? CustomSoundFilePath
    {
        get => _customSoundFilePath;
        set
        {
            if (_customSoundFilePath == value) return;
            _customSoundFilePath = value;
            _settings.CustomSoundFilePath = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private int _alarmRepeatCount;
    /// <summary>Сколько раз звонить при завершении (1..10, не бесконечно) — докладка 2026-09-27,
    /// заменяет прежнюю пару Duration/Interval, которая не была привязана к реальной длине звука.</summary>
    public int AlarmRepeatCount
    {
        get => _alarmRepeatCount;
        set
        {
            var clamped = Math.Clamp(value, 1, 10);
            if (_alarmRepeatCount == clamped) return;
            _alarmRepeatCount = clamped;
            _settings.AlarmRepeatCount = clamped;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private RunningVisualEffect _runningEffect;
    /// <summary>Визуальный эффект циферблата во время отсчёта. По умолчанию None.</summary>
    public RunningVisualEffect RunningEffect
    {
        get => _runningEffect;
        set
        {
            if (_runningEffect == value) return;
            _runningEffect = value;
            _settings.RunningEffect = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    private FinishVisualEffect _finishEffect;
    /// <summary>Визуальный эффект циферблата при завершении (пока звонит будильник). По умолчанию None.</summary>
    public FinishVisualEffect FinishEffect
    {
        get => _finishEffect;
        set
        {
            if (_finishEffect == value) return;
            _finishEffect = value;
            _settings.FinishEffect = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    // "Об авторе" — только отображение, тестовые плейсхолдеры (docs/DECISIONS.md, 2026-09-27).
    public string AuthorName => _settings.AuthorName;
    public string GitHubUrl => _settings.GitHubUrl;
    public string VirusTotalUrl => _settings.VirusTotalUrl;

    public double? InitialWindowLeft => _settings.WindowLeft;
    public double? InitialWindowTop => _settings.WindowTop;

    /// <summary>Вызывается View при закрытии/перемещении окна — персистит позицию.</summary>
    public void UpdateWindowPosition(double left, double top)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settingsService.Save(_settings);
    }

    public void InitializeTray()
    {
        _trayService.Initialize();
    }

    // ============ Подсказки (Controls/HintService.cs, docs/UI-CONTRACT.md → Подсказки) ============

    /// <summary>
    /// Текст подсказки по ключу элемента — описывает, что произойдёт при действии ИМЕННО СЕЙЧАС
    /// (зависит от состояния таймера), поэтому живёт рядом с логикой команд, а не в XAML.
    /// </summary>
    public string? GetHint(string key, object? tag = null) => key switch
    {
        "Preset" when tag is TimerPreset preset => GetPresetHint(preset),
        "StartPause" => Status switch
        {
            TimerStatus.Running => "Пауза  (Пробел)",
            TimerStatus.Paused => "Продолжить отсчёт  (Пробел)",
            TimerStatus.Finished => "Запустить этот таймер ещё раз  (Пробел)",
            _ => _engine.TotalDuration > TimeSpan.Zero
                ? "Запустить отсчёт  (Пробел)"
                : SelectedPreset is not null
                    ? $"Запустить таймер на {FormatMinutes(SelectedPreset.Minutes)}  (Пробел)"
                    : "Сначала выберите время на кольце"
        },
        "Reset" => Status == TimerStatus.Finished
            ? "Выключить звонок и сбросить на 00:00  (R)"
            : "Сбросить таймер на 00:00  (R)",
        "Center" => Status is TimerStatus.Running or TimerStatus.Paused
            ? "Клик — компактный режим\nПотяните — переместить окно"
            : "Клик — компактный режим\nКолесо мыши — ±1 минута",
        "CompactCenter" => "Клик — вернуться к полному виду",
        "Mute" => IsMuted ? "Включить звук звонка" : "Выключить звук звонка",
        "Volume" => $"Громкость звонка: {Math.Round(AlarmVolume * 100)}%",
        "Settings" => "Настройки",
        "Close" => CloseBehavior == CloseBehavior.Exit ? "Закрыть приложение" : "Свернуть в трей",
        _ => null
    };

    /// <summary>Повторяет ветки <see cref="SelectPreset"/> — подсказка обязана совпадать с тем, что сделает клик.</summary>
    private string GetPresetHint(TimerPreset preset)
    {
        var label = FormatMinutes(preset.Minutes);
        if (Status is TimerStatus.Ready or TimerStatus.Finished)
            return $"Запустить таймер на {label}";
        if (SelectedPreset is not null && SelectedPreset.Equals(preset))
            return $"Добавить {label} к текущему таймеру";
        return $"Сбросить и запустить заново на {label}";
    }

    private static string FormatMinutes(int minutes) => minutes switch
    {
        60 => "1 час",
        90 => "1,5 часа",
        _ => $"{minutes} минут"
    };

    // ============ Логика ============

    /// <summary>
    /// Клик по сектору (docs/UI-CONTRACT.md → SectorRingControl → Логика клика, обновлено 2026-09-27).
    /// Ready/Finished → сектор сразу выбирает время И запускает отсчёт (по явному требованию).
    /// Running/Paused → повторный клик на УЖЕ активный сектор добавляет его время к текущему
    /// отсчёту (докладка, как в классических кухонных таймерах); клик на ДРУГОЙ сектор во время
    /// отсчёта — сброс и немедленный запуск с временем нового сектора (докладка 2026-09-27:
    /// пользователь ожидает замены, а не игнорирования клика).
    /// </summary>
    private void SelectPreset(TimerPreset preset)
    {
        if (Status is TimerStatus.Ready or TimerStatus.Finished)
        {
            SelectedPreset = preset;
            _settings.LastPresetMinutes = preset.Minutes;
            _settingsService.Save(_settings);
            _soundService.StopAlarm();
            _engine.SetDuration(TimeSpan.FromMinutes(preset.Minutes));
            _engine.Start();
            RaiseStatusDependentChanges();
        }
        else if (SelectedPreset is not null && SelectedPreset.Equals(preset))
        {
            _engine.AddDuration(TimeSpan.FromMinutes(preset.Minutes));
            RefreshFromEngine();
        }
        else
        {
            SelectedPreset = preset;
            _settings.LastPresetMinutes = preset.Minutes;
            _settingsService.Save(_settings);
            _soundService.StopAlarm();
            _engine.Restart(TimeSpan.FromMinutes(preset.Minutes));
            RaiseStatusDependentChanges();
        }
    }

    private void StartPause()
    {
        if (Status == TimerStatus.Running)
        {
            _engine.Pause();
        }
        else
        {
            _soundService.StopAlarm();
            // После сброса (и при запуске приложения) на экране 00:00 — Play запускает подсвеченный
            // на кольце пресет (докладка 2026-09-27: «после сброса Play не запускает текущий таймер»).
            if (Status == TimerStatus.Ready && _engine.TotalDuration <= TimeSpan.Zero && SelectedPreset is not null)
                _engine.SetDuration(TimeSpan.FromMinutes(SelectedPreset.Minutes));
            _engine.Start();
        }
        RaiseStatusDependentChanges();
    }

    private void ResetTimer()
    {
        _soundService.StopAlarm();
        _engine.Reset();
        RaiseStatusDependentChanges();
    }

    /// <summary>Колесо мыши над центром (docs/UI-CONTRACT.md). +1/-1 минута, только когда время можно менять.</summary>
    private void AdjustTime(int deltaMinutes)
    {
        if (Status is TimerStatus.Running or TimerStatus.Paused) return;

        var newDuration = _engine.TotalDuration + TimeSpan.FromMinutes(deltaMinutes);
        if (newDuration < MinDuration) newDuration = MinDuration;
        if (newDuration > MaxDuration) newDuration = MaxDuration;

        _engine.SetDuration(newDuration);

        // Подсветка сектора актуальна, только если новое значение совпадает с одним из пресетов.
        SelectedPreset = Presets.FirstOrDefault(p => p.Minutes == newDuration.TotalMinutes);
        if (SelectedPreset is not null)
        {
            _settings.LastPresetMinutes = SelectedPreset.Minutes;
            _settingsService.Save(_settings);
        }

        RefreshFromEngine();
    }

    private void BrowseCustomSound()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите звуковой файл",
            Filter = "Аудио файлы (*.wav;*.mp3)|*.wav;*.mp3|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            CustomSoundFilePath = dialog.FileName;
            SelectedRingtone = RingtoneChoice.Custom;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Нет браузера/некорректная ссылка (плейсхолдер) — не ронять приложение.
        }
    }

    private void OnEngineFinished(object? sender, EventArgs e)
    {
        if (!IsMuted) _soundService.PlayAlarm(_settings);
        RaiseStatusDependentChanges();
    }

    private void RefreshFromEngine()
    {
        RemainingTime = _engine.GetRemaining();
        IsColonLit = Status != TimerStatus.Running || RemainingTime.Milliseconds >= 500;
        var total = _engine.TotalDuration;
        ProgressFraction = total.TotalSeconds <= 0
            ? 0
            : 1 - (RemainingTime.TotalSeconds / total.TotalSeconds);
    }

    private void RaiseStatusDependentChanges()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
        RefreshFromEngine();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _uiTimer.Stop();
        _soundService.StopAlarm();
        _trayService.Dispose();
    }
}

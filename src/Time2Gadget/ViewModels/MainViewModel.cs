using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);
    private const int UiTickIntervalMs = 150;

    private readonly ITimerEngine _engine;
    private readonly ISettingsService _settingsService;
    private readonly ISoundService _soundService;
    private readonly ITrayService _trayService;
    private readonly IUpdateService _updateService;
    private readonly PowerService _power = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly AppSettings _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Событие "закрыть окно совсем" (пункт трея "Выход") — обрабатывается View/App.</summary>
    public event EventHandler? ExitRequested;
    /// <summary>Событие "показать/восстановить окно" (пункт трея "Показать таймер"/двойной клик).</summary>
    public event EventHandler? ShowRequested;
    /// <summary>Событие "открыть настройки" (шестерёнка в окне / пункт трея) — обрабатывается MainWindow.</summary>
    public event EventHandler? SettingsRequested;
    /// <summary>Автозакрытие после отработавшего таймера — View выполняет как обычное закрытие окна
    /// (свернуть в трей или выйти — по <see cref="CloseBehavior"/>).</summary>
    public event EventHandler? AutoCloseRequested;

    /// <summary>Сколько ждать после окончания, если звонка нет (выключен звук) — чтобы финал успели увидеть.</summary>
    private static readonly TimeSpan SilentFinishAutoCloseDelay = TimeSpan.FromSeconds(3);
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private DispatcherTimer? _silentAutoCloseTimer;

    public ObservableCollection<TimerPreset> Presets { get; } = new(TimerPreset.All);
    public IReadOnlyList<AudioDeviceInfo> AudioDevices { get; }

    public IReadOnlyList<EnumOption<CloseBehavior>> CloseBehaviorOptions { get; } = new[]
    {
        new EnumOption<CloseBehavior>(CloseBehavior.MinimizeToTray, "Сворачивать в трей"),
        new EnumOption<CloseBehavior>(CloseBehavior.Exit, "Закрывать приложение"),
    };

    public MainViewModel(ITimerEngine engine, ISettingsService settingsService, ISoundService soundService, ITrayService trayService,
        IUpdateService updateService)
    {
        _engine = engine;
        _settingsService = settingsService;
        _soundService = soundService;
        _trayService = trayService;
        _updateService = updateService;

        _settings = _settingsService.Load();
        AudioDevices = _soundService.GetOutputDevices();

        _selectedRingtoneId = RingtoneCatalog.DefaultId;
        _selectedAudioDevice = AudioDeviceInfo.SystemDefault;
        LoadCachedFromSettings();

        _engine.Finished += OnEngineFinished;
        _soundService.PreviewEnded += OnPreviewEnded;
        LoadBuiltInDurations();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AlarmVolume)) foreach (var q in QuickTimers) q.OnGeneralVolumeChanged(); // галочка «общая» — ползунок следует
        };
        _soundService.AlarmCompleted += (_, _) => _dispatcher.BeginInvoke(() => { _alarmPlaying = false; TryAutoClose(); RefreshFinishEffectActive(); });
        _engine.StatusChanged += (_, _) => RaiseStatusDependentChanges();

        SelectPresetCommand = new RelayCommand(p => SelectPreset((TimerPreset)p!));
        StartPauseCommand = new RelayCommand(StartPause);
        ResetCommand = new RelayCommand(ResetTimer);
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        AdjustTimeCommand = new RelayCommand(p => AdjustTime((int)p!));
        ToggleAlwaysOnTopCommand = new RelayCommand(() => IsAlwaysOnTop = !IsAlwaysOnTop);
        ToggleCompactModeCommand = new RelayCommand(() => IsCompactMode = !IsCompactMode);
        ToggleAutoCloseCommand = new RelayCommand(() => AutoCloseAfterFinish = !AutoCloseAfterFinish);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        BrowseCustomSoundCommand = new RelayCommand(BrowseCustomSound);
        // Параметр — Id конкретного звонка (кнопка ▶ в строке выпадающего списка); без параметра — выбранный.
        PreviewRingtoneCommand = new RelayCommand(PreviewFromSoundSection);
        OpenGitHubCommand = new RelayCommand(() => OpenUrl(GitHubUrl));
        // Отчёт VirusTotal по установщику ИМЕННО этой версии (хэш — из GitHub Release); нет связи — страница выпусков.
        OpenVirusTotalCommand = new RelayCommand(async () => OpenUrl(await _updateService.GetVirusTotalUrlAsync() ?? GitHubUrl));
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

        InitializeUpdates();

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
    public RelayCommand ToggleAutoCloseCommand { get; }
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

    private bool _autoCloseAfterFinish;
    /// <summary>Переключатель слева от крестика — см. <see cref="AppSettings.AutoCloseAfterFinish"/>.</summary>
    public bool AutoCloseAfterFinish
    {
        get => _autoCloseAfterFinish;
        set
        {
            if (_autoCloseAfterFinish == value) return;
            _autoCloseAfterFinish = value;
            _settings.AutoCloseAfterFinish = value;
            if (value) SetAutoCompact(false); // режимы кнопки взаимоисключающие
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Левый щелчок по той же кнопке — после окончания перейти в компактный вид (докладка 2026-09-28).</summary>
    public bool AutoCompactAfterFinish
    {
        get => _settings.AutoCompactAfterFinish;
        set
        {
            if (_settings.AutoCompactAfterFinish == value) return;
            SetAutoCompact(value);
            if (value) AutoCloseAfterFinish = false;
            _settingsService.Save(_settings);
        }
    }

    private void SetAutoCompact(bool value)
    {
        if (_settings.AutoCompactAfterFinish == value) return;
        _settings.AutoCompactAfterFinish = value;
        OnPropertyChanged(nameof(AutoCompactAfterFinish));
    }

    public RelayCommand ToggleAutoCompactCommand => _toggleAutoCompactCommand ??= new RelayCommand(() => AutoCompactAfterFinish = !AutoCompactAfterFinish);
    private RelayCommand? _toggleAutoCompactCommand;

    private bool _isWindowHidden;
    /// <summary>Окно скрыто (свёрнуто в трей) — тогда законченный таймер мигает иконкой трея. Ставит View.</summary>
    public bool IsWindowHidden
    {
        get => _isWindowHidden;
        set { if (_isWindowHidden == value) return; _isWindowHidden = value; UpdateTray(); }
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

    private string _selectedRingtoneId;
    public string SelectedRingtoneId
    {
        get => _selectedRingtoneId;
        set
        {
            if (_selectedRingtoneId == value || value is null) return;
            _selectedRingtoneId = value;
            _settings.RingtoneId = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomRingtoneSelected));
        }
    }

    public bool IsCustomRingtoneSelected => SelectedRingtoneId == RingtoneCatalog.CustomId;

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
            UpdateCustomOption(RingtoneOptions, value);
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
            OnPropertyChanged(nameof(ActiveFinishEffect));
            RefreshFinishEffectActive();
        }
    }

    // ============ Обновления (docs/DECISIONS.md, 2026-09-27; расписание изменено 2026-09-28) ============
    // Автопроверка при каждом запуске (в т.ч. автозапуск после перезагрузки) — через минуту, когда сеть уже поднялась, —
    // и дальше раз в сутки: каждый час смотрим, прошли ли сутки с последней УСПЕШНОЙ проверки (дата — в settings.json).
    // Неудачная (нет сети) дату не двигает — повтор в течение часа. Проверка тихая: нашлось — баннер в настройках и
    // жёлтое мигание шестерёнки; ошибка сети — ничего не показываем.
    private static readonly TimeSpan UpdateCheckPeriod = TimeSpan.FromDays(1);
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UpdateScheduleTick = TimeSpan.FromHours(1);
    private bool _startupUpdateCheckDone;
    private DispatcherTimer? _updateTimer;
    private string? _availableReleasePageUrl;

    public RelayCommand CheckUpdatesCommand { get; private set; } = null!;
    public RelayCommand InstallUpdateCommand { get; private set; } = null!;

    /// <summary>«Версия 1.0.5 · сентябрь 2026» — дата сборки справа от версии (докладка 2026-09-28; раньше была в «Об авторе»).</summary>
    public string CurrentVersionText => ReleaseMonth is { } month
        ? $"Версия {_updateService.CurrentVersion} · {month}"
        : $"Версия {_updateService.CurrentVersion}";

    private string? _availableVersion;
    public string? AvailableVersion
    {
        get => _availableVersion;
        private set
        {
            if (_availableVersion == value) return;
            _availableVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(IsUpdateNotAvailable));
            OnPropertyChanged(nameof(InstallUpdateButtonText));
        }
    }

    public bool IsUpdateAvailable => AvailableVersion is not null;
    public bool IsUpdateNotAvailable => !IsUpdateAvailable;

    /// <summary>
    /// Кнопка на месте «Проверить обновления», когда версия найдена (докладка 2026-09-27). Установленная копия
    /// обновляется на месте; одиночный exe — только ссылкой на страницу выпуска.
    /// </summary>
    public string InstallUpdateButtonText =>
        (_updateService.CanInstallInPlace ? "Установить версию " : "Скачать версию ") + AvailableVersion;

    private string _updateStatusText = string.Empty;
    /// <summary>Строка рядом с кнопкой «Проверить обновления» — результат ручной проверки/ход установки.</summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set { if (_updateStatusText == value) return; _updateStatusText = value; OnPropertyChanged(); }
    }

    private bool _isUpdateBusy;
    private bool IsUpdateBusy
    {
        get => _isUpdateBusy;
        set { _isUpdateBusy = value; CommandManager.InvalidateRequerySuggested(); }
    }

    private void InitializeUpdates()
    {
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(manual: true), () => !IsUpdateBusy);
        InstallUpdateCommand = new RelayCommand(() => _ = InstallUpdateAsync(), () => IsUpdateAvailable && !IsUpdateBusy);

        _updateTimer = new DispatcherTimer { Interval = FirstUpdateCheckDelay };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateScheduleTick;
            var last = _settings.LastUpdateCheckUtc;
            bool startup = !_startupUpdateCheckDone; // первая проверка после запуска — всегда
            _startupUpdateCheckDone = true;
            if (startup || last is null || DateTime.UtcNow - last.Value >= UpdateCheckPeriod)
                _ = CheckUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (IsUpdateBusy) return;
        IsUpdateBusy = true;
        if (manual) UpdateStatusText = "Проверяем…";
        try
        {
            var result = await _updateService.CheckAsync();
            if (result.Status != UpdateCheckStatus.Failed)
            {
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settingsService.Save(_settings);
            }

            switch (result.Status)
            {
                case UpdateCheckStatus.Available:
                    _availableReleasePageUrl = result.ReleasePageUrl;
                    AvailableVersion = result.Version;
                    UpdateStatusText = $"Найдена версия {result.Version}"; // и при автопроверке — рядом с кнопкой установки
                    break;
                case UpdateCheckStatus.UpToDate:
                    AvailableVersion = null;
                    if (manual) UpdateStatusText = "Установлена последняя версия";
                    break;
                default:
                    if (manual) UpdateStatusText = "Не удалось проверить — нет связи с GitHub";
                    break;
            }
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (!_updateService.CanInstallInPlace)
        {
            if (_availableReleasePageUrl is not null) OpenUrl(_availableReleasePageUrl); // портативная копия — страница выпуска
            return;
        }

        if (Status is TimerStatus.Running or TimerStatus.Paused
            && System.Windows.MessageBox.Show("Идёт отсчёт таймера. Обновление перезапустит программу и сбросит таймер. Продолжить?",
                   "Тайм2гаджет", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
               != System.Windows.MessageBoxResult.Yes)
            return;

        IsUpdateBusy = true;
        UpdateStatusText = "Скачиваем обновление…";
        try
        {
            _soundService.StopAlarm();
            await _updateService.DownloadAndRestartAsync(p => _dispatcher.BeginInvoke(() => UpdateStatusText = $"Скачиваем обновление… {p}%"));
            // сюда при успехе не доходим: процесс завершается и Velopack запускает новую версию
        }
        catch
        {
            UpdateStatusText = "Не удалось установить обновление — попробуйте позже";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    // ============ Часы под статусом (докладка 2026-09-27) ============
    public bool ShowClock
    {
        get => _settings.ShowClock;
        set
        {
            if (_settings.ShowClock == value) return;
            _settings.ShowClock = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClockAreaVisible));
            RefreshIdleClock();
            RaiseCompactLayoutChanges();
        }
    }

    public bool ShowDate
    {
        get => _settings.ShowDate;
        set
        {
            if (_settings.ShowDate == value) return;
            _settings.ShowDate = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClockAreaVisible));
            RaiseCompactLayoutChanges();
        }
    }

    /// <summary>Часы или дата включены — тогда между таймером и ними тонкий разделитель.</summary>
    public bool IsClockAreaVisible => ShowClock || ShowDate;

    // ---- Компакт как виджет часов (докладка 2026-09-27): таймер не запущен (00:00) и часы включены —
    // крупно идёт текущее время вместо нулей, под ним дата с годом (если включена), затем разделитель.
    // Идёт/на паузе/закончился — крупно таймер, под разделителем строка «часы + дата», как раньше. ----
    private bool _isIdleClock;
    public bool IsIdleClock
    {
        get => _isIdleClock;
        private set
        {
            if (_isIdleClock == value) return;
            _isIdleClock = value;
            RaiseCompactLayoutChanges();
        }
    }

    public bool IsTimerDisplay => !IsIdleClock;
    public bool ShowIdleDate => IsIdleClock && ShowDate;

    /// <summary>
    /// Часы в покое без даты — центрируются по вертикали (докладка 2026-09-27: прижатые к полоске хода, они
    /// «прилипали» к ней). С датой/при таймере блок остаётся прижатым к полоске.
    /// </summary>
    public bool CenterCompactDisplay => IsIdleClock && !ShowDate;
    // В покое разделителя нет — его роль играет полоска хода таймера сразу под датой (докладка 2026-09-27).
    public bool ShowCompactSeparator => !IsIdleClock && IsClockAreaVisible;
    public bool ShowCompactClockRow => !IsIdleClock && IsClockAreaVisible;

    private void RefreshIdleClock() =>
        IsIdleClock = ShowClock && Status == TimerStatus.Ready && _engine.TotalDuration <= TimeSpan.Zero;

    private void RaiseCompactLayoutChanges()
    {
        OnPropertyChanged(nameof(IsIdleClock));
        OnPropertyChanged(nameof(IsTimerDisplay));
        OnPropertyChanged(nameof(ShowIdleDate));
        OnPropertyChanged(nameof(CenterCompactDisplay));
        OnPropertyChanged(nameof(ShowCompactSeparator));
        OnPropertyChanged(nameof(ShowCompactClockRow));
    }

    // ---- Сон и мониторы (докладка 2026-09-28, Services/PowerService.cs) ----
    public bool WakeFromSleepOnFinish
    {
        get => _settings.WakeFromSleepOnFinish;
        set
        {
            if (_settings.WakeFromSleepOnFinish == value) return;
            _settings.WakeFromSleepOnFinish = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            UpdateWakeTimer();
        }
    }

    public bool WakeDisplayOnFinish
    {
        get => _settings.WakeDisplayOnFinish;
        set
        {
            if (_settings.WakeDisplayOnFinish == value) return;
            _settings.WakeDisplayOnFinish = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Возвращать окна других программ на мониторы после сна — служба живёт в MainWindow (нужен HWND).</summary>
    public bool RestoreOtherWindows
    {
        get => _settings.RestoreOtherWindows;
        set
        {
            if (_settings.RestoreOtherWindows == value) return;
            _settings.RestoreOtherWindows = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Открывать окна программ там, где их закрыли (Services/AppWindowMemoryService, докладка 2026-09-28).</summary>
    public bool RememberAppWindows
    {
        get => _settings.RememberAppWindows;
        set
        {
            if (_settings.RememberAppWindows == value) return;
            _settings.RememberAppWindows = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Настройки для окна «Профили размера окон» (своя ViewModel — WindowProfilesViewModel) и службы профилей.</summary>
    internal AppSettings Settings => _settings;
    internal void SaveSettings() => _settingsService.Save(_settings);

    /// <summary>Изменились профили или назначения — служба автоприменения и память окон перечитывают их.</summary>
    public event EventHandler? WindowProfilesChanged;
    internal void RaiseWindowProfilesChanged() => WindowProfilesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Профиль, назначенный программе (ключ «exe + вид окна»), или null.</summary>
    internal WindowSizeProfile? FindProfileFor(string programKey) =>
        _settings.WindowProfileAssignments.FirstOrDefault(a => a.ProgramKey == programKey) is { } a
            ? _settings.WindowProfiles.FirstOrDefault(p => p.Name == a.ProfileName)
            : null;


    /// <summary>Нужен перезапуск с правами администратора (задача Планировщика уже создана) — выполняет App.</summary>
    public event EventHandler? RestartElevatedRequested;

    /// <summary>Запуск с правами выключили, а программа работает с ними — перезапуститься с обычными правами (выполняет App).</summary>
    public event EventHandler? RestartNormalRequested;

    /// <summary>Перед перезапуском: открыть ли настройки в новой копии и с какой прокруткой (null — не открывать).</summary>
    public void RememberSettingsForRestart(double? scroll)
    {
        _settings.ReopenSettingsScroll = scroll;
        _settingsService.Save(_settings);
    }

    /// <summary>После запуска: прокрутка настроек, если их надо открыть (поле сразу очищается — только один раз).</summary>
    public double? TakeReopenSettingsScroll()
    {
        if (_settings.ReopenSettingsScroll is not { } scroll) return null;
        _settings.ReopenSettingsScroll = null;
        _settingsService.Save(_settings);
        return scroll;
    }

    /// <summary>
    /// «Запускать с правами администратора» (подпункт возврата окон, 2026-09-28). Вкл — создать задачу
    /// Планировщика (один раз через UAC) и перезапуститься через неё; отказ в UAC — галочка возвращается.
    /// Выкл — удалить задачу; текущий запуск остаётся с правами до перезапуска.
    /// </summary>
    public bool RunElevated
    {
        get => _settings.RunElevated;
        set
        {
            if (_settings.RunElevated == value) return;
            if (value && !ElevationService.CreateTask())
            {
                OnPropertyChanged(); // UAC отклонён/ошибка — галочку вернуть в «выкл»
                System.Windows.MessageBox.Show("Не удалось включить запуск с правами администратора: разрешение не получено.",
                    "Тайм2гаджет", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            if (!value) ElevationService.DeleteTask();

            _settings.RunElevated = value;
            _settings.ElevationTaskExePath = value ? Environment.ProcessPath : null; // для какой копии создана задача
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ElevationStatusText));

            if (value && !ElevationService.IsElevated) RestartElevatedRequested?.Invoke(this, EventArgs.Empty);
            // Сняли галочку, а работаем с правами — сразу перезапуск с обычными (докладка 2026-09-28: раньше права
            // оставались до следующего запуска, и было непонятно, выключилось ли).
            if (!value && ElevationService.IsElevated) RestartNormalRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsRunningElevated => ElevationService.IsElevated;

    public string ElevationStatusText => ElevationService.IsElevated
        ? "Сейчас программа работает с правами администратора."
        : RunElevated && !ElevationService.TaskTargetsThisCopy(_settings.ElevationTaskExePath)
            ? "Сейчас программа работает с обычными правами: запуск с правами настроен для другой копии программы. Снимите и поставьте галочку заново."
            : "Сейчас программа работает с обычными правами.";

    /// <summary>
    /// Таймер пробуждения — только пока идёт отсчёт; на момент окончания (сейчас + осталось). Пауза/сброс/
    /// окончание снимают его; добавление времени — переставляет (PowerService сам игнорирует мелкие расхождения).
    /// </summary>
    private void UpdateWakeTimer() =>
        _power.ScheduleWake(WakeFromSleepOnFinish && Status == TimerStatus.Running
            ? DateTime.UtcNow + RemainingTime
            : null);

    // ---- Размер видов — два ползунка в настройках (докладка 2026-09-27) ----
    public const double MinViewScale = 0.6, MaxViewScale = 1.6; // от 60% (докладка 2026-09-27; было 80%)

    public double FullViewScale
    {
        get => _settings.FullViewScale;
        set
        {
            var v = Math.Round(Math.Clamp(value, MinViewScale, MaxViewScale), 2);
            if (Math.Abs(_settings.FullViewScale - v) < 0.001) return;
            _settings.FullViewScale = v;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FullViewScaleLabel));
        }
    }

    public double CompactViewScale
    {
        get => _settings.CompactViewScale;
        set
        {
            var v = Math.Round(Math.Clamp(value, MinViewScale, MaxViewScale), 2);
            if (Math.Abs(_settings.CompactViewScale - v) < 0.001) return;
            _settings.CompactViewScale = v;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompactViewScaleLabel));
            OnPropertyChanged(nameof(QuickPanelViewScale)); // панель быстрых таймеров — высотой с компактный вид
        }
    }

    public string FullViewScaleLabel => $"Размер полного вида: {Math.Round(FullViewScale * 100)}%";
    public string CompactViewScaleLabel => $"Размер компактного вида: {Math.Round(CompactViewScale * 100)}%";

    /// <summary>«Подложка» погасших сегментов для часов — как у главного циферблата.</summary>
    public string ClockGhostText => ShowClockSeconds ? "88:88:88" : "88:88";

    private string _dateText = string.Empty;
    /// <summary>Дата коротко («СБ, 27 СЕН») — чтобы помещалась в круг под часами.</summary>
    public string DateText
    {
        get => _dateText;
        private set { if (_dateText == value) return; _dateText = value; OnPropertyChanged(); }
    }

    public bool ShowClockSeconds
    {
        get => _settings.ShowClockSeconds;
        set
        {
            if (_settings.ShowClockSeconds == value) return;
            _settings.ShowClockSeconds = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClockGhostText));
            RefreshClock();
        }
    }

    private string _clockText = string.Empty;
    /// <summary>Текущее время для подписи под статусом; обновляется на UI-тике, событие — только при смене текста.</summary>
    public string ClockText
    {
        get => _clockText;
        private set { if (_clockText == value) return; _clockText = value; OnPropertyChanged(); }
    }

    private static readonly System.Globalization.CultureInfo Russian = new("ru-RU");

    private void RefreshClock()
    {
        var now = DateTime.Now;
        ClockText = now.ToString(ShowClockSeconds ? "HH:mm:ss" : "HH:mm");
        // «сб, 27 сен.» → «СБ, 27 СЕН»: заглавными, как «ГОТОВО», и без точки сокращения.
        DateText = now.ToString("ddd, d MMM", Russian).Replace(".", string.Empty).ToUpper(Russian);
        DateDay = now.ToString("dd");
        DateMonth = now.ToString("MM");
        DateYear = now.ToString("yy");
    }

    // Дата для компакта — «ДД . ММ . ГГ» сегментным шрифтом (докладка 2026-09-27: короткий год; части
    // разнесены, слитно «27.09.26» в DSEG читалось плохо). DSEG7 не рисует буквы — только цифры.
    // Части отдельными свойствами: разрядку между ними даёт вёрстка, а не пробелы (пробел в DSEG шириной с цифру).
    private string _dateDay = string.Empty, _dateMonth = string.Empty, _dateYear = string.Empty;
    public string DateDay { get => _dateDay; private set { if (_dateDay == value) return; _dateDay = value; OnPropertyChanged(); } }
    public string DateMonth { get => _dateMonth; private set { if (_dateMonth == value) return; _dateMonth = value; OnPropertyChanged(); } }
    public string DateYear { get => _dateYear; private set { if (_dateYear == value) return; _dateYear = value; OnPropertyChanged(); } }

    /// <summary>Шкала ползунка длительности эффекта завершения, секунды; 0 — бесконечно (последний шаг).</summary>
    public static readonly int[] FinishEffectDurationSteps = { 2, 3, 5, 10, 15, 30, 60, 120, 300, 0 };
    public int FinishEffectDurationMaxIndex => FinishEffectDurationSteps.Length - 1;

    /// <summary>Позиция ползунка (индекс в <see cref="FinishEffectDurationSteps"/>).</summary>
    public int FinishEffectDurationIndex
    {
        get
        {
            int i = Array.IndexOf(FinishEffectDurationSteps, _settings.FinishEffectDurationSeconds);
            return i >= 0 ? i : FinishEffectDurationMaxIndex; // неизвестное значение в settings.json — считаем «бесконечно»
        }
        set
        {
            var seconds = FinishEffectDurationSteps[Math.Clamp(value, 0, FinishEffectDurationMaxIndex)];
            if (_settings.FinishEffectDurationSeconds == seconds) return;
            _settings.FinishEffectDurationSeconds = seconds;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FinishEffectDurationLabel));
            RefreshFinishEffectActive();
        }
    }

    public string FinishEffectDurationLabel => _settings.FinishEffectDurationSeconds switch
    {
        0 => "Длительность эффекта: бесконечно (до сброса)",
        < 60 and var s => $"Длительность эффекта: {s} с",
        var s => $"Длительность эффекта: {s / 60} мин"
    };

    private DateTime? _finishedAtUtc;
    private bool _isFinishEffectActive;
    /// <summary>
    /// Эффект завершения сейчас играет: таймер в Finished, эффект выбран и его длительность не истекла.
    /// Единый источник и для окна (MainWindow.ApplyDialEffect), и для трея.
    /// </summary>
    public bool IsFinishEffectActive
    {
        get => _isFinishEffectActive;
        private set { if (_isFinishEffectActive == value) return; _isFinishEffectActive = value; OnPropertyChanged(); }
    }

    private double SecondsSinceFinish => _finishedAtUtc is { } t ? (DateTime.UtcNow - t).TotalSeconds : 0;

    private void RefreshFinishEffectActive()
    {
        int duration = _settings.FinishEffectDurationSeconds;
        IsFinishEffectActive = Status == TimerStatus.Finished
                               && ActiveFinishEffect != FinishVisualEffect.None
                               && (duration == 0 || SecondsSinceFinish < duration);

        // «Сбросить таймер по окончании эффекта» (докладка 2026-10-01): эффект отыграл своё время и звонок закончился —
        // сброс, виджет снова показывает часы. «Бесконечный» эффект или без эффекта — нечего ждать, не сбрасываем.
        if (Status == TimerStatus.Finished && _settings.ResetAfterFinishEffect && !_resetAfterEffectQueued && !_alarmPlaying
            && ActiveFinishEffect != FinishVisualEffect.None && duration > 0 && SecondsSinceFinish >= duration)
        {
            _resetAfterEffectQueued = true;
            _dispatcher.BeginInvoke(() =>
            {
                _resetAfterEffectQueued = false;
                if (Status == TimerStatus.Finished) ResetTimer();
            });
        }
    }

    private bool _alarmPlaying;            // звонок завершения ещё играет — сброс по окончании эффекта ждёт его
    private bool _resetAfterEffectQueued;

    /// <summary>Галочка «Сбросить таймер по окончании эффекта» (под ползунком длительности эффекта).</summary>
    public bool ResetAfterFinishEffect
    {
        get => _settings.ResetAfterFinishEffect;
        set
        {
            if (_settings.ResetAfterFinishEffect == value) return;
            _settings.ResetAfterFinishEffect = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            RefreshFinishEffectActive();
        }
    }

    // ============ «Об авторе» (2026-09-27) ============
    // Константы, а не настройки: в settings.json у пользователей уже записаны прежние заглушки, и они
    // перекрыли бы новые значения по умолчанию.
    public const string Author = "alexan-moscow";

    /// <summary>Страница выпусков — пользователю там полезнее всего (скачать новую версию).</summary>
    public const string GitHubUrl = "https://github.com/alexan-moscow/time2gadget/releases";


    /// <summary>«сентябрь 2026» — месяц/год сборки проставляет сама сборка (AssemblyMetadata ReleaseDate); null — нет.</summary>
    private static string? ReleaseMonth
    {
        get
        {
            var raw = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ReleaseDate")?.Value;
            return DateTime.TryParseExact(raw, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out var date)
                ? date.ToString("MMMM yyyy", Russian)
                : null;
        }
    }

    /// <summary>Ссылка-имя в «Об авторе»: страница автора — пока заглушка (докладка 2026-09-28), адрес будет позже.</summary>
    public RelayCommand OpenAuthorPageCommand => _openAuthorPageCommand ??= new RelayCommand(() => { });
    private RelayCommand? _openAuthorPageCommand;

    // ---- Позиции окон (докладка 2026-09-28): полный и компактный вид — каждый своя; окно настроек — своя ----

    /// <summary>Сохранённое место вида; null — ещё не было (первое переключение центрирует по циферблату).</summary>
    /// <param name="allowLegacy">Только при старте: прежняя общая позиция (до раздельных) — для вида, в котором
    /// программу закрыли. При переключении нельзя: CompactMode к этому моменту уже новый.</param>
    public System.Windows.Point? GetSavedWindowPosition(bool compact, bool allowLegacy = false)
    {
        var (left, top) = compact
            ? (_settings.CompactWindowLeft, _settings.CompactWindowTop)
            : (_settings.FullWindowLeft, _settings.FullWindowTop);
        if (left is null && allowLegacy) (left, top) = (_settings.WindowLeft, _settings.WindowTop);
        return left is { } l && top is { } t ? new System.Windows.Point(l, t) : null;
    }

    public void SaveWindowPosition(bool compact, double left, double top)
    {
        if (compact) (_settings.CompactWindowLeft, _settings.CompactWindowTop) = (left, top);
        else (_settings.FullWindowLeft, _settings.FullWindowTop) = (left, top);
        _settingsService.Save(_settings);
    }

    public System.Windows.Point? SavedSettingsWindowPosition =>
        _settings.SettingsWindowLeft is { } l && _settings.SettingsWindowTop is { } t ? new System.Windows.Point(l, t) : null;

    public void SaveSettingsWindowPosition(double left, double top)
    {
        (_settings.SettingsWindowLeft, _settings.SettingsWindowTop) = (left, top);
        _settingsService.Save(_settings);
    }

    /// <summary>Сохранить текущие настройки как есть (первый запуск — зафиксировать значения по умолчанию).</summary>
    public void PersistSettings() => _settingsService.Save(_settings);

    /// <summary>Поля-кэши свойств — из _settings (при старте и после сброса настроек).</summary>
    private void LoadCachedFromSettings()
    {
        _isMuted = _settings.IsMuted;
        _isAlwaysOnTop = _settings.AlwaysOnTop;
        _isCompactMode = _settings.CompactMode;
        _isLaunchAtStartup = _settings.LaunchAtStartup; // App.xaml.cs при старте сверяет с реестром
        _closeBehavior = _settings.CloseBehavior;
        _autoCloseAfterFinish = _settings.AutoCloseAfterFinish;
        _alarmVolume = _settings.AlarmVolume;
        _selectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == _settings.AudioDeviceId) ?? AudioDeviceInfo.SystemDefault;
        // Неизвестный Id (старые настройки/удалённый звонок) — показываем звонок по умолчанию.
        _selectedRingtoneId = _settings.RingtoneId == RingtoneCatalog.CustomId || RingtoneCatalog.IsBuiltIn(_settings.RingtoneId)
            ? _settings.RingtoneId
            : RingtoneCatalog.DefaultId;
        _customSoundFilePath = _settings.CustomSoundFilePath;
        if (_ringtoneOptions is not null) UpdateCustomOption(_ringtoneOptions, _customSoundFilePath); // после сброса настроек
        _alarmRepeatCount = _settings.AlarmRepeatCount;
        _runningEffect = _settings.RunningEffect;
        _finishEffect = _settings.FinishEffect;
        LoadQuickTimers();

        // Подсвечиваем последний использованный сектор, но НЕ скармливаем его время в engine —
        // при запуске приложения дисплей должен показывать 00:00 (docs/DECISIONS.md, 2026-09-27),
        // а не длительность последнего пресета. Отсчёт начинается только явным кликом по сектору.
        var lastMinutes = _settings.LastPresetMinutes;
        SelectedPreset = Presets.FirstOrDefault(p => p.Minutes == lastMinutes) ?? Presets[4]; // fallback: 30 мин
    }

    /// <summary>Настройки сброшены к значениям по умолчанию — View заново раскладывает и центрирует окна.</summary>
    public event EventHandler? SettingsReset;

    public RelayCommand ResetSettingsCommand => _resetSettingsCommand ??= new RelayCommand(ResetSettings);
    private RelayCommand? _resetSettingsCommand;

    /// <summary>
    /// «Сбросить настройки» (докладка 2026-09-28): все значения по умолчанию, включая положения окон, без
    /// перезапуска. Идущий таймер не трогаем — это не настройка. Дату последней проверки обновлений
    /// сохраняем, чтобы сброс не вызывал лишний запрос к GitHub.
    /// </summary>
    private void ResetSettings()
    {
        if (System.Windows.MessageBox.Show(
                "Вернуть все настройки к значениям по умолчанию и поставить окна на исходные места?",
                "Тайм2гаджет — сброс настроек", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.Yes)
            return;

        ResetWallpaperFeatures(); // оба режима фона выключаются — фон как был до них
        PinWallpapers = false; // по умолчанию выкл — вернуть слайд-шоу, пока известно, откуда оно шло
        var lastUpdateCheck = _settings.LastUpdateCheckUtc;
        var (windowProfiles, windowAssignments) = (_settings.WindowProfiles, _settings.WindowProfileAssignments);
        if (_settings.RunElevated) ElevationService.DeleteTask(); // по умолчанию выкл — задача не нужна
        var defaults = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanRead && p.CanWrite))
            p.SetValue(_settings, p.GetValue(defaults));
        _settings.LastUpdateCheckUtc = lastUpdateCheck;
        // Профили размера окон — данные пользователя (как пресеты), сброс настроек их не стирает (2026-09-29).
        (_settings.WindowProfiles, _settings.WindowProfileAssignments) = (windowProfiles, windowAssignments);

        LoadCachedFromSettings();
        AutostartService.SetEnabled(_settings.LaunchAtStartup);
        _settingsService.Save(_settings);

        OnPropertyChanged(string.Empty); // все привязки перечитать
        RefreshIdleClock();
        RaiseCompactLayoutChanges();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        SettingsReset?.Invoke(this, EventArgs.Empty);
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
            TimerStatus.Running => $"Пауза{KeyHint(StartPauseKey)}",
            TimerStatus.Paused => $"Продолжить отсчёт{KeyHint(StartPauseKey)}",
            TimerStatus.Finished => $"Запустить этот таймер ещё раз{KeyHint(StartPauseKey)}",
            _ => _engine.TotalDuration > TimeSpan.Zero
                ? $"Запустить отсчёт{KeyHint(StartPauseKey)}"
                : SelectedPreset is not null
                    ? $"Запустить таймер на {FormatMinutes(SelectedPreset.Minutes)}{KeyHint(StartPauseKey)}"
                    : "Сначала выберите время на кольце"
        },
        "Reset" => Status == TimerStatus.Finished
            ? $"Выключить звонок и сбросить на 00:00{KeyHint(ResetKey)}"
            : $"Сбросить таймер на 00:00{KeyHint(ResetKey)}",
        "Center" => Status is TimerStatus.Running or TimerStatus.Paused
            ? $"Клик ЛКМ — компактный режим{KeyHint(CompactKey)}\nПотяните — переместить окно"
            : $"Клик ЛКМ — компактный режим{KeyHint(CompactKey)}\nКолесо мыши — ±1 минута",
        "CompactCenter" => $"Клик ЛКМ — вернуться к полному виду{KeyHint(CompactKey)}",
        "Mute" => IsMuted ? "Включить звук звонка" : "Выключить звук звонка",
        "Volume" => $"Громкость звонка: {Math.Round(AlarmVolume * 100)}%",
        "Settings" => IsUpdateAvailable ? $"Настройки — доступно обновление {AvailableVersion}" : "Настройки",
        "Close" => CloseBehavior == CloseBehavior.Exit ? "Закрыть приложение" : "Свернуть в трей",
        "AutoClose" => AutoCloseAfterFinish
            ? $"Автозакрытие ВКЛ: после звонка приложение {(CloseBehavior == CloseBehavior.Exit ? "закроется" : "свернётся в трей")}\nКлик ЛКМ — выключить"
            : $"Автозакрытие выкл\nКлик ЛКМ — после звонка {(CloseBehavior == CloseBehavior.Exit ? "закрывать приложение" : "сворачивать в трей")}",
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

    private static string KeyHint(HotkeyBinding key) => key.IsEmpty ? "" : $"  ({key})";

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
            ClearQuickTimerOverrides();
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
            ClearQuickTimerOverrides();
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
        ClearQuickTimerOverrides(); // свой звук и эффект быстрого таймера — только до сброса
        _soundService.StopAlarm();
        _alarmPlaying = false;
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

        ClearQuickTimerOverrides();
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
        if (PickAndImportSound() is not { } path) return;
        var previous = CustomSoundFilePath;
        CustomSoundFilePath = path;
        SelectedRingtoneId = RingtoneCatalog.CustomId;
        DeleteSoundIfUnused(previous); // прежняя копия — только если её не использует быстрый таймер
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

    // Эффект завершения в трее — те же вспышки/пульсации, что в окне; строб короче обычного UI-тика
    // (150мс), поэтому пока таймер в Finished, трей обновляется чаще (TrayService перерисовывает только при изменении).
    private static readonly TimeSpan TrayEffectFrameInterval = TimeSpan.FromMilliseconds(40);
    private DispatcherTimer? _trayEffectTimer;

    private void OnEngineFinished(object? sender, EventArgs e)
    {
        _finishedAtUtc = DateTime.UtcNow;
        _trayEffectTimer ??= CreateTrayEffectTimer();
        _trayEffectTimer.Start();

        // Мониторы погасли по простою (или компьютер только что проснулся по таймеру пробуждения — тогда
        // Windows держит экраны выключенными) — включаем, чтобы окончание увидели.
        if (WakeDisplayOnFinish) _power.WakeDisplay();

        if (!IsMuted && !_quickSilent) // быстрый таймер без звука — только эффект
        {
            _alarmPlaying = true;
            _soundService.PlayAlarm(_settings, _alarmChoice); // автозакрытие — по AlarmCompleted, когда звонок отыграет
        }
        else
        {
            _silentAutoCloseTimer?.Stop();
            _silentAutoCloseTimer = new DispatcherTimer { Interval = SilentFinishAutoCloseDelay };
            _silentAutoCloseTimer.Tick += (_, _) => { _silentAutoCloseTimer?.Stop(); TryAutoClose(); };
            _silentAutoCloseTimer.Start();
        }
        RaiseStatusDependentChanges();
    }

    /// <summary>
    /// Закрыть/свернуть после отработавшего таймера — только если переключатель включён и таймер всё ещё
    /// в Finished (пользователь не успел сбросить/перезапустить, пока звенело).
    /// </summary>
    private void TryAutoClose()
    {
        if (Status != TimerStatus.Finished) return;
        if (AutoCloseAfterFinish)
        {
            AutoCloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (AutoCompactAfterFinish)
        {
            IsCompactMode = true; // вид сам встаёт на своё сохранённое место
            if (IsWindowHidden) ShowRequested?.Invoke(this, EventArgs.Empty); // было в трее — показать компакт
        }
    }

    /// <summary>
    /// Иконка трея повторяет кольцо таймера (docs/UI-CONTRACT.md → Tray). Зовётся на каждом UI-тике;
    /// TrayService сам перерисовывает иконку только при видимом изменении.
    /// </summary>
    private void UpdateTray()
    {
        if (_disposed) return; // выход: трей уже закрыт, а окно ещё сообщает «скрыто»
        var tooltip = Status switch
        {
            TimerStatus.Running => $"Тайм2гаджет — осталось {Converters.TimeSpanToStringConverter.Format(RemainingTime)}",
            TimerStatus.Paused => $"Тайм2гаджет — пауза, {Converters.TimeSpanToStringConverter.Format(RemainingTime)}",
            TimerStatus.Finished => "Тайм2гаджет — время вышло!",
            _ => "Тайм2гаджет"
        };
        var activeEffect = IsFinishEffectActive ? ActiveFinishEffect : FinishVisualEffect.None;
        // Эффекта нет (выключен/истёк), а окна не видно — простое мигание, чтобы окончание не пропустили.
        bool blinkWhileHidden = Status == TimerStatus.Finished && IsWindowHidden && activeEffect == FinishVisualEffect.None;
        _trayService.Update(new TrayIconState(Status, ProgressFraction, activeEffect, SecondsSinceFinish, blinkWhileHidden, tooltip));
    }

    private DispatcherTimer CreateTrayEffectTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TrayEffectFrameInterval };
        timer.Tick += (_, _) => { RefreshFinishEffectActive(); UpdateTray(); };
        return timer;
    }

    private void RefreshFromEngine()
    {
        RemainingTime = _engine.GetRemaining();
        IsColonLit = Status != TimerStatus.Running || RemainingTime.Milliseconds >= 500;
        var total = _engine.TotalDuration;
        ProgressFraction = total.TotalSeconds <= 0
            ? 0
            : 1 - (RemainingTime.TotalSeconds / total.TotalSeconds);
        RefreshFinishEffectActive();
        RefreshClock();
        RefreshIdleClock();
        UpdateWakeTimer();
        UpdateTray();
    }

    private void RaiseStatusDependentChanges()
    {
        if (Status != TimerStatus.Finished)
        {
            _silentAutoCloseTimer?.Stop();
            _trayEffectTimer?.Stop();
            _finishedAtUtc = null;
            _power.ReleaseDisplay(); // окончание «погашено» (сброс/новый запуск) — экран больше не держим
        }
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
        RefreshFromEngine();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _uiTimer.Stop();
        _trayEffectTimer?.Stop();
        _silentAutoCloseTimer?.Stop();
        _updateTimer?.Stop();
        _power.Dispose();
        _soundService.StopAlarm();
        _trayService.Dispose();
    }
}

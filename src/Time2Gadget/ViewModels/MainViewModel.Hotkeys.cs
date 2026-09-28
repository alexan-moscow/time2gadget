using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Threading;
using Time2Gadget.Models;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Клавиши и быстрые таймеры (докладка 2026-09-28). У каждой из четырёх клавиш действий — галочка «везде»:
/// включена — клавиша глобальная (Services/GlobalHotkeyService, регистрирует MainWindow по <see cref="HotkeysChanged"/>),
/// выключена — работает, пока окно таймера в фокусе (MainWindow → <see cref="HandleWindowKey"/>). Быстрые таймеры — всегда глобальные.
/// </summary>
public sealed partial class MainViewModel
{
    public const int MaxQuickTimers = 5;
    /// <summary>Id глобальных клавиш для GlobalHotkeyService: действия 1–4, быстрые таймеры — 10 + номер строки.</summary>
    public const int ShowHideHotkeyId = 1, StartPauseHotkeyId = 2, ResetHotkeyId = 3, CompactHotkeyId = 4, QuickTimerHotkeyIdBase = 10;

    // Сочетания, которые подставляются при включении «везде» (решение пользователя 2026-09-28): Пробел/R глобально
    // мешали бы всем программам. При выключении — обратно клавиши по умолчанию для окна.
    private static readonly HotkeyBinding GlobalStartPauseDefault = HotkeyBinding.FromKey(Key.F10, ModifierKeys.Control);
    private static readonly HotkeyBinding GlobalResetDefault = HotkeyBinding.FromKey(Key.F11, ModifierKeys.Control);

    /// <summary>Изменились глобальные клавиши — View перерегистрирует их.</summary>
    public event EventHandler? HotkeysChanged;
    /// <summary>Нажата клавиша «Показать / скрыть окно».</summary>
    public event EventHandler? ToggleWindowRequested;

    public ObservableCollection<QuickTimerItem> QuickTimers { get; } = new();

    public void RequestShowWindow() => ShowRequested?.Invoke(this, EventArgs.Empty);

    public HotkeyBinding StartPauseKey { get => _settings.StartPauseKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.StartPauseKey = v, StartPauseGlobal); }
    public HotkeyBinding ResetKey { get => _settings.ResetKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.ResetKey = v, ResetGlobal); }
    public HotkeyBinding CompactKey { get => _settings.CompactKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.CompactKey = v, CompactGlobal); }
    public HotkeyBinding ShowHideKey { get => _settings.ShowHideKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.ShowHideKey = v, ShowHideGlobal); }

    public bool StartPauseGlobal
    {
        get => _settings.StartPauseGlobal;
        set => SetGlobal(value, _settings.StartPauseGlobal, v =>
        {
            _settings.StartPauseGlobal = v;
            _settings.StartPauseKey = v ? GlobalStartPauseDefault : HotkeyBinding.FromKey(Key.Space);
        }, nameof(StartPauseKey));
    }

    public bool ResetGlobal
    {
        get => _settings.ResetGlobal;
        set => SetGlobal(value, _settings.ResetGlobal, v =>
        {
            _settings.ResetGlobal = v;
            _settings.ResetKey = v ? GlobalResetDefault : HotkeyBinding.FromKey(Key.R);
        }, nameof(ResetKey));
    }

    public bool CompactGlobal
    {
        get => _settings.CompactGlobal;
        set => SetGlobal(value, _settings.CompactGlobal, v => _settings.CompactGlobal = v, null);
    }

    public bool ShowHideGlobal
    {
        get => _settings.ShowHideGlobal;
        set => SetGlobal(value, _settings.ShowHideGlobal, v => _settings.ShowHideGlobal = v, null);
    }

    /// <summary>Кнопка-значок в строке клавиши (окно ↔ глобус): параметр — StartPause/Reset/Compact/ShowHide.</summary>
    public RelayCommand ToggleKeyScopeCommand => _toggleKeyScopeCommand ??= new RelayCommand(p =>
    {
        switch (p as string)
        {
            case "StartPause": StartPauseGlobal = !StartPauseGlobal; break;
            case "Reset": ResetGlobal = !ResetGlobal; break;
            case "Compact": CompactGlobal = !CompactGlobal; break;
            case "ShowHide": ShowHideGlobal = !ShowHideGlobal; break;
        }
    });
    private RelayCommand? _toggleKeyScopeCommand;

    private void SetKey(HotkeyBinding value, Action<AppSettings, HotkeyBinding> assign, bool global, [CallerMemberName] string? name = null)
    {
        assign(_settings, value ?? HotkeyBinding.Empty);
        _settingsService.Save(_settings);
        OnPropertyChanged(name);
        if (global) HotkeysChanged?.Invoke(this, EventArgs.Empty);
        else RefreshHotkeyWarnings(); // совпадение с глобальной тоже показываем
    }

    private void SetGlobal(bool value, bool current, Action<bool> apply, string? keyName, [CallerMemberName] string? name = null)
    {
        if (value == current) return;
        apply(value);
        _settingsService.Save(_settings);
        OnPropertyChanged(name);
        if (keyName is not null) OnPropertyChanged(keyName);
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanAddQuickTimer => QuickTimers.Count < MaxQuickTimers;

    public RelayCommand AddQuickTimerCommand => _addQuickTimerCommand ??= new RelayCommand(() =>
    {
        if (!CanAddQuickTimer) return;
        var model = new QuickTimer();
        _settings.QuickTimers.Add(model);
        QuickTimers.Add(new QuickTimerItem(this, model));
        OnQuickTimersChanged(listChanged: true);
    });
    private RelayCommand? _addQuickTimerCommand;

    internal void RemoveQuickTimer(QuickTimerItem item)
    {
        if (ReferenceEquals(_previewOwner, item)) StopPreview();
        if (ReferenceEquals(_effectPreviewList, item.EffectOptions)) StopEffectPreview();
        _settings.QuickTimers.Remove(item.Model);
        DeleteSoundIfUnused(item.Model.CustomSoundFilePath);
        QuickTimers.Remove(item);
        if (QuickTimers.Count == 0) { AddQuickTimerCommand.Execute(null); return; } // всегда хотя бы одна строка
        OnQuickTimersChanged(listChanged: true);
    }

    internal void OnQuickTimersChanged(bool listChanged = false, bool hotkeys = true)
    {
        if (listChanged)
        {
            for (int i = 0; i < QuickTimers.Count; i++) QuickTimers[i].Number = i + 1;
            OnPropertyChanged(nameof(CanAddQuickTimer));
        }
        _settingsService.Save(_settings);
        if (hotkeys) HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadQuickTimers()
    {
        _settings.QuickTimers ??= new List<QuickTimer>();
        _settings.QuickTimers.RemoveAll(q => q is null);
        if (_settings.QuickTimers.Count > MaxQuickTimers) _settings.QuickTimers.RemoveRange(MaxQuickTimers, _settings.QuickTimers.Count - MaxQuickTimers);
        if (_settings.QuickTimers.Count == 0) _settings.QuickTimers.Add(new QuickTimer());

        QuickTimers.Clear();
        foreach (var q in _settings.QuickTimers)
        {
            q.Binding ??= HotkeyBinding.Empty;
            QuickTimers.Add(new QuickTimerItem(this, q) { Number = QuickTimers.Count + 1 });
        }
        OnPropertyChanged(nameof(CanAddQuickTimer));
    }

    // ---- Звук быстрого таймера (докладка 2026-09-28): свой звонок и своё устройство; null — как в разделе «Звук» ----

    /// <summary>Ключ «как в разделе «Звук»» в списке устройств (у звонков это пустая строка).</summary>
    internal const string GeneralDeviceKey = "general";

    internal IReadOnlyList<AudioDeviceInfo> QuickTimerDeviceOptions => _quickTimerDeviceOptions ??=
        AudioDevices.Prepend(new AudioDeviceInfo(GeneralDeviceKey, "Как в разделе «Звук»")).ToList();
    private IReadOnlyList<AudioDeviceInfo>? _quickTimerDeviceOptions;

    internal double GeneralVolume => AlarmVolume;

    // Звук идущего быстрого таймера; сбрасывается, когда время выбирают иначе (сектор, колесо, сброс).
    private SoundChoice? _alarmChoice;

    // ---- Глобальные клавиши ----

    /// <summary>Глобальные клавиши для регистрации: id → сочетание (только рабочие строки).</summary>
    public IReadOnlyDictionary<int, HotkeyBinding> GetGlobalHotkeys()
    {
        var map = new Dictionary<int, HotkeyBinding>();
        foreach (var (id, key, global) in ActionKeys())
            if (global && !key.IsEmpty && !map.ContainsValue(key)) map[id] = key;
        for (int i = 0; i < QuickTimers.Count; i++)
            if (QuickTimers[i].Model.IsUsable && !map.ContainsValue(QuickTimers[i].Model.Binding))
                map[QuickTimerHotkeyIdBase + i] = QuickTimers[i].Model.Binding;
        return map;
    }

    private IEnumerable<(int Id, HotkeyBinding Key, bool Global)> ActionKeys()
    {
        yield return (StartPauseHotkeyId, StartPauseKey, StartPauseGlobal);
        yield return (ResetHotkeyId, ResetKey, ResetGlobal);
        yield return (CompactHotkeyId, CompactKey, CompactGlobal);
        yield return (ShowHideHotkeyId, ShowHideKey, ShowHideGlobal);
    }

    private static string ActionName(int id) => id switch
    {
        StartPauseHotkeyId => "Старт / пауза",
        ResetHotkeyId => "Сброс",
        CompactHotkeyId => "Компактный вид",
        _ => "Показать / скрыть"
    };

    private IReadOnlyCollection<int> _failedHotkeyIds = Array.Empty<int>();

    /// <summary>View сообщает, какие глобальные клавиши Windows не дала зарегистрировать (заняты).</summary>
    public void SetFailedHotkeys(IReadOnlyCollection<int> failedIds)
    {
        _failedHotkeyIds = failedIds;
        RefreshHotkeyWarnings();
    }

    /// <summary>Предупреждения под разделами: пусто — всё в порядке.</summary>
    public string HotkeysWarning { get; private set; } = string.Empty;
    public string QuickTimersWarning { get; private set; } = string.Empty;

    private void RefreshHotkeyWarnings()
    {
        var keys = ActionKeys().Where(a => _failedHotkeyIds.Contains(a.Id))
            .Select(a => $"{ActionName(a.Id)}: «{a.Key}» занято другой программой — выберите другое сочетание.").ToList();

        // Одно и то же сочетание в двух местах: глобальное перехватывает его у окна таймера.
        var all = ActionKeys().Select(a => (Name: ActionName(a.Id), a.Key)).ToList();
        all.AddRange(QuickTimers.Select(q => ($"Таймер {q.Number}", q.Model.Binding)));
        var duplicates = all.Where(a => !a.Key.IsEmpty).GroupBy(a => a.Key).Where(g => g.Count() > 1)
            .Select(g => $"«{g.Key}» назначено дважды: {string.Join(", ", g.Select(x => x.Name))}.");

        var timers = new List<string>();
        foreach (var q in QuickTimers)
        {
            var m = q.Model;
            if (_failedHotkeyIds.Contains(QuickTimerHotkeyIdBase + q.Number - 1))
                timers.Add($"Таймер {q.Number}: «{m.Binding}» занято другой программой.");
            else if (m.Binding.IsEmpty && m.Duration > TimeSpan.Zero)
                timers.Add($"Таймер {q.Number}: не назначена клавиша.");
            else if (!m.Binding.IsEmpty && m.Duration <= TimeSpan.Zero)
                timers.Add($"Таймер {q.Number}: не задано время.");
        }

        HotkeysWarning = string.Join("\n", keys.Concat(duplicates));
        QuickTimersWarning = string.Join("\n", timers);
        OnPropertyChanged(nameof(HotkeysWarning));
        OnPropertyChanged(nameof(QuickTimersWarning));
    }

    /// <summary>Сработала глобальная клавиша (id из <see cref="GetGlobalHotkeys"/>) — или клавиша окна с тем же действием.</summary>
    public void OnGlobalHotkey(int id)
    {
        switch (id)
        {
            case ShowHideHotkeyId: ToggleWindowRequested?.Invoke(this, EventArgs.Empty); return;
            case StartPauseHotkeyId: StartPauseCommand.Execute(null); return;
            case ResetHotkeyId: ResetCommand.Execute(null); return;
            case CompactHotkeyId: ToggleCompactModeCommand.Execute(null); return;
        }
        int index = id - QuickTimerHotkeyIdBase;
        if (index >= 0 && index < QuickTimers.Count && QuickTimers[index].Model.IsUsable)
            StartQuickTimer(QuickTimers[index].Model);
    }

    /// <summary>Быстрый таймер: запустить заново на это время, что бы ни шло сейчас (звонок — выключить).</summary>
    private void StartQuickTimer(QuickTimer timer)
    {
        _soundService.StopAlarm();
        var duration = timer.Duration;
        // Совпадает с сектором кольца — подсветить его; нет — кольцо без выделения.
        SelectedPreset = Presets.FirstOrDefault(p => TimeSpan.FromMinutes(p.Minutes) == duration);
        SetQuickTimerOverrides(timer); // свой звук (или без звука) и эффект завершения
        _engine.Restart(duration);
        RaiseStatusDependentChanges();
    }

    /// <summary>
    /// Клавиши окна таймера (не глобальные): true — нажатие обработано. Esc — всегда ещё и сброс.
    /// Глобальные сюда не доходят (их перехватывает Windows), поэтому пропускаются.
    /// </summary>
    public bool HandleWindowKey(HotkeyBinding pressed)
    {
        if (pressed.IsEmpty) return false;
        if (pressed == HotkeyBinding.FromKey(Key.Escape)) { ResetCommand.Execute(null); return true; }
        foreach (var (id, key, global) in ActionKeys())
        {
            if (global || pressed != key) continue;
            OnGlobalHotkey(id); // то же действие, что у глобальной
            return true;
        }
        return false;
    }
}

/// <summary>Строка «Быстрые таймеры» в настройках — обёртка над <see cref="QuickTimer"/> с сохранением при изменении.</summary>
public sealed class QuickTimerItem : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    internal QuickTimer Model { get; }
    private int _number;

    internal QuickTimerItem(MainViewModel owner, QuickTimer model)
    {
        _owner = owner;
        Model = model;
        RemoveCommand = new RelayCommand(() => _owner.RemoveQuickTimer(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get => _number; set { _number = value; OnPropertyChanged(); } }

    public int Hours { get => Model.Hours; set => Set(Math.Clamp(value, 0, 23), v => Model.Hours = v, Model.Hours); }
    public int Minutes { get => Model.Minutes; set => Set(Math.Clamp(value, 0, 59), v => Model.Minutes = v, Model.Minutes); }
    public int Seconds { get => Model.Seconds; set => Set(Math.Clamp(value, 0, 59), v => Model.Seconds = v, Model.Seconds); }

    public HotkeyBinding Binding
    {
        get => Model.Binding;
        set
        {
            value ??= HotkeyBinding.Empty;
            if (Model.Binding == value) return;
            Model.Binding = value;
            OnPropertyChanged();
            _owner.OnQuickTimersChanged();
        }
    }

    // ---- Звук: всплывашка по кнопке-колокольчику (громкость, звонок, устройство) ----
    private IReadOnlyList<RingtoneOption>? _soundOptions;
    /// <summary>Свой список у каждой строки: у пункта «Свой файл…» — файл и длительность этого таймера.</summary>
    public IReadOnlyList<RingtoneOption> SoundOptions => _soundOptions ??= _owner.CreateRingtoneOptions(general: true, Model.CustomSoundFilePath);
    public IReadOnlyList<AudioDeviceInfo> DeviceOptions => _owner.QuickTimerDeviceOptions;

    /// <summary>Id звонка для списка: "" — общий звонок.</summary>
    public string RingtoneKey
    {
        get => Model.RingtoneId ?? string.Empty;
        set
        {
            if (value == RingtoneCatalog.CustomId && string.IsNullOrEmpty(Model.CustomSoundFilePath))
            {
                // «Свой файл…» без файла — файл выбирается по щелчку (ChooseRingtone); выделение вернуть назад.
                Dispatcher.CurrentDispatcher.BeginInvoke(() => OnPropertyChanged(nameof(RingtoneKey)));
                return;
            }
            SetSound(() => Model.RingtoneId = string.IsNullOrEmpty(value) ? null : value);
        }
    }

    /// <summary>Звонить по окончании (иначе только эффект).</summary>
    public bool SoundEnabled => Model.SoundEnabled;

    private bool _isSoundMenuOpen;
    public bool IsSoundMenuOpen { get => _isSoundMenuOpen; set { if (_isSoundMenuOpen != value) { _isSoundMenuOpen = value; OnPropertyChanged(); } } }

    /// <summary>
    /// Колокольчик (решение пользователя 2026-09-28): звук включён — щелчок выключает его (меню не открывается);
    /// выключен — открывает меню выбора звука.
    /// </summary>
    public RelayCommand BellCommand => _bellCommand ??= new RelayCommand(() =>
    {
        if (Model.SoundEnabled) SetSound(() => Model.SoundEnabled = false);
        else IsSoundMenuOpen = true;
    });
    private RelayCommand? _bellCommand;

    /// <summary>Щелчок по звонку в меню: выбрать, включить звук и закрыть меню («Свой файл…» без файла — сначала выбрать файл).</summary>
    public void ChooseRingtone(string id)
    {
        _owner.StopPreview();
        IsSoundMenuOpen = false;
        if (id == RingtoneCatalog.CustomId && string.IsNullOrEmpty(Model.CustomSoundFilePath))
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(BrowseSound); // после того, как меню закроется
            return;
        }
        SetSound(() =>
        {
            Model.RingtoneId = string.IsNullOrEmpty(id) ? null : id;
            Model.SoundEnabled = true;
        });
    }

    // ---- Эффект завершения: кнопка левее колокольчика, меню с просмотром ----

    private IReadOnlyList<EffectOption>? _effectOptions;
    public IReadOnlyList<EffectOption> EffectOptions => _effectOptions ??= MainViewModel.CreateFinishEffectOptions("Нет эффекта");

    public FinishVisualEffect FinishEffect => Model.FinishEffect;

    private bool _isEffectMenuOpen;
    public bool IsEffectMenuOpen { get => _isEffectMenuOpen; set { if (_isEffectMenuOpen != value) { _isEffectMenuOpen = value; OnPropertyChanged(); } } }

    public RelayCommand EffectCommand => _effectCommand ??= new RelayCommand(() => IsEffectMenuOpen = true);
    private RelayCommand? _effectCommand;

    public string EffectToolTip => $"Эффект по окончании: {EffectOptions.FirstOrDefault(o => Equals(o.Value, Model.FinishEffect))?.Label}\nЩелчок — выбрать";

    /// <summary>Щелчок по эффекту в меню: выбрать, прекратить показ и закрыть меню.</summary>
    public void ChooseEffect(object value)
    {
        _owner.StopEffectPreview();
        IsEffectMenuOpen = false;
        if (value is not FinishVisualEffect effect || effect == Model.FinishEffect) return;
        Model.FinishEffect = effect;
        OnPropertyChanged(nameof(FinishEffect));
        OnPropertyChanged(nameof(EffectToolTip));
        _owner.OnQuickTimersChanged(hotkeys: false);
    }

    public void PreviewEffect(EffectOption option) => _owner.TogglePreviewEffect(EffectOptions, option);

    /// <summary>Id устройства для списка: <see cref="MainViewModel.GeneralDeviceKey"/> — как в разделе «Звук».</summary>
    public string DeviceKey
    {
        get => Model.AudioDeviceId ?? MainViewModel.GeneralDeviceKey;
        set => SetSound(() => Model.AudioDeviceId = value is null or MainViewModel.GeneralDeviceKey ? null : value);
    }

    /// <summary>Галочка «общая»: громкость из раздела «Звук». Снимается сама, если тронуть ползунок.</summary>
    public bool UseGeneralVolume
    {
        get => Model.Volume is null;
        set
        {
            if (value == UseGeneralVolume) return;
            SetSound(() => Model.Volume = value ? null : _owner.GeneralVolume);
        }
    }

    /// <summary>Ползунок громкости: при «общей» показывает общую; изменение — своя громкость таймера.</summary>
    public double VolumeValue
    {
        get => Model.Volume ?? _owner.GeneralVolume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - VolumeValue) < 0.001) return;
            SetSound(() => Model.Volume = value);
        }
    }

    internal void OnGeneralVolumeChanged()
    {
        if (Model.Volume is null) OnPropertyChanged(nameof(VolumeValue));
    }

    public bool IsCustomFileSelected => Model.RingtoneId == RingtoneCatalog.CustomId;
    public string CustomFileName => string.IsNullOrEmpty(Model.CustomSoundFilePath) ? "" : System.IO.Path.GetFileName(Model.CustomSoundFilePath);

    public RelayCommand BrowseSoundCommand => _browseSoundCommand ??= new RelayCommand(BrowseSound);
    private RelayCommand? _browseSoundCommand;

    /// <summary>Выбрать свой файл таймера: копия — в папку звуков программы, прежняя удаляется, если больше не нужна.</summary>
    private void BrowseSound()
    {
        if (_owner.PickAndImportSound() is not { } path)
        {
            OnPropertyChanged(nameof(RingtoneKey)); // отмена — выделение в списке вернуть на прежний звонок
            return;
        }
        var previous = Model.CustomSoundFilePath;
        SetSound(() =>
        {
            Model.CustomSoundFilePath = path;
            Model.RingtoneId = RingtoneCatalog.CustomId;
            Model.SoundEnabled = true;
        });
        _owner.UpdateCustomOption(SoundOptions, path);
        _owner.DeleteSoundIfUnused(previous);
    }

    /// <summary>Задан свой звук (звонок, устройство или громкость) — колокольчик подсвечен.</summary>
    public string SoundToolTip => !Model.SoundEnabled
        ? "Звук выключен (только эффект)\nЩелчок — выбрать звук"
        : $"Звук: {SoundOptions.FirstOrDefault(r => r.Id == RingtoneKey)?.Title ?? "общий"}" +
          (IsCustomFileSelected ? $" ({CustomFileName})" : "") + "\n" +
          $"Громкость: {(Model.Volume is { } v ? $"{Math.Round(v * 100)}%" : "общая")}\n" +
          $"Устройство: {DeviceOptions.FirstOrDefault(d => d.Id == DeviceKey)?.FriendlyName ?? "как в разделе «Звук»"}\n" +
          "Щелчок — выключить звук";

    /// <summary>▶/■ во всплывашке: звонок — с громкостью и на устройстве этого таймера.</summary>
    public void Preview(string ringtoneId) =>
        _owner.TogglePreview(this, ringtoneId, new SoundChoice(
            string.IsNullOrEmpty(ringtoneId) ? null : ringtoneId, Model.CustomSoundFilePath, Model.AudioDeviceId, Model.Volume));

    private void SetSound(Action assign)
    {
        assign();
        foreach (var name in new[] { nameof(RingtoneKey), nameof(DeviceKey), nameof(UseGeneralVolume), nameof(VolumeValue),
                     nameof(IsCustomFileSelected), nameof(CustomFileName), nameof(SoundEnabled), nameof(SoundToolTip) })
            OnPropertyChanged(name);
        _owner.OnQuickTimersChanged(hotkeys: false);
    }

    public RelayCommand RemoveCommand { get; }

    private void Set(int value, Action<int> assign, int current, [CallerMemberName] string? name = null)
    {
        if (value == current) return;
        assign(value);
        OnPropertyChanged(name);
        _owner.OnQuickTimersChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    public const int ShowHideHotkeyId = 1, StartPauseHotkeyId = 2, ResetHotkeyId = 3, CompactHotkeyId = 4, CursorConfineHotkeyId = 5,
        WindowProfilesHotkeyId = 6, BackgroundWindowHotkeyId = 7, QuickTimersWindowHotkeyId = 8, QuickTimerHotkeyIdBase = 10;

    // ---- Быстрое открытие окна «Размер и положение окон программ» (докладка 2026-09-29): галочка и клавиша — в самом окне ----

    /// <summary>Клавиша включена — регистрируется глобально; по умолчанию выкл.</summary>
    public bool WindowProfilesHotkeyEnabled
    {
        get => _settings.WindowProfilesHotkeyEnabled;
        set => SetGlobal(value, _settings.WindowProfilesHotkeyEnabled, v => _settings.WindowProfilesHotkeyEnabled = v, nameof(WindowProfilesKeyShown));
    }

    public HotkeyBinding WindowProfilesKey
    {
        get => _settings.WindowProfilesKey ?? HotkeyBinding.Empty;
        set => SetKey(value, (s, v) => s.WindowProfilesKey = v, global: true);
    }

    /// <summary>Открыть (вывести вперёд) окно «Размер и положение окон программ»; аргумент — окно, активное в момент нажатия.</summary>
    public event EventHandler<IntPtr>? WindowProfilesRequested;

    // ---- Быстрое открытие окон «Заставка и фон экрана» и «Быстрые таймеры» (докладка 2026-10-01) ----

    public bool BackgroundWindowHotkeyEnabled
    {
        get => _settings.BackgroundWindowHotkeyEnabled;
        set => SetGlobal(value, _settings.BackgroundWindowHotkeyEnabled, v => _settings.BackgroundWindowHotkeyEnabled = v, nameof(BackgroundWindowKeyShown));
    }

    public HotkeyBinding BackgroundWindowKey
    {
        get => _settings.BackgroundWindowKey ?? HotkeyBinding.Empty;
        set => SetKey(value, (s, v) => s.BackgroundWindowKey = v, global: true);
    }

    public bool QuickTimersWindowHotkeyEnabled
    {
        get => _settings.QuickTimersWindowHotkeyEnabled;
        set => SetGlobal(value, _settings.QuickTimersWindowHotkeyEnabled, v => _settings.QuickTimersWindowHotkeyEnabled = v, nameof(QuickTimersWindowKeyShown));
    }

    public HotkeyBinding QuickTimersWindowKey
    {
        get => _settings.QuickTimersWindowKey ?? HotkeyBinding.Empty;
        set => SetKey(value, (s, v) => s.QuickTimersWindowKey = v, global: true);
    }

    // Поле сочетания клавиши быстрого открытия: выключено — «Не задано» (приглушено), включено — само сочетание (по умолчанию
    // или своё), его можно переназначить (докладка 2026-10-01).
    public HotkeyBinding WindowProfilesKeyShown
    {
        get => WindowProfilesHotkeyEnabled ? WindowProfilesKey : HotkeyBinding.Empty;
        set { if (WindowProfilesHotkeyEnabled) { WindowProfilesKey = value; OnPropertyChanged(); } }
    }

    public HotkeyBinding BackgroundWindowKeyShown
    {
        get => BackgroundWindowHotkeyEnabled ? BackgroundWindowKey : HotkeyBinding.Empty;
        set { if (BackgroundWindowHotkeyEnabled) { BackgroundWindowKey = value; OnPropertyChanged(); } }
    }

    public HotkeyBinding QuickTimersWindowKeyShown
    {
        get => QuickTimersWindowHotkeyEnabled ? QuickTimersWindowKey : HotkeyBinding.Empty;
        set { if (QuickTimersWindowHotkeyEnabled) { QuickTimersWindowKey = value; OnPropertyChanged(); } }
    }

    /// <summary>Открыть окно «Заставка и фон экрана» (клавиша быстрого открытия).</summary>
    public event EventHandler? BackgroundWindowRequested;

    /// <summary>Открыть окно «Быстрые таймеры» (клавиша быстрого открытия).</summary>
    public event EventHandler? QuickTimersWindowRequested;

    /// <summary>Клавиши быстрого открытия окон, которые могут быть включены: id, сочетание, включена.</summary>
    private IEnumerable<(int Id, HotkeyBinding Key, bool Enabled)> WindowOpenKeys()
    {
        yield return (WindowProfilesHotkeyId, WindowProfilesKey, WindowProfilesHotkeyEnabled);
        yield return (BackgroundWindowHotkeyId, BackgroundWindowKey, BackgroundWindowHotkeyEnabled);
        yield return (QuickTimersWindowHotkeyId, QuickTimersWindowKey, QuickTimersWindowHotkeyEnabled);
        yield return (QuickPanelHotkeyId, QuickPanelKey, QuickPanelHotkeyEnabled);
    }

    // ---- «Не выпускать указатель мыши из окна» (докладка 2026-09-29): клавиша — в окне «Размер и положение окон программ» ----

    /// <summary>Глобальная клавиша: временно выключить/включить ограничение указателя. Регистрируется, только пока оно используется.</summary>
    public HotkeyBinding CursorConfineKey
    {
        get => _settings.CursorConfineKey ?? HotkeyBinding.Empty;
        set => SetKey(value, (s, v) => s.CursorConfineKey = v, global: true);
    }

    /// <summary>Служба ограничения указателя — создаёт MainWindow.</summary>
    internal Services.CursorConfineService? CursorConfine { get; set; }

    /// <summary>Отметить окно «не выпускать указатель» или снять; клавиша регистрируется/снимается вместе с первым/последним окном.</summary>
    internal void SetCursorConfine(IntPtr hwnd, bool confine)
    {
        if (CursorConfine is not { } service) return;
        bool wasInUse = service.HasWindows;
        service.Set(hwnd, confine);
        if (wasInUse != service.HasWindows) HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

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
        // имя — первое свободное «Таймер N» (имена других строк не сдвигаются)
        int n = 1;
        while (QuickTimers.Any(q => q.Model.Name == $"Таймер {n}")) n++;
        var model = new QuickTimer { Name = $"Таймер {n}" };
        // цвета виджета — случайные из палитры, ход и окончание разные, по возможности не как у других таймеров
        var used = QuickTimers.SelectMany(q => new[] { q.Model.PanelColor, q.Model.PanelFinishColor }).ToHashSet();
        var palette = QuickTimerItem.PanelColors.Select(c => c.Hex).ToList();
        var fresh = palette.Where(c => !used.Contains(c)).ToList();
        var pool = fresh.Count >= 2 ? fresh : palette;
        var progress = pool[Random.Shared.Next(pool.Count)];
        var finishPool = pool.Where(c => c != progress).ToList();
        model.PanelColor = progress;
        model.PanelFinishColor = finishPool[Random.Shared.Next(finishPool.Count)];
        // эффекты виджета — тоже случайные, ещё не выбранные у других таймеров
        var usedProgress = QuickTimers.Select(q => q.Model.PanelProgress).ToHashSet();
        var progressAll = Enum.GetValues<QuickPanelProgress>().Where(e => e != QuickPanelProgress.None).ToList();
        var progressFresh = progressAll.Where(e => !usedProgress.Contains(e)).ToList();
        var progressPool = progressFresh.Count > 0 ? progressFresh : progressAll;
        model.PanelProgress = progressPool[Random.Shared.Next(progressPool.Count)];
        var usedFinish = QuickTimers.Select(q => q.Model.PanelFinish).ToHashSet();
        var finishAll = Enum.GetValues<QuickPanelFinish>().Where(e => e != QuickPanelFinish.None).ToList();
        var finishFresh = finishAll.Where(e => !usedFinish.Contains(e)).ToList();
        var finishEffects = finishFresh.Count > 0 ? finishFresh : finishAll;
        model.PanelFinish = finishEffects[Random.Shared.Next(finishEffects.Count)];
        model.PanelConfigured = true;
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
        item.Panel.Stop();
        if (QuickTimers.Count == 0) AddQuickTimerCommand.Execute(null); // всегда хотя бы одна строка
        else OnQuickTimersChanged(listChanged: true);
        RefreshPanelTimers();
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
        SwapOldPanelDefaults();
        foreach (var q in _settings.QuickTimers) if (q.ShowInPanel) q.PanelConfigured = true; // уже настроены в виджете
        for (int i = 0; i < _settings.QuickTimers.Count; i++) _settings.QuickTimers[i].Name ??= $"Таймер {i + 1}"; // строки до имён

        QuickTimers.Clear();
        foreach (var q in _settings.QuickTimers)
        {
            q.Binding ??= HotkeyBinding.Empty;
            QuickTimers.Add(new QuickTimerItem(this, q) { Number = QuickTimers.Count + 1 });
        }
        OnPropertyChanged(nameof(CanAddQuickTimer));
        RefreshPanelTimers();
        UpdatePanelTicker();
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
        if (CursorConfine?.HasWindows == true && !CursorConfineKey.IsEmpty && !map.ContainsValue(CursorConfineKey))
            map[CursorConfineHotkeyId] = CursorConfineKey;
        foreach (var (id, key, enabled) in WindowOpenKeys())
            if (enabled && !key.IsEmpty && !map.ContainsValue(key)) map[id] = key;
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
        CursorConfineHotkeyId => "Указатель мыши в окне",
        WindowProfilesHotkeyId => "Размер и положение окон программ",
        BackgroundWindowHotkeyId => "Фоновая заставка и слайдшоу экрана",
        QuickTimersWindowHotkeyId => "Быстрые таймеры (окно)",
        QuickPanelHotkeyId => "Виджет быстрых таймеров",
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
        var keys = ActionKeys().Append((CursorConfineHotkeyId, CursorConfineKey, true)).Concat(WindowOpenKeys().Select(k => (k.Id, k.Key, true)))
            .Where(a => _failedHotkeyIds.Contains(a.Item1))
            .Select(a => $"{ActionName(a.Item1)}: «{a.Item2}» занято другой программой — выберите другое сочетание.").ToList();

        // Одно и то же сочетание в двух местах: глобальное перехватывает его у окна таймера.
        var all = ActionKeys().Select(a => (Name: ActionName(a.Id), a.Key)).ToList();
        all.Add((ActionName(CursorConfineHotkeyId), CursorConfineKey));
        foreach (var (id, key, enabled) in WindowOpenKeys()) if (enabled) all.Add((ActionName(id), key));
        all.AddRange(QuickTimers.Select(q => (q.Name, q.Model.Binding)));
        var duplicates = all.Where(a => !a.Key.IsEmpty).GroupBy(a => a.Key).Where(g => g.Count() > 1)
            .Select(g => $"«{g.Key}» назначено дважды: {string.Join(", ", g.Select(x => x.Name))}.");

        var timers = new List<string>();
        foreach (var q in QuickTimers)
        {
            var m = q.Model;
            if (_failedHotkeyIds.Contains(QuickTimerHotkeyIdBase + q.Number - 1))
                timers.Add($"{q.Name}: «{m.Binding}» занято другой программой.");
            else if (m.Binding.IsEmpty && m.Duration > TimeSpan.Zero && !m.ShowInPanel) // в панели и без клавиши — можно
                timers.Add($"{q.Name}: не назначена клавиша.");
            else if (!m.Binding.IsEmpty && m.Duration <= TimeSpan.Zero)
                timers.Add($"{q.Name}: не задано время.");
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
            case CursorConfineHotkeyId:
                if (CursorConfine is { } confine)
                    _trayService.ShowBalloon("Тайм2гаджет", confine.Toggle()
                        ? $"Указатель мыши снова не выходит из окна. {CursorConfineKey} — выключить."
                        : $"Указатель мыши свободен. {CursorConfineKey} — снова ограничить окном.");
                return;
            case WindowProfilesHotkeyId:
                WindowProfilesRequested?.Invoke(this, Services.NativeWindows.ForegroundWindow);
                return;
            case BackgroundWindowHotkeyId:
                BackgroundWindowRequested?.Invoke(this, EventArgs.Empty);
                return;
            case QuickTimersWindowHotkeyId:
                QuickTimersWindowRequested?.Invoke(this, EventArgs.Empty);
                return;
            case QuickPanelHotkeyId:
                QuickPanelToggleRequested?.Invoke(this, EventArgs.Empty);
                return;
        }
        int index = id - QuickTimerHotkeyIdBase;
        if (index >= 0 && index < QuickTimers.Count && QuickTimers[index].Model.IsUsable)
        {
            // таймер виджета (синий/оранжевый) — запускается в виджете, иначе — на главном таймере (компакт и полный вид)
            if (QuickTimers[index].Model.ShowInPanel) StartPanelTimer(QuickTimers[index]);
            else StartQuickTimer(QuickTimers[index].Model);
        }
    }

    /// <summary>Быстрый таймер: запустить заново на это время, что бы ни шло сейчас (звонок — выключить).</summary>
    internal void StartQuickTimer(QuickTimer timer)
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
        Panel = new QuickPanelTimer(owner, this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get => _number; set { _number = value; OnPropertyChanged(); } }

    /// <summary>Отсчёт этой строки в виджете быстрых таймеров (свой, независимо от главного таймера).</summary>
    public QuickPanelTimer Panel { get; }

    // ---- Плей/пауза и сброс перед именем (докладка 2026-10-01): таймер виджета — его отсчёт в виджете, иначе — главный таймер ----

    /// <summary>Этот таймер сейчас идёт (или на паузе, или звонит) на главном таймере.</summary>
    private bool IsOnMain => ReferenceEquals(_owner.MainQuickTimer, Model) && !_owner.IsMainIdle;

    public bool RowIsRunning => ShowInPanel ? Panel.IsRunning : IsOnMain && _owner.IsRunning;

    public RelayCommand RowPlayCommand => _rowPlayCommand ??= new RelayCommand(() =>
    {
        if (Model.Duration <= TimeSpan.Zero) return;
        if (ShowInPanel)
        {
            bool wasIdle = !Panel.IsActive;
            Panel.PlayPauseFromRow();
            if (wasIdle) _owner.ShowQuickPanel();
        }
        else if (IsOnMain && !_owner.IsFinished) _owner.StartPauseCommand.Execute(null);
        else _owner.StartQuickTimer(Model);
    });
    private RelayCommand? _rowPlayCommand;

    public RelayCommand RowResetCommand => _rowResetCommand ??= new RelayCommand(() =>
    {
        if (ShowInPanel) Panel.ResetFromRow();
        else if (IsOnMain) _owner.ResetCommand.Execute(null);
    });
    private RelayCommand? _rowResetCommand;

    public string RowPlayToolTip => (ShowInPanel ? "В виджете быстрых таймеров. " : "На главном таймере. ") +
        (RowIsRunning ? "Клик ЛКМ — пауза" : "Клик ЛКМ — запустить");

    public string RowResetToolTip => ShowInPanel ? "Клик ЛКМ — сброс в виджете" : "Клик ЛКМ — сброс главного таймера (если работает этот таймер)";

    internal void RaiseRowState()
    {
        OnPropertyChanged(nameof(RowIsRunning));
        OnPropertyChanged(nameof(RowPlayToolTip));
        OnPropertyChanged(nameof(RowResetToolTip));
    }

    /// <summary>Имя (по умолчанию «Таймер N»); пустое — вернуть прежнее.</summary>
    public string Name
    {
        get => Model.Name ?? $"Таймер {Number}";
        set
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value == Model.Name) { OnPropertyChanged(); return; }
            Model.Name = value.Length > 24 ? value[..24] : value;
            OnPropertyChanged();
            Panel.RaiseLook();
            _owner.OnQuickTimersChanged(hotkeys: false);
        }
    }

    public int Days { get => Model.Days; set => Set(Math.Clamp(value, 0, 99), v => Model.Days = v, Model.Days); }
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

    public string EffectToolTip => ShowInPanel
        ? "Эффект главного таймера недоступен: таймер в виджете, у него свои эффекты (строка «В виджете»)"
        : $"Эффект по окончании: {EffectOptions.FirstOrDefault(o => Equals(o.Value, Model.FinishEffect))?.Label}\nКлик ЛКМ — выбрать";

    /// <summary>Эффект главного таймера — только у обычного таймера (синий/оранжевый идут в виджете, там свои эффекты).</summary>
    public bool MainEffectEnabled => !ShowInPanel;

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
        ? "Звук выключен (только эффект)\nКлик ЛКМ — выбрать звук"
        : $"Звук: {SoundOptions.FirstOrDefault(r => r.Id == RingtoneKey)?.Title ?? "общий"}" +
          (IsCustomFileSelected ? $" ({CustomFileName})" : "") + "\n" +
          $"Громкость: {(Model.Volume is { } v ? $"{Math.Round(v * 100)}%" : "общая")}\n" +
          $"Устройство: {DeviceOptions.FirstOrDefault(d => d.Id == DeviceKey)?.FriendlyName ?? "как в разделе «Звук»"}\n" +
          "Клик ЛКМ — выключить звук";

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
        Panel.OnDurationChanged();
        _owner.OnQuickTimersChanged();
    }

    // ---- Панель быстрых таймеров (докладка 2026-10-01): переключатель «в панели», эффект хода и цвет, эффект окончания ----

    /// <summary>Показывать в панели быстрых таймеров.</summary>
    public bool ShowInPanel
    {
        get => Model.ShowInPanel;
        set
        {
            if (Model.ShowInPanel == value) return;
            Model.ShowInPanel = value;
            if (value && !Model.PanelConfigured)
            {
                // первый таймер, впервые в виджете: тонкие линии голубым, строб красным
                Model.PanelConfigured = true;
                if (Number == 1)
                {
                    Model.PanelProgress = QuickPanelProgress.ThinLines;
                    Model.PanelColor = "#00C2FF";
                    Model.PanelFinish = QuickPanelFinish.Strobe;
                    Model.PanelFinishColor = "#FF1744";
                    OnPanelLookChanged();
                }
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelToolTip));
            OnPropertyChanged(nameof(IsPanelPermanentShown));
            OnPropertyChanged(nameof(MainEffectEnabled));
            OnPropertyChanged(nameof(EffectToolTip));
            RaiseRowState();
            _owner.OnQuickTimersChanged(); // и предупреждения: таймеру виджета клавиша не обязательна
            _owner.RefreshPanelTimers();
        }
    }

    /// <summary>Постоянно в панели (оранжевый); иначе (синий) — пропадает, когда отыграют эффект и звук окончания.</summary>
    public bool PanelPermanent
    {
        get => Model.PanelPermanent;
        set
        {
            if (Model.PanelPermanent == value) return;
            Model.PanelPermanent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelToolTip));
            OnPropertyChanged(nameof(IsPanelPermanentShown));
            _owner.OnQuickTimersChanged(hotkeys: false);
            _owner.RefreshPanelTimers();
        }
    }

    /// <summary>Переключатель оранжевый: в панели и постоянно.</summary>
    public bool IsPanelPermanentShown => ShowInPanel && PanelPermanent;

    /// <summary>Клик ЛКМ: показан — убрать; нет — показать до окончания (синий).</summary>
    public RelayCommand TogglePanelCommand => _togglePanelCommand ??= new RelayCommand(() =>
    {
        if (ShowInPanel) { ShowInPanel = false; return; }
        PanelPermanent = false;
        ShowInPanel = true;
    });
    private RelayCommand? _togglePanelCommand;

    /// <summary>Клик ПКМ: постоянно в панели (оранжевый); уже постоянно — обратно до окончания (синий).</summary>
    public RelayCommand PermanentPanelCommand => _permanentPanelCommand ??= new RelayCommand(() =>
    {
        if (ShowInPanel && PanelPermanent) { PanelPermanent = false; return; }
        PanelPermanent = true;
        ShowInPanel = true;
    });
    private RelayCommand? _permanentPanelCommand;

    public string PanelToolTip => !ShowInPanel
        ? "Не в виджете: клавиша запускает главный таймер.\nКлик ЛКМ — таймер виджета (синий: виден, пока работает и доигрывает окончание). Клик ПКМ — в виджете всегда (оранжевый)"
        : PanelPermanent
            ? "В виджете всегда (оранжевый).\nКлик ЛКМ — убрать из виджета. Клик ПКМ — синий: виден, пока работает и доигрывает окончание"
            : "Таймер виджета (синий): клавиша запускает его в виджете, по окончании (когда отыграют эффект и звук) он пропадает из виджета до следующего запуска.\nКлик ЛКМ — убрать из виджета. Клик ПКМ — в виджете всегда (оранжевый)";

    private static readonly EnumOption<QuickPanelProgress>[] ProgressChoices = new[]
    {
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Fill, "заливка строки"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Drain, "убывающая заливка"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Line, "полоска под строкой"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Segments, "деления под строкой"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.ThinLines, "тонкие линии под строкой"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Flash, "вспышки"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Breathe, "дыхание цвета"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Waves, "встречные волны"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.Snake, "змейка"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.RainbowSnake, "радужная змейка"),
        new EnumOption<QuickPanelProgress>(QuickPanelProgress.None, "без хода"),
    };

    private static readonly EnumOption<QuickPanelFinish>[] FinishChoices = new[]
    {
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Blink, "мигание"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Flash, "вспышки цветом"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Pulse, "пульсация времени"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Strobe, "строб"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Rainbow, "радужная волна"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Waves, "встречные волны"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.Snake, "змейка"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.RainbowSnake, "радужная змейка"),
        new EnumOption<QuickPanelFinish>(QuickPanelFinish.None, "без эффекта"),
    };

    /// <summary>Свои списки у каждой строки — у пунктов ▶/■ (просмотр в виджете, докладка 2026-10-01).</summary>
    public IReadOnlyList<EffectOption> ProgressOptions => _progressOptions ??= ProgressChoices.Select(o => new EffectOption(o.Value, o.Label) { Tint = Model.PanelColor }).ToList();
    private IReadOnlyList<EffectOption>? _progressOptions;
    public IReadOnlyList<EffectOption> FinishOptions => _finishOptions ??= FinishChoices.Select(o => new EffectOption(o.Value, o.Label) { Tint = Model.PanelFinishColor }).ToList();
    private IReadOnlyList<EffectOption>? _finishOptions;

    /// <summary>▶/■ у эффекта виджета: показать этот таймер в виджете с эффектом (повторно — прекратить).</summary>
    public void TogglePanelPreview(EffectOption option) => _owner.TogglePanelPreview(this, option);

    internal void MarkPanelPreview(object? value)
    {
        foreach (var o in ProgressOptions.Concat(FinishOptions)) o.IsPlaying = value is not null && Equals(o.Value, value);
    }

    public QuickPanelProgress PanelProgress
    {
        get => Model.PanelProgress;
        set { if (Model.PanelProgress == value) return; Model.PanelProgress = value; OnPanelLookChanged(); }
    }

    public QuickPanelFinish PanelFinish
    {
        get => Model.PanelFinish;
        set { if (Model.PanelFinish == value) return; Model.PanelFinish = value; OnPanelLookChanged(); }
    }

    /// <summary>Длительность эффекта окончания в панели, 0–20 с.</summary>
    public int PanelFinishSeconds
    {
        get => Model.PanelFinishSeconds;
        set { value = Math.Clamp(value, 0, 20); if (Model.PanelFinishSeconds == value) return; Model.PanelFinishSeconds = value; OnPanelLookChanged(); }
    }

    public string PanelColor => Model.PanelColor;

    /// <summary>У радужных эффектов свой набор цветов — кнопка цвета недоступна.</summary>
    public bool ProgressColorEnabled => Model.PanelProgress is not (QuickPanelProgress.None or QuickPanelProgress.RainbowSnake);
    public bool FinishColorEnabled => Model.PanelFinish is not (QuickPanelFinish.None or QuickPanelFinish.Rainbow or QuickPanelFinish.RainbowSnake);
    public string PanelFinishColor => Model.PanelFinishColor;

    private bool _isFinishColorMenuOpen;
    public bool IsFinishColorMenuOpen { get => _isFinishColorMenuOpen; set { if (_isFinishColorMenuOpen != value) { _isFinishColorMenuOpen = value; OnPropertyChanged(); } } }

    public RelayCommand OpenFinishColorMenuCommand => _openFinishColorMenuCommand ??= new RelayCommand(() => IsFinishColorMenuOpen = true);
    private RelayCommand? _openFinishColorMenuCommand;

    public RelayCommand ChooseFinishColorCommand => _chooseFinishColorCommand ??= new RelayCommand(p =>
    {
        IsFinishColorMenuOpen = false;
        if (p is not string hex || hex == Model.PanelFinishColor) return;
        Model.PanelFinishColor = hex;
        OnPanelLookChanged();
    });
    private RelayCommand? _chooseFinishColorCommand;

    public string FinishColorToolTip => $"Цвет эффекта окончания в виджете: {ColorName(Model.PanelFinishColor)}. Клик ЛКМ — выбрать";
    private static string ColorName(string hex) => PanelColors.FirstOrDefault(c => c.Hex == hex)?.Name ?? hex;

    private bool _isColorMenuOpen;
    public bool IsColorMenuOpen { get => _isColorMenuOpen; set { if (_isColorMenuOpen != value) { _isColorMenuOpen = value; OnPropertyChanged(); } } }

    public RelayCommand OpenColorMenuCommand => _openColorMenuCommand ??= new RelayCommand(() => IsColorMenuOpen = true);
    private RelayCommand? _openColorMenuCommand;

    public RelayCommand ChooseColorCommand => _chooseColorCommand ??= new RelayCommand(p =>
    {
        IsColorMenuOpen = false;
        if (p is not string hex || hex == Model.PanelColor) return;
        Model.PanelColor = hex;
        OnPanelLookChanged();
    });
    private RelayCommand? _chooseColorCommand;

    /// <summary>Цвета эффекта хода: синий программы и яркие основные.</summary>
    public static IReadOnlyList<WallpaperColorOption> PanelColors { get; } = new WallpaperColorOption[]
    {
        new("#3D8BFF", "Синий (как в программе)"), new("#00C2FF", "Голубой"), new("#00C853", "Зелёный"), new("#AEEA00", "Салатовый"),
        new("#FFD600", "Жёлтый"), new("#FF9100", "Оранжевый"), new("#FF1744", "Красный"), new("#F50057", "Малиновый"),
        new("#D500F9", "Пурпурный"), new("#7C4DFF", "Фиолетовый"), new("#FFFFFF", "Белый"), new("#90A4AE", "Серый"),
    };

    public string ColorToolTip => $"Цвет хода в виджете: {ColorName(Model.PanelColor)}. Клик ЛКМ — выбрать";

    private void OnPanelLookChanged()
    {
        foreach (var n in new[] { nameof(PanelProgress), nameof(PanelFinish), nameof(PanelFinishSeconds), nameof(PanelColor), nameof(ColorToolTip), nameof(PanelFinishColor), nameof(FinishColorToolTip), nameof(ProgressColorEnabled), nameof(FinishColorEnabled) })
            OnPropertyChanged(n);
        foreach (var o in ProgressOptions) o.Tint = Model.PanelColor; // ▶/■ в списках — выбранным цветом
        foreach (var o in FinishOptions) o.Tint = Model.PanelFinishColor;
        Panel.RaiseLook();
        _owner.OnQuickTimersChanged(hotkeys: false);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

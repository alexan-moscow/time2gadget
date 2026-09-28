using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Time2Gadget.Models;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Клавиши и быстрые таймеры (докладка 2026-09-28). Клавиши окна (старт/пауза, сброс, компакт) обрабатывает
/// MainWindow, пока окно в фокусе; глобальные (показать/скрыть, быстрые таймеры) регистрирует
/// Services/GlobalHotkeyService по событию <see cref="HotkeysChanged"/>.
/// </summary>
public sealed partial class MainViewModel
{
    public const int MaxQuickTimers = 5;
    /// <summary>Id глобальных клавиш для GlobalHotkeyService: показать/скрыть и быстрые таймеры (10 + номер строки).</summary>
    public const int ShowHideHotkeyId = 1, QuickTimerHotkeyIdBase = 10;

    /// <summary>Изменились глобальные клавиши — View перерегистрирует их.</summary>
    public event EventHandler? HotkeysChanged;
    /// <summary>Нажата клавиша «Показать / скрыть окно».</summary>
    public event EventHandler? ToggleWindowRequested;

    public ObservableCollection<QuickTimerItem> QuickTimers { get; } = new();

    public void RequestShowWindow() => ShowRequested?.Invoke(this, EventArgs.Empty);

    public HotkeyBinding StartPauseKey { get => _settings.StartPauseKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.StartPauseKey = v); }
    public HotkeyBinding ResetKey { get => _settings.ResetKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.ResetKey = v); }
    public HotkeyBinding CompactKey { get => _settings.CompactKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.CompactKey = v); }
    public HotkeyBinding ShowHideKey { get => _settings.ShowHideKey ?? HotkeyBinding.Empty; set => SetKey(value, (s, v) => s.ShowHideKey = v, global: true); }

    private void SetKey(HotkeyBinding value, Action<AppSettings, HotkeyBinding> assign, bool global = false, [CallerMemberName] string? name = null)
    {
        assign(_settings, value ?? HotkeyBinding.Empty);
        _settingsService.Save(_settings);
        OnPropertyChanged(name);
        if (global) HotkeysChanged?.Invoke(this, EventArgs.Empty);
        else RefreshHotkeyWarnings(); // конфликт с глобальными тоже показываем
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
        _settings.QuickTimers.Remove(item.Model);
        QuickTimers.Remove(item);
        if (QuickTimers.Count == 0) { AddQuickTimerCommand.Execute(null); return; } // всегда хотя бы одна строка
        OnQuickTimersChanged(listChanged: true);
    }

    internal void OnQuickTimersChanged(bool listChanged = false)
    {
        if (listChanged)
        {
            for (int i = 0; i < QuickTimers.Count; i++) QuickTimers[i].Number = i + 1;
            OnPropertyChanged(nameof(CanAddQuickTimer));
        }
        _settingsService.Save(_settings);
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Глобальные клавиши для регистрации: id → сочетание (только рабочие строки).</summary>
    public IReadOnlyDictionary<int, HotkeyBinding> GetGlobalHotkeys()
    {
        var map = new Dictionary<int, HotkeyBinding>();
        if (!ShowHideKey.IsEmpty) map[ShowHideHotkeyId] = ShowHideKey;
        for (int i = 0; i < QuickTimers.Count; i++)
            if (QuickTimers[i].Model.IsUsable && !map.ContainsValue(QuickTimers[i].Model.Binding))
                map[QuickTimerHotkeyIdBase + i] = QuickTimers[i].Model.Binding;
        return map;
    }

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
        var keys = new List<string>();
        if (_failedHotkeyIds.Contains(ShowHideHotkeyId))
            keys.Add($"«{ShowHideKey}» занято другой программой — выберите другое сочетание.");

        // Одно и то же сочетание в двух местах: глобальное перехватывает его у окна таймера.
        var all = new List<(string Name, HotkeyBinding Key)>
        {
            ("Старт / пауза", StartPauseKey), ("Сброс", ResetKey), ("Компактный вид", CompactKey), ("Показать / скрыть", ShowHideKey)
        };
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

    /// <summary>Сработала глобальная клавиша (id из <see cref="GetGlobalHotkeys"/>).</summary>
    public void OnGlobalHotkey(int id)
    {
        if (id == ShowHideHotkeyId) { ToggleWindowRequested?.Invoke(this, EventArgs.Empty); return; }
        int index = id - QuickTimerHotkeyIdBase;
        if (index >= 0 && index < QuickTimers.Count && QuickTimers[index].Model.IsUsable)
            StartQuickTimer(QuickTimers[index].Model.Duration);
    }

    /// <summary>Быстрый таймер: запустить заново на это время, что бы ни шло сейчас (звонок — выключить).</summary>
    private void StartQuickTimer(TimeSpan duration)
    {
        _soundService.StopAlarm();
        // Совпадает с сектором кольца — подсветить его; нет — кольцо без выделения.
        SelectedPreset = Presets.FirstOrDefault(p => TimeSpan.FromMinutes(p.Minutes) == duration);
        _engine.Restart(duration);
        RaiseStatusDependentChanges();
    }

    /// <summary>Клавиши окна таймера: true — нажатие обработано. Esc — всегда ещё и сброс.</summary>
    public bool HandleWindowKey(HotkeyBinding pressed)
    {
        if (pressed.IsEmpty) return false;
        if (pressed == StartPauseKey) { StartPauseCommand.Execute(null); return true; }
        if (pressed == ResetKey || pressed == HotkeyBinding.FromKey(Key.Escape)) { ResetCommand.Execute(null); return true; }
        if (pressed == CompactKey) { ToggleCompactModeCommand.Execute(null); return true; }
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

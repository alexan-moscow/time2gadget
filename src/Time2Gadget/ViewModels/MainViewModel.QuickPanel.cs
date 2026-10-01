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

    /// <summary>Все таймеры виджета (синие и оранжевые) — невидимый слой окна виджета для его ширины и высоты.</summary>
    public ObservableCollection<QuickPanelTimer> PanelSizers { get; } = new();

    public bool QuickPanelSortMenu
    {
        get => _settings.QuickPanelSortMenu;
        set
        {
            if (_settings.QuickPanelSortMenu == value) return;
            _settings.QuickPanelSortMenu = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickPanelClickControlAvailable));
            OnPropertyChanged(nameof(QuickPanelClickControlActive));
        }
    }

    /// <summary>Быстрое управление кликами по таймеру (галочка; действует, только когда меню по ПКМ выключено).</summary>
    public bool QuickPanelClickControl
    {
        get => _settings.QuickPanelClickControl;
        set
        {
            if (_settings.QuickPanelClickControl == value) return;
            _settings.QuickPanelClickControl = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickPanelClickControlActive));
        }
    }

    public bool QuickPanelClickControlAvailable => !QuickPanelSortMenu;

    /// <summary>Клики по строкам виджета управляют таймерами.</summary>
    public bool QuickPanelClickControlActive => QuickPanelClickControl && !QuickPanelSortMenu;

    public bool QuickPanelShrink
    {
        get => _settings.QuickPanelShrink;
        set { if (_settings.QuickPanelShrink == value) return; _settings.QuickPanelShrink = value; _settingsService.Save(_settings); OnPropertyChanged(); }
    }
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

    /// <summary>Поле сочетания: выключено — «Не задано», включено — сочетание (по умолчанию Ctrl+Shift+PageUp).</summary>
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

    /// <summary>Размер панели, 60–160% (ползунок в разделе «Размер» настроек и в окне «Быстрые таймеры»).</summary>
    public double QuickPanelScale
    {
        get => Math.Clamp(_settings.QuickPanelScale, 0.6, 1.6);
        set
        {
            var v = Math.Round(Math.Clamp(value, 0.6, 1.6), 2);
            if (Math.Abs(_settings.QuickPanelScale - v) < 0.001) return;
            _settings.QuickPanelScale = v;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickPanelScaleLabel));
        }
    }

    public string QuickPanelScaleLabel => $"Размер виджета быстрых таймеров: {Math.Round(QuickPanelScale * 100)}%";

    // ---- Кнопки в заголовке панели (докладка 2026-10-01) ----

    /// <summary>Поверх всех окон (клик ЛКМ по кнопке; зелёная верхняя половина значка).</summary>
    public bool QuickPanelTopmost
    {
        get => _settings.QuickPanelTopmost;
        set { if (_settings.QuickPanelTopmost == value) return; _settings.QuickPanelTopmost = value; SavePanelFlag(); }
    }

    /// <summary>Закреплена — не перетаскивается (клик ПКМ по той же кнопке; жёлтая нижняя половина значка).</summary>
    public bool QuickPanelPinned
    {
        get => _settings.QuickPanelPinned;
        set { if (_settings.QuickPanelPinned == value) return; _settings.QuickPanelPinned = value; SavePanelFlag(); }
    }

    /// <summary>Открыть снова на том же месте, когда таймер панели закончится (клик ПКМ по крестику; крестик зелёный).</summary>
    public bool QuickPanelReopenOnFinish
    {
        get => _settings.QuickPanelReopenOnFinish;
        set { if (_settings.QuickPanelReopenOnFinish == value) return; _settings.QuickPanelReopenOnFinish = value; SavePanelFlag(); }
    }

    private void SavePanelFlag()
    {
        _settingsService.Save(_settings);
        foreach (var n in new[] { nameof(QuickPanelTopmost), nameof(QuickPanelPinned), nameof(QuickPanelReopenOnFinish),
                     nameof(QuickPanelWindowToolTip), nameof(QuickPanelCloseToolTip) })
            OnPropertyChanged(n);
    }

    public RelayCommand ToggleQuickPanelTopmostCommand => _toggleTopmost ??= new RelayCommand(() => QuickPanelTopmost = !QuickPanelTopmost);
    private RelayCommand? _toggleTopmost;
    public RelayCommand ToggleQuickPanelPinnedCommand => _togglePinned ??= new RelayCommand(() => QuickPanelPinned = !QuickPanelPinned);
    private RelayCommand? _togglePinned;
    public RelayCommand ToggleQuickPanelReopenCommand => _toggleReopen ??= new RelayCommand(() => QuickPanelReopenOnFinish = !QuickPanelReopenOnFinish);
    private RelayCommand? _toggleReopen;

    public string QuickPanelWindowToolTip =>
        $"Поверх всех окон: {(QuickPanelTopmost ? "да" : "нет")} (верхняя половина значка — зелёная).\n" +
        $"Закреплена: {(QuickPanelPinned ? "да — не перетаскивается" : "нет")} (нижняя половина — жёлтая).\n" +
        "Клик ЛКМ — поверх всех окон. Клик ПКМ — закрепить";

    public string QuickPanelCloseToolTip =>
        "Клик ЛКМ — закрыть виджет.\n" +
        (QuickPanelReopenOnFinish
            ? "Клик ПКМ — не открывать снова (сейчас открывается на том же месте, когда таймер закончится — крестик зелёный)"
            : "Клик ПКМ — открывать снова на том же месте, когда таймер закончится (крестик станет зелёным)");
    internal bool QuickPanelOpen => _settings.QuickPanelOpen;

    // ---- Места окон (Views/WindowMemory) ----
    internal WindowPlacement? GetWindowPlacement(string key) =>
        _settings.WindowPlacements is { } map && map.TryGetValue(key, out var p) ? p : null;

    internal void SaveWindowPlacement(string key, double left, double top, double width, double height)
    {
        _settings.WindowPlacements ??= new();
        _settings.WindowPlacements[key] = new WindowPlacement { Left = left, Top = top, Width = width, Height = height };
        _settingsService.Save(_settings);
    }

    internal void ShowQuickPanel() => QuickPanelShowRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Главный таймер в покое (не идёт, не на паузе, не звонит).</summary>
    internal bool IsMainIdle => Status == TimerStatus.Ready;

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

    // ---- Порядок таймеров в виджете (контекстное меню виджета и окно «Быстрые таймеры») ----

    public IReadOnlyList<PanelSortOption> QuickPanelSortOptions => _sortOptions ??= new PanelSortOption[]
    {
        new(QuickPanelSort.Created, "по порядку создания"),
        new(QuickPanelSort.RemainingDescending, "по оставшемуся времени, убывание"),
        new(QuickPanelSort.RemainingAscending, "по оставшемуся времени, возрастание"),
        new(QuickPanelSort.StartedDescending, "по времени запуска, убывание"),
        new(QuickPanelSort.StartedAscending, "по времени запуска, возрастание"),
    }.Select(o => { o.IsChecked = o.Value == _settings.QuickPanelSort; return o; }).ToList();
    private IReadOnlyList<PanelSortOption>? _sortOptions;

    public QuickPanelSort QuickPanelSort
    {
        get => _settings.QuickPanelSort;
        set
        {
            if (_settings.QuickPanelSort == value) return;
            _settings.QuickPanelSort = value;
            _settingsService.Save(_settings);
            foreach (var o in QuickPanelSortOptions) o.IsChecked = o.Value == value;
            OnPropertyChanged();
            RefreshPanelTimers();
        }
    }

    public RelayCommand SetQuickPanelSortCommand => _setSort ??= new RelayCommand(p => { if (p is QuickPanelSort s) QuickPanelSort = s; });
    private RelayCommand? _setSort;

    private IEnumerable<QuickTimerItem> SortForPanel(IEnumerable<QuickTimerItem> items)
    {
        var list = items.ToList(); // порядок создания — порядок строк; он же — при равенстве
        int Index(QuickTimerItem q) => list.IndexOf(q);
        return _settings.QuickPanelSort switch
        {
            QuickPanelSort.RemainingDescending => list.OrderByDescending(q => q.Panel.Remaining).ThenBy(Index),
            QuickPanelSort.RemainingAscending => list.OrderBy(q => q.Panel.Remaining).ThenBy(Index),
            // не запускавшиеся — в конце
            QuickPanelSort.StartedDescending => list.OrderBy(q => q.Panel.StartedUtc is null).ThenByDescending(q => q.Panel.StartedUtc).ThenBy(Index),
            QuickPanelSort.StartedAscending => list.OrderBy(q => q.Panel.StartedUtc is null).ThenBy(q => q.Panel.StartedUtc).ThenBy(Index),
            _ => list,
        };
    }

    // ---- Просмотр эффекта в виджете (▶/■ в списках эффектов окна «Быстрые таймеры», докладка 2026-10-01) ----

    private QuickTimerItem? _previewItem;
    private bool _previewOpenedWidget;

    /// <summary>Показать виджет для просмотра; true — он был скрыт (по окончании просмотра — скрыть снова). Задаёт MainWindow.</summary>
    internal Func<bool>? ShowWidgetForPreview { get; set; }
    /// <summary>Скрыть виджет после просмотра. Задаёт MainWindow.</summary>
    internal Action? HideWidgetAfterPreview { get; set; }

    internal void TogglePanelPreview(QuickTimerItem item, EffectOption option)
    {
        bool same = ReferenceEquals(_previewItem, item) && Equals(item.Panel.PreviewValue, option.Value);
        StopPanelPreview();
        if (same || !option.CanPreview) return;
        _previewItem = item;
        item.Panel.StartPreview(option.Value);
        item.MarkPanelPreview(option.Value);
        RefreshPanelTimers(); // синий таймер в покое — появится на время просмотра
        _previewOpenedWidget = ShowWidgetForPreview?.Invoke() ?? false;
    }

    /// <summary>■, закрытие списка, выбор пункта, закрытие окна: всё как было — синий в покое пропадает, скрытый виджет скрывается.</summary>
    internal void StopPanelPreview()
    {
        if (_previewItem is not { } item) return;
        _previewItem = null;
        item.Panel.StopPreview();
        item.MarkPanelPreview(null);
        RefreshPanelTimers();
        if (_previewOpenedWidget) HideWidgetAfterPreview?.Invoke();
        _previewOpenedWidget = false;
    }

    /// <summary>Клавиша таймера виджета: запустить в виджете заново и показать виджет.</summary>
    internal void StartPanelTimer(QuickTimerItem item)
    {
        item.Panel.StartFresh();
        QuickPanelShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool _refreshingPanel;

    /// <summary>
    /// Список виджета: оранжевые («постоянно») — всегда, синие — пока идут, на паузе или доигрывают окончание; порядок —
    /// по настройке. Идущие таймеры не сбрасываются: состояние живёт в строке.
    /// </summary>
    internal void RefreshPanelTimers()
    {
        if (_refreshingPanel) return;
        _refreshingPanel = true;
        try { RefreshPanelTimersCore(); }
        finally { _refreshingPanel = false; }
    }

    private void RefreshPanelTimersCore()
    {
        foreach (var q in QuickTimers.Where(q => !q.Model.ShowInPanel && q.Panel.IsActive)) q.Panel.Stop(); // убрали из виджета
        var all = QuickTimers.Where(q => q.Model.ShowInPanel).Select(q => q.Panel).ToList();
        if (!all.SequenceEqual(PanelSizers)) { PanelSizers.Clear(); foreach (var p in all) PanelSizers.Add(p); }
        var wanted = SortForPanel(QuickTimers.Where(q => q.Model.ShowInPanel && (q.Model.PanelPermanent || q.Panel.IsActive || q.Panel.IsPreviewing)))
            .Select(q => q.Panel).ToList();
        for (int i = PanelTimers.Count - 1; i >= 0; i--)
            if (!wanted.Contains(PanelTimers[i])) PanelTimers.RemoveAt(i);
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
                if (_settings.QuickPanelSort is QuickPanelSort.RemainingAscending or QuickPanelSort.RemainingDescending) RefreshPanelTimers();
                UpdatePanelTicker();
            };
        }
        if (_panelTicker is null) return;
        if (needed && !_panelTicker.IsEnabled) _panelTicker.Start();
        else if (!needed && _panelTicker.IsEnabled) _panelTicker.Stop();
    }

    /// <summary>
    /// Таймер панели закончился: свой звук (если включён) один раз — возвращает номер звучания (0 — без звука); панель открыть
    /// снова, если так задано крестиком.
    /// </summary>
    internal int OnPanelTimerFinished(QuickTimer timer)
    {
        int sound = 0;
        // свой звук окончания — одновременно с другими таймерами и главным звонком, каждый на своём устройстве
        if (timer.SoundEnabled) sound = _soundService.PlayIndependent(_settings, timer.Sound);
        if (QuickPanelReopenOnFinish) QuickPanelShowRequested?.Invoke(this, EventArgs.Empty);
        return sound;
    }

    /// <summary>Звучание закончилось (доиграло или прервано) — таймеры панели ждут его, чтобы закрыться.</summary>
    private void NotifyPanelSoundEnded(int number)
    {
        foreach (var q in QuickTimers) q.Panel.OnSoundEnded(number);
    }

    internal void StopPanelSound(int number) => _soundService.StopIndependent(number);

    /// <summary>Старые сочетания по умолчанию (панель — Ctrl+Shift+Insert, окно фона — Ctrl+Shift+PageUp) — поменять местами.</summary>
    private void SwapOldPanelDefaults()
    {
        var insert = HotkeyBinding.FromKey(System.Windows.Input.Key.Insert, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
        var pageUp = HotkeyBinding.FromKey(System.Windows.Input.Key.PageUp, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
        if (_settings.QuickPanelKey != insert || _settings.BackgroundWindowKey != pageUp) return;
        (_settings.QuickPanelKey, _settings.BackgroundWindowKey) = (pageUp, insert);
        _settingsService.Save(_settings);
    }
}
public enum QuickPanelState { Ready, Running, Paused, Finished }

/// <summary>Пункт «Сортировать» (контекстное меню виджета, список в окне «Быстрые таймеры»).</summary>
public sealed class PanelSortOption : INotifyPropertyChanged
{
    private bool _isChecked;
    public PanelSortOption(QuickPanelSort value, string label) { Value = value; Label = label; }
    public QuickPanelSort Value { get; }
    public string Label { get; }
    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Отсчёт быстрого таймера в панели — свой у каждой строки, независимо от главного таймера.</summary>
public sealed class QuickPanelTimer : INotifyPropertyChanged
{
    /// <summary>Имя в панели — не длиннее 15 знаков, длиннее — сокращается.</summary>
    public const int MaxNameLength = 15;

    private readonly MainViewModel _owner;
    private readonly QuickTimerItem _item;
    private DateTime? _endUtc;
    private TimeSpan _left;
    private DateTime? _effectUntil;
    private int _soundNumber;   // звучание окончания этого таймера (0 — нет или уже отыграло)
    private QuickPanelState _state;

    internal QuickPanelTimer(MainViewModel owner, QuickTimerItem item)
    {
        _owner = owner;
        _item = item;
        _left = Total;
        for (int i = 0; i < 10; i++) Segments.Add(new PanelSegment());
        PlayPauseCommand = new RelayCommand(PlayPause);
        ResetCommand = new RelayCommand(() =>
        {
            if (_state == QuickPanelState.Finished) EndFinish(); // сброс после окончания — как будто всё отыграло
            else Reset();
        });
    }

    private QuickTimer Model => _item.Model;
    private TimeSpan Total => Model.Duration;

    public string Name => _item.Name.Length > MaxNameLength ? _item.Name[..(MaxNameLength - 1)].TrimEnd() + "…" : _item.Name;
    public string FullName => _item.Name;
    public string Color => Model.PanelColor;
    public string FinishColor => Model.PanelFinishColor;
    public QuickPanelProgress Progress => PreviewValue is QuickPanelProgress p ? p : Model.PanelProgress;
    public QuickPanelFinish Finish => PreviewValue is QuickPanelFinish f ? f : Model.PanelFinish;

    /// <summary>Просматриваемый эффект (QuickPanelProgress или QuickPanelFinish); null — просмотра нет.</summary>
    internal object? PreviewValue { get; private set; }
    public bool IsPreviewing => PreviewValue is not null;

    /// <summary>Эффект хода из эффектов главного таймера идёт: таймер идёт или его ход просматривают.</summary>
    public bool IsRunningFxActive => IsRunning || PreviewValue is QuickPanelProgress;

    /// <summary>
    /// Подложка «88» под цифрами: под заливками, вспышками и эффектом окончания её тёмные сегменты проступали сквозь цвет —
    /// прячем, как на главном циферблате (докладка 2026-10-01).
    /// </summary>
    public bool ShowGhost => !IsEffectActive && Progress is not (QuickPanelProgress.Fill or QuickPanelProgress.Drain
        or QuickPanelProgress.Flash or QuickPanelProgress.Breathe);

    internal void StartPreview(object value)
    {
        PreviewValue = value;
        RaiseLook();
        Raise(nameof(IsPreviewing)); Raise(nameof(IsRunningFxActive));
        RaiseEffect();
        _owner.UpdatePanelTicker();
    }

    internal void StopPreview()
    {
        PreviewValue = null;
        RaiseLook();
        Raise(nameof(IsPreviewing)); Raise(nameof(IsRunningFxActive));
        RaiseEffect();
        _owner.UpdatePanelTicker();
    }

    public QuickPanelState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            Raise(nameof(State)); Raise(nameof(IsRunning)); Raise(nameof(PlayToolTip)); Raise(nameof(IsRunningFxActive));
            _item.RaiseRowState();
            _owner.RefreshPanelTimers(); // синий таймер появляется/пропадает в виджете
        }
    }

    /// <summary>Идёт, на паузе или доигрывает окончание (синий таймер в это время виден в виджете).</summary>
    public bool IsActive => _state != QuickPanelState.Ready;

    /// <summary>Когда запущен заново в последний раз (сортировка «по времени запуска»).</summary>
    public DateTime? StartedUtc { get; private set; }

    /// <summary>Эффект хода из эффектов главного таймера (рисует Controls/PanelRowEffect) — только пока идёт.</summary>
    public string? RunningFx => Progress is QuickPanelProgress.Flash or QuickPanelProgress.Breathe or QuickPanelProgress.Waves
        or QuickPanelProgress.Snake or QuickPanelProgress.RainbowSnake ? Progress.ToString() : null;

    /// <summary>Эффект окончания из эффектов главного таймера (строб, радуга, волны, змейки).</summary>
    public string? FinishFx => Finish is QuickPanelFinish.Strobe or QuickPanelFinish.Rainbow or QuickPanelFinish.Waves
        or QuickPanelFinish.Snake or QuickPanelFinish.RainbowSnake ? Finish.ToString() : null;

    /// <summary>Клавиша таймера: заново на всё время, что бы ни было.</summary>
    internal void StartFresh()
    {
        if (Total <= TimeSpan.Zero) return;
        if (_state == QuickPanelState.Finished) StopOwnSound();
        _effectUntil = null;
        _left = Total;
        _endUtc = DateTime.UtcNow + Total;
        StartedUtc = DateTime.UtcNow;
        State = QuickPanelState.Running;
        RaiseTime();
        RaiseEffect();
        _owner.UpdatePanelTicker();
    }

    /// <summary>Быстрое управление, клик ПКМ по строке: идёт (на паузе, доигрывает окончание) — сброс; остановленный — ничего.</summary>
    internal void QuickRightClick()
    {
        if (IsActive) ResetFromRow();
    }

    /// <summary>Кнопка плей/пауза (виджет и строка окна «Быстрые таймеры»).</summary>
    internal void PlayPauseFromRow() => PlayPause();

    /// <summary>Кнопка сброса (виджет и строка окна «Быстрые таймеры»).</summary>
    internal void ResetFromRow() => ResetCommand.Execute(null);

    public bool IsRunning => _state == QuickPanelState.Running;

    /// <summary>Идёт эффект окончания (его длительность — своя у таймера, до 20 с).</summary>
    public bool IsEffectActive => PreviewValue is QuickPanelFinish || _state == QuickPanelState.Finished && _effectUntil is { } until && DateTime.UtcNow < until;
    private bool _effectShown;

    internal bool NeedsTicks => _state == QuickPanelState.Running || _state == QuickPanelState.Finished || IsPreviewing;

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
    public double Fraction => PreviewValue is QuickPanelProgress && _state != QuickPanelState.Running
        ? DateTime.UtcNow.TimeOfDay.TotalMilliseconds % 4000 / 4000 // просмотр хода: заливка/полоски бегут по кругу за 4 с
        : Total <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining.TotalSeconds / Total.TotalSeconds, 0, 1);
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
                StartFresh();
                return;
        }
        RaiseTime();
        RaiseEffect();
        _owner.UpdatePanelTicker();
    }

    internal void Reset()
    {
        _endUtc = null;
        _effectUntil = null;
        _left = Total;
        State = QuickPanelState.Ready;
        RaiseTime();
        RaiseEffect();
        _owner.UpdatePanelTicker();
    }

    /// <summary>Строку убрали из панели — отсчёт останавливается (и звук окончания, если ещё играет).</summary>
    internal void Stop()
    {
        if (_state == QuickPanelState.Finished) StopOwnSound();
        Reset();
    }

    /// <summary>Время таймера поменяли в настройках — в покое сразу показать новое.</summary>
    internal void OnDurationChanged()
    {
        if (_state == QuickPanelState.Ready) _left = Total;
        RaiseTime();
    }

    internal void Tick(DateTime now)
    {
        if (IsPreviewing && _state != QuickPanelState.Running) RaiseTime();
        if (_state == QuickPanelState.Running && _endUtc is { } end)
        {
            if (now >= end)
            {
                _endUtc = null;
                _left = TimeSpan.Zero;
                int seconds = Model.PanelFinish == QuickPanelFinish.None ? 0 : Math.Clamp(Model.PanelFinishSeconds, 0, 20);
                _effectUntil = now + TimeSpan.FromSeconds(seconds);
                State = QuickPanelState.Finished;
                _soundNumber = _owner.OnPanelTimerFinished(Model);
            }
            RaiseTime();
            RaiseEffect();
        }
        else if (_state == QuickPanelState.Finished)
        {
            RaiseEffect();
            // закрыть (или сбросить) — когда отыграли и эффект, и звук: что дольше
            if (!IsEffectActive && _soundNumber == 0) EndFinish();
        }
    }

    /// <summary>Звучание закончилось — если это звук окончания этого таймера, больше его не ждём.</summary>
    internal void OnSoundEnded(int number)
    {
        if (number != 0 && number == _soundNumber) _soundNumber = 0;
    }

    private void StopOwnSound()
    {
        if (_soundNumber == 0) return;
        int number = _soundNumber;
        _soundNumber = 0;
        _owner.StopPanelSound(number);
    }

    /// <summary>
    /// Всё отыграло (или сброс после окончания): сбросить. Синий таймер при этом пропадает из виджета (настройка остаётся —
    /// клавиша или кнопка в окне «Быстрые таймеры» покажет его снова), оранжевый остаётся.
    /// </summary>
    private void EndFinish()
    {
        StopOwnSound();
        Reset();
    }

    private void RaiseEffect()
    {
        bool active = IsEffectActive;
        if (active == _effectShown) return;
        _effectShown = active;
        Raise(nameof(IsEffectActive));
        Raise(nameof(ShowGhost));
    }

    private void RaiseTime()
    {
        Raise(nameof(Remaining)); Raise(nameof(TimeText)); Raise(nameof(GhostText)); Raise(nameof(Fraction)); Raise(nameof(RemainingFraction));
        int lit = (int)Math.Floor(Fraction * Segments.Count + 0.0001);
        for (int i = 0; i < Segments.Count; i++) Segments[i].IsLit = i < lit;
    }

    /// <summary>Имя, цвета, эффекты поменяли в окне «Быстрые таймеры».</summary>
    internal void RaiseLook()
    {
        foreach (var n in new[] { nameof(Name), nameof(FullName), nameof(Color), nameof(FinishColor), nameof(Progress), nameof(Finish), nameof(RunningFx), nameof(FinishFx), nameof(ShowGhost) }) Raise(n);
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

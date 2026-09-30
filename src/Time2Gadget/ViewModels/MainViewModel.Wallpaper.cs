using System.IO;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Фон рабочего стола (Services/WallpaperService): «Закрепить фоны» (докладка 2026-09-29) и своя картинка с режимом
/// на каждый монитор (докладка 2026-09-30). Настраивается в окне «Заставка и фон экрана» (Views/DesktopBackgroundWindow).
/// </summary>
public sealed partial class MainViewModel
{
    // ---- Закрепить фоны ----
    private System.Windows.Threading.DispatcherTimer? _wallpaperTimer;

    /// <summary>
    /// Закрепить фоны: слайд-шоу Windows останавливается, на каждом мониторе остаётся его картинка. Пока галочка стоит,
    /// раз в минуту проверяем: слайд-шоу снова включили в Windows — закрепляем новые картинки. Сняли — слайд-шоу возвращается
    /// (кроме случая, когда стоит своя картинка: тогда слайд-шоу вернёт «✕» у картинки).
    /// </summary>
    public bool PinWallpapers
    {
        get => _settings.PinWallpapers;
        set
        {
            if (_settings.PinWallpapers == value) return;
            _settings.PinWallpapers = value;
            if (value) PinWallpapersNow();
            else
            {
                _wallpaperTimer?.Stop();
                if (_settings.WallpaperSlideshowBackup is { } backup)
                {
                    if (HasOwnWallpaper)
                    {
                        if (_settings.WallpaperBefore is { Slideshow: null } before) before.Slideshow = backup;
                    }
                    else if (!WallpaperService.IsSlideshow()) WallpaperService.Restore(backup);
                }
                _settings.WallpaperSlideshowBackup = null;
            }
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinWallpapersStatus));
        }
    }

    public string PinWallpapersStatus => !PinWallpapers ? string.Empty
        : _settings.WallpaperSlideshowBackup is not null
            ? "Слайд-шоу остановлено — картинки на мониторах закреплены."
            : "Слайд-шоу Windows сейчас не идёт — закреплять нечего. Включите его (Параметры → Персонализация → Фон), и через минуту картинки закрепятся.";

    /// <summary>При запуске и по галочке: закрепить, если идёт слайд-шоу; дальше — проверка раз в минуту.</summary>
    public void PinWallpapersNow()
    {
        if (!_settings.PinWallpapers) return;
        if (WallpaperService.Pin() is { } backup)
        {
            _settings.WallpaperSlideshowBackup = backup;
            _settingsService.Save(_settings);
            OnPropertyChanged(nameof(PinWallpapersStatus));
        }
        if (_wallpaperTimer is null)
        {
            _wallpaperTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _wallpaperTimer.Tick += (_, _) => PinWallpapersNow();
        }
        _wallpaperTimer.Start();
    }

    // ================= «Заставка и фон экрана»: два режима — статичная заставка и слайдшоу (докладка 2026-09-30) =================
    // Включён может быть только один: включение одного выключает другой. Фон до своих настроек (WallpaperBefore) общий:
    // запоминается при первом применении, возвращается при выключении режима.

    /// <summary>Изменились настройки фона — окно «Заставка и фон экрана» перечитывает их (после «✕», импорта, сброса и т.п.).</summary>
    public event EventHandler? WallpaperChanged;

    private void RaiseWallpaperChanged()
    {
        OnPropertyChanged(nameof(StaticWallpaperEnabled));
        OnPropertyChanged(nameof(SlideshowEnabled));
        OnPropertyChanged(nameof(IsStaticSectionEnabled));
        OnPropertyChanged(nameof(IsSlideshowSectionEnabled));
        RaiseSlideshowScheduleChanged();
        WallpaperChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Вернуть фон, который был до своих настроек (выключили режим, «✕» правым щелчком).</summary>
    private void RestoreBackgroundBefore()
    {
        var before = _settings.WallpaperBefore;
        _settings.WallpaperBefore = null;
        _settingsService.Save(_settings);
        _appliedWallpaperSignature = null;
        _appliedSlideshowSignature = null;
        if (before is not null) WallpaperService.RestoreState(before);
    }

    private void EnsureBackgroundCaptured()
    {
        if (_settings.WallpaperBefore is null && WallpaperService.Capture() is { } captured)
        {
            _settings.WallpaperBefore = captured;
            _settingsService.Save(_settings);
        }
    }

    /// <summary>Прежний фон монитора (как его показывала Windows); не запомнен — чёрный.</summary>
    private MonitorWallpaperPlan PreviousBackgroundPlan(WallpaperMonitor m) =>
        _settings.WallpaperBefore is { } before && before.Images.TryGetValue(m.Id, out var original)
            ? new MonitorWallpaperPlan(m, original, WallpaperService.FitFromPosition(before.Position), null)
            : new MonitorWallpaperPlan(m, null, WallpaperFit.None, null);

    // ---- Статичная заставка / сплошной фон ----

    internal string? WallpaperImage => _settings.WallpaperImage;

    internal WallpaperFit WallpaperFitFor(string monitorId) =>
        _settings.WallpaperModes.TryGetValue(monitorId, out var fit) ? fit : WallpaperFit.Stretch;

    /// <summary>Сплошной цвет монитора («#RRGGBB») или null — у монитора прежний фон.</summary>
    internal string? WallpaperColorFor(string monitorId) =>
        _settings.WallpaperColors.TryGetValue(monitorId, out var color) ? color : null;

    /// <summary>Задана своя статичная настройка: картинка или хотя бы один сплошной цвет.</summary>
    internal bool HasOwnWallpaper => _settings.WallpaperImage is not null || _settings.WallpaperColors.Count > 0;

    /// <summary>Тумблер «Статичная заставка / сплошной фон»: включён — настройка на экране, выключен — прежний фон (настройка хранится).</summary>
    public bool StaticWallpaperEnabled
    {
        get => _settings.StaticWallpaperEnabled ?? HasOwnWallpaper;
        set
        {
            if (value == StaticWallpaperEnabled) return;
            if (value)
            {
                if (_settings.SlideshowEnabled) StopSlideshow(restore: false);
                _settings.StaticWallpaperEnabled = true;
                _settingsService.Save(_settings);
                ApplyWallpaper(force: true);
            }
            else
            {
                _settings.StaticWallpaperEnabled = false;
                _settingsService.Save(_settings);
                RestoreBackgroundBefore();
            }
            RaiseWallpaperChanged();
        }
    }

    /// <summary>Раздел статичной заставки доступен, пока не включено слайдшоу.</summary>
    public bool IsStaticSectionEnabled => !SlideshowEnabled;

    private string? _appliedWallpaperSignature; // что поставлено сейчас: картинка + мониторы + режимы + цвета

    /// <summary>Выбрать картинку; при включённом режиме — сразу на мониторы.</summary>
    internal bool SetWallpaperImage(string? image)
    {
        _settings.WallpaperImage = image;
        _settingsService.Save(_settings);
        return !StaticWallpaperEnabled || ApplyWallpaper(force: true);
    }

    /// <summary>Режим монитора (кнопка «Мон N»).</summary>
    internal bool SetWallpaperFit(string monitorId, WallpaperFit fit)
    {
        if (fit == WallpaperFit.Stretch) _settings.WallpaperModes.Remove(monitorId);
        else _settings.WallpaperModes[monitorId] = fit;
        _settingsService.Save(_settings);
        return !StaticWallpaperEnabled || ApplyWallpaper(force: true);
    }

    /// <summary>Сплошной цвет монитора без картинки (квадратик у кнопки монитора).</summary>
    internal bool SetWallpaperColor(string monitorId, string hex)
    {
        _settings.WallpaperColors[monitorId] = hex;
        _settingsService.Save(_settings);
        return !StaticWallpaperEnabled || ApplyWallpaper(force: true);
    }

    /// <summary>
    /// Поставить статичную настройку: на каждый монитор — картинку в его режиме, а без картинки («не отображать» или картинки
    /// нет) — его сплошной цвет, иначе его прежний фон. Без <paramref name="force"/> — только если с прошлого раза поменялись
    /// мониторы (подключили монитор, сменили разрешение).
    /// </summary>
    internal bool ApplyWallpaper(bool force)
    {
        if (!StaticWallpaperEnabled || !HasOwnWallpaper) return false;
        var monitors = WallpaperService.Monitors();
        var signature = WallpaperSignature(monitors);
        if (!force && signature == _appliedWallpaperSignature) return true;
        EnsureBackgroundCaptured();
        var plans = monitors.Select(m =>
        {
            var color = WallpaperColorFor(m.Id) is { } hex ? WallpaperService.ParseColor(hex) : (System.Drawing.Color?)null;
            var fit = WallpaperFitFor(m.Id);
            if (_settings.WallpaperImage is { } image && fit != WallpaperFit.None)
                return new MonitorWallpaperPlan(m, image, fit, color);
            return color is null ? PreviousBackgroundPlan(m) : new MonitorWallpaperPlan(m, null, WallpaperFit.None, color);
        }).ToList();
        bool ok = WallpaperService.Apply(plans);
        if (ok) _appliedWallpaperSignature = signature;
        return ok;
    }

    /// <summary>«✕» (правый щелчок): своя картинка, режимы и цвета сняты, фон — как был до них.</summary>
    internal void ClearWallpaper()
    {
        _settings.WallpaperImage = null;
        _settings.WallpaperModes.Clear();
        _settings.WallpaperColors.Clear();
        _settingsService.Save(_settings);
        if (StaticWallpaperEnabled || _settings.WallpaperBefore is not null && !_settings.SlideshowEnabled) RestoreBackgroundBefore();
        RaiseWallpaperChanged();
    }

    /// <summary>«✕» (левый щелчок): картинка и режимы сняты, на всех мониторах — сплошной чёрный (цвет каждого можно сменить).</summary>
    internal void BlackoutWallpaper()
    {
        _settings.WallpaperImage = null;
        _settings.WallpaperModes.Clear();
        _settings.WallpaperColors = WallpaperService.Monitors().ToDictionary(m => m.Id, _ => "#000000");
        _settingsService.Save(_settings);
        ApplyWallpaper(force: true);
        RaiseWallpaperChanged();
    }

    private string WallpaperSignature(IEnumerable<WallpaperMonitor> monitors) =>
        _settings.WallpaperImage + "|" + string.Join(";", monitors.Select(m => $"{m.Id}={m.Bounds}:{WallpaperFitFor(m.Id)}:{WallpaperColorFor(m.Id)}"));

    // ---- Динамичная заставка / слайдшоу ----

    private System.Windows.Threading.DispatcherTimer? _slideshowTimer;
    private string? _appliedSlideshowSignature;
    private WallpaperUnderlay? _underlay;     // основной способ показа слайдшоу — своё окно-подложка
    private bool _underlayFailed;             // встроиться не удалось — показываем средствами Windows
    private SlideshowSettings Slideshow => _settings.Slideshow ??= new SlideshowSettings();

    /// <summary>Тумблер «Динамичная заставка / слайдшоу»: включён — шаги по расписанию, выключен — прежний фон.</summary>
    public bool SlideshowEnabled
    {
        get => _settings.SlideshowEnabled;
        set
        {
            if (value == _settings.SlideshowEnabled) return;
            if (value)
            {
                if (StaticWallpaperEnabled) { _settings.StaticWallpaperEnabled = false; _appliedWallpaperSignature = null; }
                _settings.SlideshowEnabled = true;
                Slideshow.Epoch = DateTime.Now; // шаг 1 — с этого момента
                _settingsService.Save(_settings);
                ApplySlideshow(force: true);
            }
            else StopSlideshow(restore: true);
            RaiseWallpaperChanged();
        }
    }

    /// <summary>Раздел слайдшоу доступен, пока не включена статичная заставка.</summary>
    public bool IsSlideshowSectionEnabled => !StaticWallpaperEnabled;

    private void StopSlideshow(bool restore)
    {
        _slideshowTimer?.Stop();
        _underlay?.Hide();
        _underlayFailed = false;
        _settings.SlideshowEnabled = false;
        _settingsService.Save(_settings);
        _appliedSlideshowSignature = null;
        if (restore) RestoreBackgroundBefore();
    }

    // Расписание: списки в окне — по индексам (порядок пунктов — как в запросе пользователя).

    /// <summary>0 — раз в час, 1 — раз в сутки, 2 — свой интервал.</summary>
    public int SlideshowKindIndex
    {
        get => (int)Slideshow.Kind;
        set => ChangeSchedule(() => Slideshow.Kind = (SlideshowKind)Math.Clamp(value, 0, 2));
    }

    /// <summary>Раз в час: 0 — указать точное время старта, 1 — каждый реальный час.</summary>
    public int SlideshowHourlyModeIndex
    {
        get => Slideshow.HourlyFromTime ? 0 : 1;
        set => ChangeSchedule(() => Slideshow.HourlyFromTime = value == 0);
    }

    /// <summary>Раз в сутки: 0 — указать точное время смены, 1 — в начале суток (00:00).</summary>
    public int SlideshowDailyModeIndex
    {
        get => Slideshow.DailyAtTime ? 0 : 1;
        set => ChangeSchedule(() => Slideshow.DailyAtTime = value == 0);
    }

    /// <summary>Свой интервал: 0 — старт сейчас, 1 — старт в указанное время.</summary>
    public int SlideshowIntervalModeIndex
    {
        get => Slideshow.IntervalStartAt ? 1 : 0;
        set => ChangeSchedule(() => Slideshow.IntervalStartAt = value == 1);
    }

    public TimeSpan SlideshowHourlyStart { get => Slideshow.HourlyStart; set => ChangeSchedule(() => Slideshow.HourlyStart = value); }
    public TimeSpan SlideshowDailyTime { get => Slideshow.DailyTime; set => ChangeSchedule(() => Slideshow.DailyTime = value); }
    /// <summary>Свой интервал: не меньше минимума способа показа (Windows — 2 с, подложка — 1 с); меньше — поле поправляется само.</summary>
    public TimeSpan SlideshowInterval
    {
        get => Slideshow.Interval < SlideshowSchedule.MinInterval(Slideshow) ? SlideshowSchedule.MinInterval(Slideshow) : Slideshow.Interval;
        set
        {
            var min = SlideshowSchedule.MinInterval(Slideshow);
            ChangeSchedule(() => Slideshow.Interval = value < min ? min : value);
            if (value < min) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SlideshowInterval)));
        }
    }
    public TimeSpan SlideshowIntervalStart { get => Slideshow.IntervalStart; set => ChangeSchedule(() => Slideshow.IntervalStart = value); }

    public bool IsSlideshowHourly => Slideshow.Kind == SlideshowKind.Hourly;
    public bool IsSlideshowDaily => Slideshow.Kind == SlideshowKind.Daily;
    public bool IsSlideshowInterval => Slideshow.Kind == SlideshowKind.Interval;
    public bool ShowSlideshowHourlyTime => IsSlideshowHourly && Slideshow.HourlyFromTime;
    public bool ShowSlideshowDailyTime => IsSlideshowDaily && Slideshow.DailyAtTime;
    public bool ShowSlideshowIntervalStart => IsSlideshowInterval && Slideshow.IntervalStartAt;

    /// <summary>Способ показа: 0 — своё окно-подложка (эффекты), 1 — средствами Windows (без эффектов, по умолчанию).</summary>
    public int SlideshowDisplayIndex
    {
        get => Slideshow.UseUnderlay ? 0 : 1;
        set => ChangeDisplay(() =>
        {
            Slideshow.UseUnderlay = value == 0;
            _underlayFailed = false;
            // средствами Windows чаще раза в 2 с не успевает — короткий интервал поднять до минимума
            if (Slideshow.Interval < SlideshowSchedule.MinInterval(Slideshow)) Slideshow.Interval = SlideshowSchedule.MinInterval(Slideshow);
        });
    }

    /// <summary>Эффект смены (порядок — как в <see cref="SlideshowEffect"/>).</summary>
    public int SlideshowEffectIndex
    {
        get => (int)Slideshow.Effect;
        set => ChangeDisplay(() => Slideshow.Effect = (SlideshowEffect)Math.Clamp(value, 0, 7));
    }

    public bool IsSlideshowUnderlay => Slideshow.UseUnderlay;
    public bool IsSlideshowWindows => !Slideshow.UseUnderlay;

    /// <summary>Способ показа или эффект изменили — шаг не сбрасывается, показ — заново.</summary>
    private void ChangeDisplay(Action change)
    {
        change();
        _settingsService.Save(_settings);
        RaiseSlideshowScheduleChanged();
        if (SlideshowEnabled) ApplySlideshow(force: true);
    }

    /// <summary>Расписание изменили — шаг 1 заново с этого момента.</summary>
    private void ChangeSchedule(Action change)
    {
        change();
        Slideshow.Epoch = DateTime.Now;
        _settingsService.Save(_settings);
        RaiseSlideshowScheduleChanged();
        if (SlideshowEnabled) ApplySlideshow(force: true);
    }

    private void RaiseSlideshowScheduleChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(SlideshowKindIndex), nameof(SlideshowHourlyModeIndex), nameof(SlideshowDailyModeIndex), nameof(SlideshowIntervalModeIndex),
                     nameof(SlideshowHourlyStart), nameof(SlideshowDailyTime), nameof(SlideshowInterval), nameof(SlideshowIntervalStart),
                     nameof(IsSlideshowHourly), nameof(IsSlideshowDaily), nameof(IsSlideshowInterval),
                     nameof(ShowSlideshowHourlyTime), nameof(ShowSlideshowDailyTime), nameof(ShowSlideshowIntervalStart), nameof(SlideshowStatus),
                     nameof(SlideshowDisplayIndex), nameof(SlideshowEffectIndex), nameof(IsSlideshowUnderlay), nameof(IsSlideshowWindows),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>30 шагов монитора (копия; null — пусто).</summary>
    internal List<SlideshowSlot?> SlideshowSlotsFor(string monitorId)
    {
        var slots = Slideshow.Monitors.TryGetValue(monitorId, out var list) ? list.Select(s => s?.Clone()).ToList() : new List<SlideshowSlot?>();
        while (slots.Count < SlideshowSettings.SlotCount) slots.Add(null);
        return slots.Take(SlideshowSettings.SlotCount).ToList();
    }

    /// <summary>Своя длина цикла монитора (тумблер «Шагов в цикле») или null — по заполненным шагам.</summary>
    internal int? SlideshowCycleSetting(string monitorId) =>
        Slideshow.CycleLengths.TryGetValue(monitorId, out var n) ? n : null;

    /// <summary>Номер последнего заполненного шага (0 — шагов нет).</summary>
    internal int SlideshowLastFilled(string monitorId) =>
        Slideshow.Monitors.TryGetValue(monitorId, out var list) ? list.FindLastIndex(s => s is { IsEmpty: false }) + 1 : 0;

    /// <summary>
    /// Длина цикла монитора (решение пользователя 2026-09-30): тумблер включён — выбранное число; выключен — номер последнего
    /// заполненного шага (1 и 5 → 5, только 13 → 13). Шагов нет вовсе — 0 (на мониторе прежний фон).
    /// </summary>
    internal int SlideshowCycleLength(string monitorId) =>
        SlideshowLastFilled(monitorId) == 0 ? 0 : SlideshowCycleSetting(monitorId) ?? SlideshowLastFilled(monitorId);

    /// <summary>Сохранить шаги монитора и его длину цикла (null — по заполненным шагам).</summary>
    internal void SetSlideshowSlots(string monitorId, IEnumerable<SlideshowSlot?> slots, int? cycleLength)
    {
        var list = slots.Select(s => s is { IsEmpty: false } ? s.Clone() : null).ToList();
        if (list.All(s => s is null)) Slideshow.Monitors.Remove(monitorId);
        else Slideshow.Monitors[monitorId] = list;
        if (cycleLength is { } n) Slideshow.CycleLengths[monitorId] = Math.Clamp(n, 2, SlideshowSettings.SlotCount);
        else Slideshow.CycleLengths.Remove(monitorId);
        _settingsService.Save(_settings);
        if (SlideshowEnabled) ApplySlideshow(force: true);
        RaiseWallpaperChanged();
    }

    /// <summary>«✕» у монитора слайдшоу (левый щелчок): шаги убрать (запомнив их).</summary>
    internal void ClearSlideshowMonitor(string monitorId)
    {
        if (!Slideshow.Monitors.TryGetValue(monitorId, out var slots)) return;
        Slideshow.Cleared[monitorId] = slots;
        Slideshow.Monitors.Remove(monitorId);
        _settingsService.Save(_settings);
        if (SlideshowEnabled) ApplySlideshow(force: true);
        RaiseWallpaperChanged();
    }

    /// <summary>«✕» у монитора слайдшоу (правый щелчок): вернуть убранные шаги.</summary>
    internal void RestoreSlideshowMonitor(string monitorId)
    {
        if (!Slideshow.Cleared.Remove(monitorId, out var slots)) return;
        Slideshow.Monitors[monitorId] = slots;
        _settingsService.Save(_settings);
        if (SlideshowEnabled) ApplySlideshow(force: true);
        RaiseWallpaperChanged();
    }

    internal bool CanRestoreSlideshowMonitor(string monitorId) => Slideshow.Cleared.ContainsKey(monitorId);

    /// <summary>Длина общего цикла: по монитору, где шагов больше (решение пользователя 2026-10-01).</summary>
    private int SlideshowGlobalLength(IEnumerable<WallpaperMonitor> monitors) =>
        monitors.Select(m => SlideshowCycleLength(m.Id)).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Шаг монитора (0-based) после <paramref name="changes"/> смен: общий цикл — по монитору с большим числом шагов; где шаги
    /// монитора закончились, остаётся его последний шаг. -1 — у монитора шагов нет (прежний фон).
    /// </summary>
    private int SlideshowStepFor(string monitorId, long changes, int globalLength)
    {
        int n = SlideshowCycleLength(monitorId);
        if (n <= 0 || globalLength <= 0) return -1;
        return (int)Math.Min(changes % globalLength, n - 1);
    }

    /// <summary>Предупреждение, когда у мониторов разное число шагов (пусто — одинаковое).</summary>
    internal string SlideshowLengthsWarning(IReadOnlyList<WallpaperMonitor> monitors)
    {
        var lengths = monitors.Select(m => (m.Number, Length: SlideshowCycleLength(m.Id))).Where(x => x.Length > 0).ToList();
        if (lengths.Select(x => x.Length).Distinct().Count() <= 1) return string.Empty;
        return "Число шагов у мониторов разное (" + string.Join(", ", lengths.Select(x => $"монитор {x.Number} — {x.Length}")) +
               "): цикл идёт по большему, а на мониторе, где шаги закончились, до конца цикла остаётся его последняя картинка.";
    }

    /// <summary>
    /// Поставить текущий шаг: смены — для всех мониторов разом, каждый монитор — по кругу своей длины; пустой шаг — прежний фон
    /// монитора. Без <paramref name="force"/> — только если шаги или мониторы поменялись. Дальше — таймер до следующей смены.
    /// </summary>
    internal void ApplySlideshow(bool force)
    {
        if (!SlideshowEnabled) return;
        // Проводник перезапустился — подложка пропала вместе с ним: встроить заново, даже если шаг тот же.
        if (Slideshow.UseUnderlay && !_underlayFailed && _underlay is { IsHostAlive: false }) force = true;
        var monitors = WallpaperService.Monitors();
        long changes = SlideshowSchedule.StepsSinceStart(Slideshow, DateTime.Now);
        int global = SlideshowGlobalLength(monitors);
        var signature = string.Join(";", monitors.Select(m => $"{m.Id}={m.Bounds}:{SlideshowStepFor(m.Id, changes, global)}"));
        if (force || signature != _appliedSlideshowSignature)
        {
            EnsureBackgroundCaptured();
            var plans = monitors.Select(m =>
            {
                int step = SlideshowStepFor(m.Id, changes, global);
                var slot = step >= 0 && Slideshow.Monitors.TryGetValue(m.Id, out var list) && step < list.Count ? list[step] : null;
                if (slot?.Image is { } image && File.Exists(image)) return new MonitorWallpaperPlan(m, image, slot.Fit, null);
                if (slot?.Color is { } hex) return new MonitorWallpaperPlan(m, null, WallpaperFit.None, WallpaperService.ParseColor(hex));
                return PreviousBackgroundPlan(m);
            }).ToList();
            if (ShowSlideshowFrames(plans)) _appliedSlideshowSignature = signature;
        }
        OnPropertyChanged(nameof(SlideshowStatus));
        ScheduleSlideshowTimer();
    }

    /// <summary>Показать шаг: подложкой (основной способ, с эффектом) или средствами Windows (запасной / подложка недоступна).</summary>
    private bool ShowSlideshowFrames(IReadOnlyList<MonitorWallpaperPlan> plans)
    {
        if (Slideshow.UseUnderlay)
        {
            try
            {
                _underlay ??= new WallpaperUnderlay();
                var frames = WallpaperService.PrepareFiles(plans, WallpaperService.WindowsBackground());
                if (_underlay.Show(frames, Slideshow.Effect))
                {
                    _underlayFailed = false;
                    WallpaperService.CleanupGeneratedExcept(frames.Select(f => f.File));
                    return true;
                }
            }
            catch { /* ниже — средствами Windows */ }
            _underlayFailed = true;
        }
        else _underlay?.Hide();
        return WallpaperService.Apply(plans);
    }

    /// <summary>
    /// Выход из программы: подложка исчезнет вместе с программой — на её место текущий шаг обычным фоном Windows, чтобы на
    /// мониторах не осталось старой картинки.
    /// </summary>
    public void OnAppExit()
    {
        if (!SlideshowEnabled || _underlay is not { IsHostAlive: true }) return;
        _underlay.Hide();
        var saved = Slideshow.UseUnderlay;
        Slideshow.UseUnderlay = false; // только на этот показ (в настройки не сохраняется)
        ApplySlideshow(force: true);
        Slideshow.UseUnderlay = saved;
        _slideshowTimer?.Stop();
    }

    private void ScheduleSlideshowTimer()
    {
        if (_slideshowTimer is null)
        {
            _slideshowTimer = new System.Windows.Threading.DispatcherTimer();
            _slideshowTimer.Tick += (_, _) => { _slideshowTimer.Stop(); ApplySlideshow(force: false); };
        }
        // До следующей смены, но не дольше минуты: после сна и перевода часов шаг сам выправится.
        var wait = SlideshowSchedule.NextChange(Slideshow, DateTime.Now) - DateTime.Now + TimeSpan.FromMilliseconds(300);
        _slideshowTimer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : wait;
        _slideshowTimer.Stop();
        _slideshowTimer.Start();
    }

    /// <summary>«Сейчас шаг 3 из 5 · следующая смена в 15:00» (+ предупреждения) — под настройками слайдшоу.</summary>
    public string SlideshowStatus
    {
        get
        {
            if (!SlideshowEnabled) return string.Empty;
            var monitors = WallpaperService.Monitors();
            var now = DateTime.Now;
            long changes = SlideshowSchedule.StepsSinceStart(Slideshow, now);
            int global = SlideshowGlobalLength(monitors);
            if (global == 0) return "Шаги не заполнены — на мониторах прежний фон. Нажмите на монитор, чтобы разложить картинки по шагам.";
            var next = SlideshowSchedule.NextChange(Slideshow, now);
            var when = next.Date == now.Date ? next.ToString("HH:mm:ss") : next.ToString("dd.MM HH:mm:ss");
            var text = $"Сейчас шаг {changes % global + 1} из {global} · следующая смена в {when}";
            if (global == 1) text += "\nДля слайдшоу нужно минимум 2 шага — пока картинка не меняется.";
            if (Slideshow.UseUnderlay && _underlayFailed) text += "\nОкно-подложку встроить не удалось — показываем средствами Windows (без эффектов).";
            if (SlideshowLengthsWarning(monitors) is { Length: > 0 } warning) text += "\n" + warning;
            return text;
        }
    }

    // ---- Общее: запуск, смена мониторов, картинки, экспорт/импорт ----

    /// <summary>При запуске: статичная — уже стоит (Windows её помнит), запомнить мониторы; слайдшоу — текущий шаг и таймер.</summary>
    public void StartWallpaperFeatures()
    {
        if (SlideshowEnabled) ApplySlideshow(force: false);
        else if (StaticWallpaperEnabled && HasOwnWallpaper) _appliedWallpaperSignature = WallpaperSignature(WallpaperService.Monitors());
    }

    /// <summary>Мониторы поменялись или компьютер проснулся (MainWindow) — своя настройка под новые размеры/текущий шаг.</summary>
    public void ReapplyWallpaperIfNeeded()
    {
        if (SlideshowEnabled) ApplySlideshow(force: false);
        else ApplyWallpaper(force: false);
    }

    /// <summary>Сброс настроек: оба режима выключены, фон — как был до своих настроек.</summary>
    internal void ResetWallpaperFeatures()
    {
        _slideshowTimer?.Stop();
        _underlay?.Hide();
        if (StaticWallpaperEnabled || SlideshowEnabled) RestoreBackgroundBefore();
    }

    /// <summary>Добавить картинки в папку программы (не больше 30 всего).</summary>
    internal (List<string> Imported, int Skipped) ImportLibraryImages(IEnumerable<string> files) => WallpaperService.ImportImages(files);

    /// <summary>
    /// Удалить картинку из папки программы безвозвратно; где она была выбрана (статичная заставка, шаги слайдшоу) — там пусто.
    /// </summary>
    internal bool DeleteLibraryImage(string path)
    {
        bool Same(string? p) => p is not null && string.Equals(p, path, StringComparison.OrdinalIgnoreCase);
        if (Same(_settings.WallpaperImage)) _settings.WallpaperImage = null;
        foreach (var dict in new[] { Slideshow.Monitors, Slideshow.Cleared })
            foreach (var list in dict.Values)
                for (int i = 0; i < list.Count; i++)
                    if (Same(list[i]?.Image)) list[i] = null;
        _settingsService.Save(_settings);
        // Сначала убрать с экрана (Windows может держать файл), потом удалить.
        if (StaticWallpaperEnabled) ApplyWallpaper(force: true);
        if (SlideshowEnabled) ApplySlideshow(force: true);
        bool ok = WallpaperService.DeleteImage(path);
        RaiseWallpaperChanged();
        return ok;
    }

    /// <summary>Экспорт: все картинки папки программы и настройки статичной заставки и слайдшоу — одним архивом.</summary>
    internal void ExportWallpaper(string zipPath)
    {
        var current = WallpaperService.Monitors();
        var numbers = current.ToDictionary(m => m.Id, m => m.Number);
        int? Num(string id) => numbers.TryGetValue(id, out var n) ? n : null;
        string? Name(string? path) => path is null ? null : Path.GetFileName(path);
        var s = Slideshow;
        var data = new WallpaperPackageData
        {
            StaticEnabled = StaticWallpaperEnabled,
            StaticImage = Name(_settings.WallpaperImage),
            StaticModes = _settings.WallpaperModes.Where(kv => Num(kv.Key) is not null).ToDictionary(kv => Num(kv.Key)!.Value, kv => kv.Value),
            StaticColors = _settings.WallpaperColors.Where(kv => Num(kv.Key) is not null).ToDictionary(kv => Num(kv.Key)!.Value, kv => kv.Value),
            SlideshowEnabled = SlideshowEnabled,
            Kind = s.Kind, UseUnderlay = s.UseUnderlay, Effect = s.Effect, HourlyFromTime = s.HourlyFromTime, HourlyStart = s.HourlyStart, DailyAtTime = s.DailyAtTime, DailyTime = s.DailyTime,
            Interval = s.Interval, IntervalStartAt = s.IntervalStartAt, IntervalStart = s.IntervalStart,
            SlideshowMonitors = s.Monitors.Where(kv => Num(kv.Key) is not null).ToDictionary(kv => Num(kv.Key)!.Value,
                kv => kv.Value.Select(slot => slot is null ? null : new SlideshowSlot { Image = Name(slot.Image), Color = slot.Color, Fit = slot.Fit }).ToList()),
            CycleLengths = s.CycleLengths.Where(kv => Num(kv.Key) is not null).ToDictionary(kv => Num(kv.Key)!.Value, kv => kv.Value),
            MonitorSizes = current.ToDictionary(m => m.Number, m => $"{m.Bounds.Width}×{m.Bounds.Height}"),
        };
        WallpaperPackage.Export(zipPath, data, WallpaperService.ListImages());
    }

    /// <summary>
    /// Импорт: картинки — в папку программы; настройки заменяют свои. <paramref name="monitorMap"/> — сопоставление мониторов:
    /// номер монитора в архиве → текущий монитор (нет в словаре — настройки этого монитора не переносятся).
    /// <paramref name="apply"/> — включить режим, который был включён при экспорте; иначе только скопировать (оба режима
    /// выключены). Возвращает текст для пользователя.
    /// </summary>
    internal string ImportWallpaper(string zipPath, bool apply, IReadOnlyDictionary<int, string> monitorMap)
    {
        var (data, images, skipped) = WallpaperPackage.Import(zipPath);
        var skippedText = skipped > 0 ? $" Не поместились {skipped} (в папке программы не больше {WallpaperService.LibraryLimit} картинок)." : "";
        if (data is null) return $"Картинок скопировано: {images.Count}. Настроек в архиве нет.{skippedText}";

        // выключить текущий режим (фон — как был), затем заменить настройки
        if (SlideshowEnabled) StopSlideshow(restore: true);
        else if (StaticWallpaperEnabled) { _settings.StaticWallpaperEnabled = false; RestoreBackgroundBefore(); }

        string? Id(int number) => monitorMap.TryGetValue(number, out var id) ? id : null;
        string? CopyOf(string? name) => name is not null && images.TryGetValue(name, out var p) ? p : null;
        _settings.WallpaperImage = CopyOf(data.StaticImage);
        _settings.WallpaperModes = data.StaticModes.Where(kv => Id(kv.Key) is not null).ToDictionary(kv => Id(kv.Key)!, kv => kv.Value);
        _settings.WallpaperColors = data.StaticColors.Where(kv => Id(kv.Key) is not null).ToDictionary(kv => Id(kv.Key)!, kv => kv.Value);
        var s = Slideshow;
        (s.Kind, s.HourlyFromTime, s.HourlyStart, s.DailyAtTime, s.DailyTime, s.Interval, s.IntervalStartAt, s.IntervalStart) =
            (data.Kind, data.HourlyFromTime, data.HourlyStart, data.DailyAtTime, data.DailyTime, data.Interval, data.IntervalStartAt, data.IntervalStart);
        s.Monitors = data.SlideshowMonitors.Where(kv => Id(kv.Key) is not null).ToDictionary(kv => Id(kv.Key)!,
            kv => kv.Value.Select(slot => slot is null ? null : new SlideshowSlot { Image = CopyOf(slot.Image), Color = slot.Color, Fit = slot.Fit })
                          .Select(slot => slot is { IsEmpty: false } ? slot : null).ToList());
        (s.UseUnderlay, s.Effect) = (data.UseUnderlay, data.Effect);
        s.CycleLengths = data.CycleLengths.Where(kv => Id(kv.Key) is not null).ToDictionary(kv => Id(kv.Key)!, kv => kv.Value);
        s.Cleared.Clear();
        _settingsService.Save(_settings);

        if (apply && data.SlideshowEnabled) SlideshowEnabled = true;
        else if (apply && data.StaticEnabled) StaticWallpaperEnabled = true;
        RaiseWallpaperChanged();
        return $"Картинок скопировано: {images.Count}, настройки загружены{(apply ? " и применены" : "")}.{skippedText}";
    }
}

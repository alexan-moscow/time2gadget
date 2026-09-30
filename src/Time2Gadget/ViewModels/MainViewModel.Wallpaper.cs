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

    // ---- Своя картинка фона и сплошные цвета мониторов ----

    internal string? WallpaperImage => _settings.WallpaperImage;

    internal WallpaperFit WallpaperFitFor(string monitorId) =>
        _settings.WallpaperModes.TryGetValue(monitorId, out var fit) ? fit : WallpaperFit.Stretch;

    /// <summary>Сплошной цвет монитора («#RRGGBB») или null — у монитора прежний фон.</summary>
    internal string? WallpaperColorFor(string monitorId) =>
        _settings.WallpaperColors.TryGetValue(monitorId, out var color) ? color : null;

    /// <summary>Стоит своя настройка фона: картинка или хотя бы один сплошной цвет.</summary>
    internal bool HasOwnWallpaper => _settings.WallpaperImage is not null || _settings.WallpaperColors.Count > 0;


    /// <summary>Изменились картинка, режимы или цвета — окно «Заставка и фон экрана» перечитывает их (после «✕», сброса и т.п.).</summary>
    public event EventHandler? WallpaperChanged;

    private string? _appliedWallpaperSignature; // что поставлено сейчас: картинка + мониторы + режимы + цвета

    /// <summary>Выбрать картинку и поставить её на мониторы (у каждого — свой режим).</summary>
    internal bool SetWallpaperImage(string? image)
    {
        _settings.WallpaperImage = image;
        _settingsService.Save(_settings);
        return ApplyWallpaper(force: true);
    }

    /// <summary>Режим монитора (кнопка «Мон N») — сразу на экран.</summary>
    internal bool SetWallpaperFit(string monitorId, WallpaperFit fit)
    {
        if (fit == WallpaperFit.Stretch) _settings.WallpaperModes.Remove(monitorId);
        else _settings.WallpaperModes[monitorId] = fit;
        _settingsService.Save(_settings);
        return ApplyWallpaper(force: true);
    }

    /// <summary>Сплошной цвет монитора без картинки (квадратик у кнопки монитора) — сразу на экран.</summary>
    internal bool SetWallpaperColor(string monitorId, string hex)
    {
        _settings.WallpaperColors[monitorId] = hex;
        _settingsService.Save(_settings);
        return ApplyWallpaper(force: true);
    }

    /// <summary>
    /// Поставить свою настройку: на каждый монитор — картинку в его режиме, а без картинки («не отображать» или картинки нет) —
    /// его сплошной цвет, иначе его прежний фон. Первый раз — запомнить, какой фон был (для «✕»). Без <paramref name="force"/> —
    /// только если с прошлого раза поменялись мониторы (подключили монитор, сменили разрешение).
    /// </summary>
    internal bool ApplyWallpaper(bool force)
    {
        if (!HasOwnWallpaper) return false;
        var monitors = WallpaperService.Monitors();
        var signature = WallpaperSignature(monitors);
        if (!force && signature == _appliedWallpaperSignature) return true;
        if (_settings.WallpaperBefore is null && WallpaperService.Capture() is { } captured)
        {
            _settings.WallpaperBefore = captured;
            _settingsService.Save(_settings);
        }
        var before = _settings.WallpaperBefore;
        var plans = monitors.Select(m =>
        {
            var color = WallpaperColorFor(m.Id) is { } hex ? WallpaperService.ParseColor(hex) : (System.Drawing.Color?)null;
            var fit = WallpaperFitFor(m.Id);
            if (_settings.WallpaperImage is { } image && fit != WallpaperFit.None)
                return new MonitorWallpaperPlan(m, image, fit, color);
            if (color is null && before is not null && before.Images.TryGetValue(m.Id, out var original))
                return new MonitorWallpaperPlan(m, original, WallpaperService.FitFromPosition(before.Position), null); // прежний фон монитора
            return new MonitorWallpaperPlan(m, null, WallpaperFit.None, color);
        }).ToList();
        bool ok = WallpaperService.Apply(plans);
        if (ok) _appliedWallpaperSignature = signature;
        return ok;
    }

    /// <summary>«✕» (правый щелчок) и сброс настроек: своя картинка, режимы и цвета сняты, фон — как был до них.</summary>
    internal void ClearWallpaper()
    {
        var before = _settings.WallpaperBefore;
        _settings.WallpaperImage = null;
        _settings.WallpaperModes.Clear();
        _settings.WallpaperColors.Clear();
        _settings.WallpaperBefore = null;
        _settingsService.Save(_settings);
        _appliedWallpaperSignature = null;
        if (before is not null) WallpaperService.RestoreState(before);
        WallpaperChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>«✕» (левый щелчок): картинка и режимы сняты, на всех мониторах — сплошной чёрный (цвет каждого можно сменить).</summary>
    internal void BlackoutWallpaper()
    {
        _settings.WallpaperImage = null;
        _settings.WallpaperModes.Clear();
        _settings.WallpaperColors = WallpaperService.Monitors().ToDictionary(m => m.Id, _ => "#000000");
        _settingsService.Save(_settings);
        ApplyWallpaper(force: true);
        WallpaperChanged?.Invoke(this, EventArgs.Empty);
    }
    private string WallpaperSignature(IEnumerable<WallpaperMonitor> monitors) =>
        _settings.WallpaperImage + "|" + string.Join(";", monitors.Select(m => $"{m.Id}={m.Bounds}:{WallpaperFitFor(m.Id)}:{WallpaperColorFor(m.Id)}"));

    /// <summary>При запуске: своя настройка уже стоит (Windows её помнит) — запомнить мониторы, под которые она готовилась.</summary>
    public void RememberAppliedWallpaper()
    {
        if (HasOwnWallpaper) _appliedWallpaperSignature = WallpaperSignature(WallpaperService.Monitors());
    }

    /// <summary>Мониторы поменялись (MainWindow, DisplaySettingsChanged) — своя настройка под новые размеры.</summary>
    public void ReapplyWallpaperIfNeeded() => ApplyWallpaper(force: false);
}

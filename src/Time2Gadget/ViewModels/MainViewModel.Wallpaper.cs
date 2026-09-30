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
                    if (_settings.WallpaperImage is not null)
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

    // ---- Своя картинка фона ----

    internal string? WallpaperImage => _settings.WallpaperImage;

    internal WallpaperFit WallpaperFitFor(string monitorId) =>
        _settings.WallpaperModes.TryGetValue(monitorId, out var fit) ? fit : WallpaperFit.Stretch;

    /// <summary>Изменились картинка или режимы — окно «Заставка и фон экрана» перечитывает их (после «✕», сброса и т.п.).</summary>
    public event EventHandler? WallpaperChanged;

    private string? _appliedWallpaperSignature; // что поставлено сейчас: картинка + мониторы + режимы

    /// <summary>Выбрать картинку (null — ничего не ставить) и поставить её на все мониторы.</summary>
    internal bool SetWallpaperImage(string? image)
    {
        _settings.WallpaperImage = image;
        _settingsService.Save(_settings);
        return ApplyWallpaper(force: true);
    }

    /// <summary>Режим монитора (кнопка «Мон N») — сразу на экран, если картинка выбрана.</summary>
    internal bool SetWallpaperFit(string monitorId, WallpaperFit fit)
    {
        if (fit == WallpaperFit.Stretch) _settings.WallpaperModes.Remove(monitorId);
        else _settings.WallpaperModes[monitorId] = fit;
        _settingsService.Save(_settings);
        return ApplyWallpaper(force: true);
    }

    /// <summary>
    /// Поставить свою картинку. Первый раз — запомнить, какой фон был (для «✕»). Без <paramref name="force"/> — только если
    /// с прошлого раза поменялись мониторы (подключили монитор, сменили разрешение), чтобы не перерисовывать фон зря.
    /// </summary>
    internal bool ApplyWallpaper(bool force)
    {
        if (_settings.WallpaperImage is not { } image) return false;
        var monitors = WallpaperService.Monitors();
        var signature = WallpaperSignature(image, monitors);
        if (!force && signature == _appliedWallpaperSignature) return true;
        if (_settings.WallpaperBefore is null && WallpaperService.Capture() is { } before)
        {
            _settings.WallpaperBefore = before;
            _settingsService.Save(_settings);
        }
        bool ok = WallpaperService.Apply(image, monitors, WallpaperFitFor);
        if (ok) _appliedWallpaperSignature = signature;
        return ok;
    }

    /// <summary>«✕»: своя картинка и режимы сняты, фон — как был до неё.</summary>
    internal void ClearWallpaper()
    {
        var before = _settings.WallpaperBefore;
        _settings.WallpaperImage = null;
        _settings.WallpaperModes.Clear();
        _settings.WallpaperBefore = null;
        _settingsService.Save(_settings);
        _appliedWallpaperSignature = null;
        if (before is not null) WallpaperService.RestoreState(before);
        WallpaperChanged?.Invoke(this, EventArgs.Empty);
    }

    private string WallpaperSignature(string image, IEnumerable<WallpaperMonitor> monitors) =>
        image + "|" + string.Join(";", monitors.Select(m => $"{m.Id}={m.Bounds}:{WallpaperFitFor(m.Id)}"));

    /// <summary>При запуске: своя картинка уже стоит (Windows её помнит) — запомнить мониторы, под которые она готовилась.</summary>
    public void RememberAppliedWallpaper()
    {
        if (_settings.WallpaperImage is { } image) _appliedWallpaperSignature = WallpaperSignature(image, WallpaperService.Monitors());
    }

    /// <summary>Мониторы поменялись (MainWindow, DisplaySettingsChanged) — своя картинка под новые размеры.</summary>
    public void ReapplyWallpaperIfNeeded() => ApplyWallpaper(force: false);
}

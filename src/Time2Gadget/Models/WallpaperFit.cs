namespace Time2Gadget.Models;

/// <summary>
/// Как картинка фона ложится на монитор (окно «Заставка и фон экрана», докладка 2026-09-30). Порядок — порядок переключения
/// кнопкой монитора: растянуть → по размеру → заполнить → по центру → не отображать → снова растянуть.
/// </summary>
public enum WallpaperFit
{
    /// <summary>Растянуть на весь монитор, пропорции не сохраняются.</summary>
    Stretch,
    /// <summary>По размеру: картинка целиком, пропорции сохраняются, по краям — поля цвета фона.</summary>
    Fit,
    /// <summary>Заполнить: весь монитор, пропорции сохраняются, лишнее обрезается.</summary>
    Fill,
    /// <summary>По центру в исходном размере (больше монитора — обрезается).</summary>
    Center,
    /// <summary>Не отображать: картинка на этот монитор не ставится (сплошной цвет монитора или прежний фон).</summary>
    None,
}


/// <summary>Фон до того, как Тайм2гаджет поставил свою картинку, — чтобы «✕» вернул его как было.</summary>
public sealed class WallpaperBackupState
{
    /// <summary>Монитор (путь устройства из IDesktopWallpaper) → файл картинки.</summary>
    public Dictionary<string, string> Images { get; set; } = new();

    /// <summary>DESKTOP_WALLPAPER_POSITION.</summary>
    public int Position { get; set; }

    /// <summary>Шло слайд-шоу — откуда (вернуть его вместо картинок).</summary>
    public WallpaperSlideshowBackup? Slideshow { get; set; }
}

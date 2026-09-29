namespace Time2Gadget.Models;

/// <summary>Откуда шло слайд-шоу Windows до «Закрепить фоны» (Services/WallpaperService) — чтобы вернуть его как было.</summary>
public sealed class WallpaperSlideshowBackup
{
    /// <summary>Папки/файлы слайд-шоу.</summary>
    public List<string> Paths { get; set; } = new();

    /// <summary>DESKTOP_SLIDESHOW_OPTIONS (1 — в случайном порядке).</summary>
    public int Options { get; set; }

    /// <summary>Интервал смены, мс.</summary>
    public uint Tick { get; set; }
}

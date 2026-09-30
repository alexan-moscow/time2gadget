using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>Настройки фона в архиве экспорта: картинки — по имени файла, мониторы — по номеру (на другом компьютере другие устройства).</summary>
public sealed class WallpaperPackageData
{
    public int Version { get; set; } = 1;
    public bool StaticEnabled { get; set; }
    public string? StaticImage { get; set; }
    public Dictionary<int, WallpaperFit> StaticModes { get; set; } = new();
    public Dictionary<int, string> StaticColors { get; set; } = new();
    public bool SlideshowEnabled { get; set; }
    public SlideshowKind Kind { get; set; }
    public bool HourlyFromTime { get; set; }
    public TimeSpan HourlyStart { get; set; }
    public bool DailyAtTime { get; set; }
    public TimeSpan DailyTime { get; set; }
    public TimeSpan Interval { get; set; }
    public bool IntervalStartAt { get; set; }
    public TimeSpan IntervalStart { get; set; }
    public Dictionary<int, List<SlideshowSlot?>> SlideshowMonitors { get; set; } = new();
}

/// <summary>
/// Экспорт/импорт «Заставки и фона экрана» (докладка 2026-09-30): один zip — все картинки из папки программы
/// (images/…) и wallpaper.json с настройками статичной заставки и слайдшоу.
/// </summary>
public static class WallpaperPackage
{
    private const string SettingsEntry = "wallpaper.json";
    private const string ImagesPrefix = "images/";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Export(string zipPath, WallpaperPackageData data, IEnumerable<string> images)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var image in images.Where(File.Exists))
            zip.CreateEntryFromFile(image, ImagesPrefix + Path.GetFileName(image), CompressionLevel.NoCompression);
        var entry = zip.CreateEntry(SettingsEntry);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, data, Json);
    }

    /// <summary>
    /// Прочитать архив: картинки копируются в папку программы (лимит — <see cref="WallpaperService.LibraryLimit"/>).
    /// Возвращает настройки (или null — в архиве их нет), соответствие «имя в архиве → путь копии» и сколько не влезло.
    /// </summary>
    public static (WallpaperPackageData? Data, Dictionary<string, string> Images, int Skipped) Import(string zipPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var temp = Path.Combine(Path.GetTempPath(), "Time2Gadget-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            WallpaperPackageData? data = null;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName == SettingsEntry)
                    {
                        using var stream = entry.Open();
                        data = JsonSerializer.Deserialize<WallpaperPackageData>(stream, Json);
                    }
                    else if (entry.FullName.StartsWith(ImagesPrefix) && entry.Name.Length > 0)
                    {
                        var name = Path.GetFileName(entry.Name); // без путей из архива
                        entry.ExtractToFile(Path.Combine(temp, name), overwrite: true);
                    }
                }
            }
            int skipped = 0;
            foreach (var file in Directory.GetFiles(temp)) // по одной — чтобы точно знать, куда легла каждая копия
            {
                var (imported, notFit) = WallpaperService.ImportImages(new[] { file });
                if (imported.Count > 0) map[Path.GetFileName(file)] = imported[0];
                skipped += notFit;
            }
            return (data, map, skipped);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}

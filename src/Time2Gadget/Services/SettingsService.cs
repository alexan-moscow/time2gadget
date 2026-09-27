using System.IO;
using System.Text.Json;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <inheritdoc cref="ISettingsService"/>
/// <remarks>
/// Простой JSON-файл в %APPDATA%\Time2Gadget\settings.json, без БД (docs/DECISIONS.md, 2026-09-26).
/// Любая ошибка чтения/парсинга — тихий откат на значения по умолчанию, приложение не падает.
/// </remarks>
public sealed class SettingsService : ISettingsService
{
    private static readonly string Directory_ =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget");

    private static readonly string FilePath = Path.Combine(Directory_, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory_);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Настройки — не критичный путь; молча игнорируем сбой записи (например, нет прав),
            // приложение продолжает работать с текущими значениями в памяти.
        }
    }
}

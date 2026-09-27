namespace Time2Gadget.Models;

/// <summary>Звонок из списка: Id — имя файла в Assets/Ringtones (без .mp3), Title — что видит пользователь.</summary>
public sealed record Ringtone(string Id, string Title);

/// <summary>
/// Встроенные звонки (докладка 2026-09-27): реальные mp3-записи, которые пользователь подобрал сам
/// (Pixabay), вместо прежних процедурно синтезированных. Файлы встроены в exe как ресурсы
/// (Assets/Ringtones/*.mp3, см. .csproj) и при первом использовании распаковываются в кэш.
/// Встроенных тегов с названиями у файлов нет — короткие русские названия взяты из имён файлов
/// (автор и числовой id Pixabay убраны; номер оставлен только там, где он единственное отличие).
/// </summary>
public static class RingtoneCatalog
{
    /// <summary>Особый Id — «свой файл» пользователя (путь в AppSettings.CustomSoundFilePath).</summary>
    public const string CustomId = "custom";

    /// <summary>Звонок по умолчанию — «Таймер завершён» (решение пользователя 2026-09-27).</summary>
    public const string DefaultId = "timer-finished";

    public static readonly IReadOnlyList<Ringtone> BuiltIn = new Ringtone[]
    {
        new("alarm-clock",    "Будильник"),
        new("loud-beeps",     "Громкий писк"),
        new("long-ringtone",  "Длинный рингтон"),
        new("oven-ding",      "Звонок духовки"),
        new("star-dust",      "Звёздная пыль"),
        new("short-ringtone", "Короткий рингтон"),
        new("kitchen-timer",  "Кухонный таймер"),
        new("melody",         "Мелодия"),
        new("simple-alarm",   "Простой будильник"),
        new("ringtone-19",    "Рингтон 19"),
        new("ringtone-25",    "Рингтон 25"),
        new("ringtone-42",    "Рингтон 42"),
        new("alert-loop",     "Сигнал тревоги"),
        new("synapse",        "Синапс"),
        new("siren",          "Сирена"),
        new("counter",        "Счётчик"),
        new("timer-finished", "Таймер завершён"),
        new("tea-alarm",      "Чайный будильник"),
    };

    public static bool IsBuiltIn(string? id) => BuiltIn.Any(r => r.Id == id);
}

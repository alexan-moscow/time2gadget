using Time2Gadget.Models;

namespace Time2Gadget.Services;

public interface ISoundService
{
    /// <summary>
    /// Звонок завершения отыграл все повторы сам (не остановлен вручную). Может прийти из фонового
    /// потока — подписчик сам переходит в UI-поток.
    /// </summary>
    event EventHandler? AlarmCompleted;

    /// <summary>
    /// Прослушивание с этим номером (из <see cref="PlayPreview"/>) закончилось — доиграло или остановлено.
    /// Может прийти из фонового потока.
    /// </summary>
    event EventHandler<int>? PreviewEnded;

    /// <summary>Список доступных устройств вывода звука, первый элемент — "системное по умолчанию".</summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>
    /// Запускает звонок завершения таймера по текущим настройкам (устройство/громкость/звонок).
    /// Каждое повторение — полное проигрывание звука до конца, ровно AlarmRepeatCount раз
    /// (не бесконечно), с короткой фиксированной паузой между повторами.
    /// </summary>
    /// <param name="choice">Свой звук быстрого таймера (незаданные поля — из настроек); null — общий звук.</param>
    void PlayAlarm(AppSettings settings, SoundChoice? choice = null);

    /// <summary>
    /// Прослушивание в настройках — звук один раз. choice null — выбранный в разделе «Звук».
    /// Возвращает номер прослушивания для <see cref="PreviewEnded"/> (0 — звука нет).
    /// </summary>
    int PlayPreview(AppSettings settings, SoundChoice? choice = null);

    /// <summary>Останавливает любое текущее воспроизведение (звонок или прослушивание) с быстрым затуханием.</summary>
    void StopAlarm();

    /// <summary>Длительность звука (встроенный — по Id, «свой файл» — по пути); null — не удалось прочитать.</summary>
    TimeSpan? GetDuration(string ringtoneId, string? customPath);

    /// <summary>
    /// Копирует выбранный пользователем звуковой файл в папку Sounds рядом с программой и возвращает
    /// путь к копии — звонок не ломается, если оригинал удалят/переместят. Одноимённый файл с другим
    /// содержимым получает новое имя. null — если скопировать не удалось.
    /// </summary>
    string? ImportCustomSound(string sourcePath);

    /// <summary>Удалить прежнюю копию — только из своей папки звуков (вызывающий проверяет, что она больше не нужна).</summary>
    void DeleteImportedSound(string? path);
}

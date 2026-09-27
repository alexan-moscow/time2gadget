using Time2Gadget.Models;

namespace Time2Gadget.Services;

public interface ISoundService
{
    /// <summary>
    /// Звонок завершения отыграл все повторы сам (не остановлен вручную). Может прийти из фонового
    /// потока — подписчик сам переходит в UI-поток.
    /// </summary>
    event EventHandler? AlarmCompleted;

    /// <summary>Список доступных устройств вывода звука, первый элемент — "системное по умолчанию".</summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>
    /// Запускает звонок завершения таймера по текущим настройкам (устройство/громкость/звонок).
    /// Каждое повторение — полное проигрывание звука до конца, ровно AlarmRepeatCount раз
    /// (не бесконечно), с короткой фиксированной паузой между повторами.
    /// </summary>
    void PlayAlarm(AppSettings settings);

    /// <summary>
    /// Прослушивание в настройках — играет звук один раз. ringtoneId — конкретный звонок из списка
    /// (кнопка ▶ прямо в выпадающем списке, без выбора); null — текущий выбранный.
    /// </summary>
    void PlayPreview(AppSettings settings, string? ringtoneId = null);

    /// <summary>Немедленно останавливает любое текущее воспроизведение звонка.</summary>
    void StopAlarm();

    /// <summary>
    /// Копирует выбранный пользователем звуковой файл в папку Sounds рядом с программой и возвращает
    /// путь к копии — звонок не ломается, если оригинал удалят/переместят. Предыдущая импортированная
    /// копия удаляется. null — если скопировать не удалось.
    /// </summary>
    string? ImportCustomSound(string sourcePath, string? previousImportedPath);
}

using Time2Gadget.Models;

namespace Time2Gadget.Services;

public interface ISoundService
{
    /// <summary>Список доступных устройств вывода звука, первый элемент — "системное по умолчанию".</summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>
    /// Запускает звонок завершения таймера по текущим настройкам (устройство/громкость/звонок).
    /// Каждое повторение — полное проигрывание звука до конца, ровно AlarmRepeatCount раз
    /// (не бесконечно), с короткой фиксированной паузой между повторами.
    /// </summary>
    void PlayAlarm(AppSettings settings);

    /// <summary>Короткое прослушивание в настройках — играет выбранный звук один раз.</summary>
    void PlayPreview(AppSettings settings);

    /// <summary>Немедленно останавливает любое текущее воспроизведение звонка.</summary>
    void StopAlarm();

    /// <summary>
    /// Копирует выбранный пользователем звуковой файл в папку Sounds рядом с программой и возвращает
    /// путь к копии — звонок не ломается, если оригинал удалят/переместят. Предыдущая импортированная
    /// копия удаляется. null — если скопировать не удалось.
    /// </summary>
    string? ImportCustomSound(string sourcePath, string? previousImportedPath);
}

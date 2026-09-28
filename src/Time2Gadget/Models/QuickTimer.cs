namespace Time2Gadget.Models;

/// <summary>
/// Быстрый таймер (докладка 2026-09-28): время ЧЧ:ММ:СС + глобальная клавиша/кнопка мыши; нажатие — запустить
/// таймер на это время заново. До 5 штук; по умолчанию одна пустая строка (нули, клавиша не задана).
/// Звук — свой у каждого: по умолчанию «Таймер завершён», устройство и громкость — как в разделе «Звук».
/// </summary>
public sealed class QuickTimer
{
    /// <summary>Звонок быстрого таймера по умолчанию (решение пользователя 2026-09-28).</summary>
    public const string DefaultRingtoneId = "timer-finished";

    public int Hours { get; set; }
    public int Minutes { get; set; }
    public int Seconds { get; set; }
    public HotkeyBinding Binding { get; set; } = HotkeyBinding.Empty;

    /// <summary>Id из RingtoneCatalog (или CustomId — файл <see cref="CustomSoundFilePath"/>); null — общий звонок из раздела «Звук».</summary>
    public string? RingtoneId { get; set; } = DefaultRingtoneId;
    /// <summary>Свой файл таймера — копия в папке звуков программы (как у раздела «Звук»).</summary>
    public string? CustomSoundFilePath { get; set; }
    /// <summary>Своё устройство вывода (AudioDeviceInfo.Id, "" — системное); null — как в разделе «Звук».</summary>
    public string? AudioDeviceId { get; set; }
    /// <summary>Своя громкость 0..1; null — общая громкость (галочка «общая» в меню звука).</summary>
    public double? Volume { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public TimeSpan Duration => new(Hours, Minutes, Seconds);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsUsable => Duration > TimeSpan.Zero && !Binding.IsEmpty;

    [System.Text.Json.Serialization.JsonIgnore]
    public SoundChoice Sound => new(RingtoneId, CustomSoundFilePath, AudioDeviceId, Volume);
}

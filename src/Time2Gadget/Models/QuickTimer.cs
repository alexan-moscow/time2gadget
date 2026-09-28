namespace Time2Gadget.Models;

/// <summary>
/// Быстрый таймер (докладка 2026-09-28): время ЧЧ:ММ:СС + глобальная клавиша/кнопка мыши; нажатие — запустить
/// таймер на это время заново. До 5 штук; по умолчанию одна пустая строка (нули, клавиша не задана).
/// </summary>
public sealed class QuickTimer
{
    public int Hours { get; set; }
    public int Minutes { get; set; }
    public int Seconds { get; set; }
    public HotkeyBinding Binding { get; set; } = HotkeyBinding.Empty;
    /// <summary>Свой звонок (Id из RingtoneCatalog.BuiltIn); null — общий звонок из раздела «Звук».</summary>
    public string? RingtoneId { get; set; }
    /// <summary>Своё устройство вывода (AudioDeviceInfo.Id, "" — системное); null — как в разделе «Звук».</summary>
    public string? AudioDeviceId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public TimeSpan Duration => new(Hours, Minutes, Seconds);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsUsable => Duration > TimeSpan.Zero && !Binding.IsEmpty;
}

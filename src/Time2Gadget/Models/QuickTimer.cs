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

    public TimeSpan Duration => new(Hours, Minutes, Seconds);
    public bool IsUsable => Duration > TimeSpan.Zero && !Binding.IsEmpty;
}

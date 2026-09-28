using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Time2Gadget.Models;

/// <summary>Кнопка мыши, которую можно назначить (левая/правая — нельзя: ими кликают).</summary>
public enum HotkeyMouseButton { None, Middle, XButton1, XButton2 }

/// <summary>
/// Сочетание клавиш или кнопки мыши с модификаторами (докладка 2026-09-28): клавиша <see cref="Key"/> ИЛИ
/// кнопка мыши <see cref="MouseButton"/> плюс Ctrl/Shift/Alt/Win. Пустое (<see cref="IsEmpty"/>) — не назначено.
/// </summary>
public sealed record HotkeyBinding
{
    public Key Key { get; init; } = Key.None;
    public ModifierKeys Modifiers { get; init; } = ModifierKeys.None;
    public HotkeyMouseButton MouseButton { get; init; } = HotkeyMouseButton.None;

    public static HotkeyBinding Empty { get; } = new();

    [JsonIgnore] public bool IsEmpty => Key == Key.None && MouseButton == HotkeyMouseButton.None;
    [JsonIgnore] public bool IsMouse => MouseButton != HotkeyMouseButton.None;

    public static HotkeyBinding FromKey(Key key, ModifierKeys modifiers = ModifierKeys.None) => new() { Key = key, Modifiers = modifiers };

    /// <summary>«Ctrl+Shift+F5», «Alt+Мышь «Назад»», «Не задано».</summary>
    public override string ToString()
    {
        if (IsEmpty) return "Не задано";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(IsMouse ? MouseName(MouseButton) : KeyName(Key));
        return string.Join("+", parts);
    }

    private static string MouseName(HotkeyMouseButton b) => b switch
    {
        HotkeyMouseButton.Middle => "Колесо мыши",
        HotkeyMouseButton.XButton1 => "Мышь «Назад»",
        HotkeyMouseButton.XButton2 => "Мышь «Вперёд»",
        _ => ""
    };

    private static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(k - Key.NumPad0),
        Key.Space => "Пробел",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Multiply => "Num *",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Divide => "Num /",
        _ => k.ToString()
    };
}

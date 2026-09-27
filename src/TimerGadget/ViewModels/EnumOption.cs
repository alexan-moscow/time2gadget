namespace TimerGadget.ViewModels;

/// <summary>Пара "значение enum + человеко-читаемая подпись" для ComboBox (docs/UI-CONTRACT.md → Settings).</summary>
public sealed record EnumOption<T>(T Value, string Label);

namespace Time2Gadget.ViewModels;

/// <summary>Пара "значение enum + человеко-читаемая подпись" для ComboBox (docs/UI-CONTRACT.md → Settings).</summary>
public sealed record EnumOption<T>(T Value, string Label);

/// <summary>Быстрый таймер в списке «после окончания — запустить».</summary>
public sealed record AfterTarget(string Id, string Name);

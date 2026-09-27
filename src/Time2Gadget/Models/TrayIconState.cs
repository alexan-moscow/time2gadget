namespace Time2Gadget.Models;

/// <summary>
/// Что показать иконкой в трее (docs/UI-CONTRACT.md → Tray): кольцо прогресса как у главного окна.
/// ActiveEffect — эффект завершения, который сейчас играет и в окне (None — не играет: выключен или
/// истекла его длительность). EffectSeconds — секунд с момента окончания, фаза анимации.
/// BlinkWhileHidden — эффекта нет, но окно скрыто: простое мигание красным, чтобы окончание не пропустили.
/// </summary>
public sealed record TrayIconState(
    TimerStatus Status,
    double ProgressFraction,
    FinishVisualEffect ActiveEffect,
    double EffectSeconds,
    bool BlinkWhileHidden,
    string Tooltip);

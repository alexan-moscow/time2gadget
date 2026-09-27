namespace Time2Gadget.Models;

/// <summary>
/// Что показать иконкой в трее (docs/UI-CONTRACT.md → Tray): кольцо прогресса как у главного окна.
/// IsBlinking — таймер закончился, а окно скрыто: кольцо мигает красным; BlinkOn — текущая яркая фаза.
/// </summary>
public sealed record TrayIconState(TimerStatus Status, double ProgressFraction, bool IsBlinking, bool BlinkOn, string Tooltip);

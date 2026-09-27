namespace TimerGadget.Models;

/// <summary>
/// Состояния таймера. См. docs/UI-CONTRACT.md → CenterDisplay для точного маппинга на UI.
/// </summary>
public enum TimerStatus
{
    Ready,
    Running,
    Paused,
    Finished
}

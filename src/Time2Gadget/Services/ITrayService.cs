namespace Time2Gadget.Services;

/// <summary>
/// Иконка в системном трее + контекстное меню (docs/UI-CONTRACT.md → Tray).
/// Меню — только «Настройки» и «Выход» (2026-09-27): остальное дублировало окно настроек.
/// Клик по иконке — показать окно.
/// </summary>
public interface ITrayService : IDisposable
{
    event EventHandler? ShowRequested;
    event EventHandler? SettingsRequested;
    event EventHandler? ExitRequested;

    void Initialize();
    void ShowBalloon(string title, string text);
}

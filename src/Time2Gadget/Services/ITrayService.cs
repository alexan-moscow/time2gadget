namespace Time2Gadget.Services;

/// <summary>
/// Иконка в системном трее + контекстное меню (docs/UI-CONTRACT.md → Tray).
/// </summary>
public interface ITrayService : IDisposable
{
    event EventHandler? ShowRequested;
    event EventHandler? StartPauseRequested;
    event EventHandler? ResetRequested;
    event EventHandler<bool>? AlwaysOnTopToggled;
    event EventHandler<bool>? LaunchAtStartupToggled;
    event EventHandler? SettingsRequested;
    event EventHandler? ExitRequested;

    void Initialize();
    void SetAlwaysOnTopChecked(bool value);
    void SetLaunchAtStartupChecked(bool value);
    void ShowBalloon(string title, string text);
}

using System.Windows;
using TimerGadget.Services;
using TimerGadget.ViewModels;
using TimerGadget.Views;

namespace TimerGadget;

/// <summary>
/// Точка входа. Без DI-контейнера (docs/DECISIONS.md, 2026-09-26) — ручная композиция сервисов.
/// ShutdownMode = OnExplicitShutdown: закрытие окна сворачивает в трей, приложение завершается
/// только через явный вызов Shutdown() (пункт трея "Выход", docs/UI-CONTRACT.md → Tray).
/// </summary>
public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ITimerEngine engine = new TimerEngine();
        ISettingsService settingsService = new SettingsService();
        ISoundService soundService = new SoundService();
        ITrayService trayService = new TrayService();

        _viewModel = new MainViewModel(engine, settingsService, soundService, trayService);
        _viewModel.IsLaunchAtStartup = AutostartService.IsEnabled();

        var window = new MainWindow(_viewModel);

        _viewModel.ExitRequested += (_, _) =>
        {
            _viewModel.Dispose();
            Shutdown();
        };
        _viewModel.ShowRequested += (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        };

        _viewModel.InitializeTray();

        MainWindow = window;
        window.Show();
    }
}

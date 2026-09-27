using System.Windows;
using Time2Gadget.Services;
using Time2Gadget.ViewModels;
using Time2Gadget.Views;

namespace Time2Gadget;

/// <summary>
/// Точка входа. Без DI-контейнера (docs/DECISIONS.md, 2026-09-26) — ручная композиция сервисов.
/// ShutdownMode = OnExplicitShutdown: закрытие окна сворачивает в трей, приложение завершается
/// только через явный вызов Shutdown() (пункт трея "Выход", docs/UI-CONTRACT.md → Tray).
/// </summary>
public partial class App : Application
{
    // Одна копия на пользовательскую сессию (Local\), docs/DECISIONS.md, 2026-09-27. Повторный запуск
    // не открывает второе окно, а сигналит событием уже запущенной копии — она показывает своё окно.
    private const string InstanceMutexName = @"Local\Time2Gadget.SingleInstance";
    private const string ShowEventName = @"Local\Time2Gadget.ShowWindow";

    private MainViewModel? _viewModel;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Первая копия ещё не успела создать событие (запуск в ту же долю секунды) — просто выходим.
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ITimerEngine engine = new TimerEngine();
        ISettingsService settingsService = new SettingsService();
        ISoundService soundService = new SoundService();
        ITrayService trayService = new TrayService();
        IUpdateService updateService = new UpdateService();

        _viewModel = new MainViewModel(engine, settingsService, soundService, trayService, updateService);
        _viewModel.IsLaunchAtStartup = AutostartService.IsEnabled();

        var window = new MainWindow(_viewModel);

        _viewModel.ExitRequested += (_, _) =>
        {
            _viewModel.Dispose();
            Shutdown();
        };
        _viewModel.ShowRequested += (_, _) => ShowMainWindow(window);

        _viewModel.InitializeTray();

        MainWindow = window;
        window.Show();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => ShowMainWindow(window)),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

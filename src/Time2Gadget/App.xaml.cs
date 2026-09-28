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

    // Та же просьба «покажи окно», но системным сообщением: копия с обычными правами не может открыть объекты
    // копии, запущенной с правами администратора (другой уровень целостности), а разрешённое сообщение доходит
    // (ChangeWindowMessageFilterEx). Нужно с режимом «Запускать с правами администратора» (2026-09-28).
    private static readonly int ShowMessage = RegisterWindowMessage("Time2Gadget.ShowWindow");

    private MainViewModel? _viewModel;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private System.Windows.Interop.HwndSource? _showListener;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isFirstInstance;
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out isFirstInstance);
        }
        catch (UnauthorizedAccessException)
        {
            isFirstInstance = false; // мьютекс есть, но создан копией с правами администратора — она и запущена
        }

        if (!isFirstInstance)
        {
            SignalRunningInstance();
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // Режим «с правами администратора»: запуск с обычными правами передаёт управление задаче Планировщика
        // (повышение без UAC) и выходит. LaunchedByTask — защита от зацикливания, если задача не дала повышения.
        var startupSettings = new SettingsService().Load();
        if (startupSettings.RunElevated && !ElevationService.IsElevated && !ElevationService.LaunchedByTask
            && ElevationService.TaskTargetsThisCopy(startupSettings.ElevationTaskExePath) // не запускать чужую копию
            && ElevationService.TaskExists())
        {
            _instanceMutex!.ReleaseMutex(); // до запуска задачи — иначе новая копия решит, что уже запущена
            _instanceMutex.Dispose();
            _instanceMutex = null;
            if (ElevationService.RunTask())
            {
                Shutdown();
                return;
            }
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _); // задача не запустилась — работаем как есть
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ITimerEngine engine = new TimerEngine();
        ISettingsService settingsService = new SettingsService();
        ISoundService soundService = new SoundService();
        ITrayService trayService = new TrayService();
        IUpdateService updateService = new UpdateService();

        _viewModel = new MainViewModel(engine, settingsService, soundService, trayService, updateService);
        if (settingsService.IsFirstRun)
        {
            // Первый запуск: автозапуск по умолчанию ВКЛ (решение пользователя 2026-09-27) — сразу прописываем
            // в реестр и сохраняем настройки, чтобы следующий запуск уже не считался первым (иначе отключённый
            // вручную, например в Диспетчере задач, автозапуск включался бы снова).
            if (_viewModel.IsLaunchAtStartup) AutostartService.SetEnabled(true);
            _viewModel.PersistSettings();
        }
        else
        {
            _viewModel.IsLaunchAtStartup = AutostartService.IsEnabled(); // реестр — источник истины
        }

        var window = new MainWindow(_viewModel);

        _viewModel.ExitRequested += (_, _) =>
        {
            _viewModel.Dispose();
            Shutdown();
        };
        _viewModel.ShowRequested += (_, _) => ShowMainWindow(window);
        _viewModel.RestartElevatedRequested += (_, _) => RestartElevated();
        _viewModel.RestartNormalRequested += (_, _) => RestartNormal();

        _viewModel.InitializeTray();

        MainWindow = window;
        window.Show();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => ShowMainWindow(window)),
            null, Timeout.Infinite, executeOnlyOnce: false);

        // Невидимое окно БЕЗ владельца — только такие получают широковещательные сообщения (главное окно
        // без кнопки на панели задач имеет скрытого владельца). Сообщение разрешено и от копий с меньшими правами.
        _showListener = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Time2Gadget.ShowListener")
        {
            Width = 0, Height = 0, WindowStyle = 0, ParentWindow = IntPtr.Zero
        });
        ChangeWindowMessageFilterEx(_showListener.Handle, ShowMessage, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
        _showListener.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg == ShowMessage) { ShowMainWindow(window); handled = true; }
            return IntPtr.Zero;
        });
    }

    /// <summary>Повторный запуск: попросить уже запущенную копию показать окно (событием и сообщением).</summary>
    private static void SignalRunningInstance()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowEventName);
            existing.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            // Нет события (копия ещё стартует) или оно у копии с правами администратора — остаётся сообщение.
        }
        PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Включили «Запускать с правами администратора» (задача уже создана): перезапуститься через задачу.
    /// Мьютекс освобождается ДО запуска — иначе новая копия решит, что программа уже запущена, и закроется.
    /// </summary>
    private void RestartElevated()
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        if (ElevationService.RunTask())
        {
            _viewModel?.Dispose();
            Shutdown();
            return;
        }
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _);
        MessageBox.Show("Не удалось перезапустить программу с правами администратора. Настройка сохранена — сработает при следующем запуске.",
            "Тайм2гаджет", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Выключили «Запускать с правами администратора», работая с правами: перезапуститься с обычными. Процесс с правами
    /// запускает дочерние тоже с правами, поэтому запуск — через проводник (он работает с обычными правами пользователя).
    /// </summary>
    private void RestartNormal()
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Environment.ProcessPath}\"")
            {
                UseShellExecute = false
            });
            _viewModel?.Dispose();
            Shutdown();
        }
        catch
        {
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _);
            MessageBox.Show("Не удалось перезапустить программу. Запуск с правами администратора выключен — перезапустите её вручную.",
                "Тайм2гаджет", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showListener?.Dispose();
        _showEvent?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { /* уже освобождён */ }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int msg, uint action, IntPtr changeFilterStruct);
}

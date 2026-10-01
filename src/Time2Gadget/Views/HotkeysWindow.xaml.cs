using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно «Настройка клавиш» (докладка 2026-10-01: раздел «Клавиши» вынесен из настроек). Привязка — к MainViewModel.</summary>
public partial class HotkeysWindow : Window
{
    private static HotkeysWindow? _current;

    public HotkeysWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        WindowMemory.Attach(this, viewModel, "Hotkeys"); // открывается там, где оставили
        DataContext = viewModel;
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    /// <summary>Одно окно на программу; уже открыто — вывести вперёд.</summary>
    public static void ShowSingle(MainViewModel main, Window? owner)
    {
        if (_current is null)
        {
            _current = new HotkeysWindow(main) { Owner = owner };
            if (owner is null && _current.WindowStartupLocation != WindowStartupLocation.Manual) _current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        else if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }
}

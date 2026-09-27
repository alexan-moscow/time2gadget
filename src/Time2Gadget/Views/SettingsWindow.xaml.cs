using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Окно настроек. Биндится НАПРЯМУЮ на MainViewModel (не отдельная ViewModel) — изменения
/// применяются мгновенно в рантайме без синхронизирующего механизма, см. docs/ARCHITECTURE.md.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

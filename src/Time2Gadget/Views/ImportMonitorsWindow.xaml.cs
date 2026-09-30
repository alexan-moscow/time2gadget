using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно сопоставления мониторов при импорте (докладка 2026-10-01). Результат — <see cref="Apply"/> и карта мониторов.</summary>
public partial class ImportMonitorsWindow : Window
{
    private readonly ImportMonitorsViewModel _viewModel;

    public ImportMonitorsWindow(ImportMonitorsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    /// <summary>true — «Применить сейчас», false — «Только скопировать» (при DialogResult = true).</summary>
    public bool Apply { get; private set; }

    public Dictionary<int, string> Map => _viewModel.Map();

    private void OnApply(object sender, RoutedEventArgs e) { Apply = true; DialogResult = true; }
    private void OnCopyOnly(object sender, RoutedEventArgs e) { Apply = false; DialogResult = true; }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

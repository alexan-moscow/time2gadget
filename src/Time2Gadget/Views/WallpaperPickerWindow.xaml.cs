using System.Windows;
using System.Windows.Controls;
using Time2Gadget.Services;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно выбора картинки монитора (докладка 2026-10-01) — логика во ViewModels/WallpaperPickerViewModel.</summary>
public partial class WallpaperPickerWindow : Window
{
    private readonly WallpaperPickerViewModel _viewModel;

    public WallpaperPickerWindow(MainViewModel main, WallpaperMonitor monitor)
    {
        InitializeComponent();
        DataContext = _viewModel = new WallpaperPickerViewModel(main, monitor);
        // выбранная сейчас картинка — подсвечена в списке
        Loaded += (_, _) =>
            LibraryList.SelectedItem = _viewModel.Library.FirstOrDefault(i => string.Equals(i.Path, _viewModel.Selected, StringComparison.OrdinalIgnoreCase));
    }

    private void OnLibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryList.SelectedItem is LibraryItem item) _viewModel.Selected = item.Path;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}

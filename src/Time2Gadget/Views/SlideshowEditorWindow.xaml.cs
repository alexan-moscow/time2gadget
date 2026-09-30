using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Time2Gadget.Services;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно шагов слайдшоу одного монитора (докладка 2026-09-30) — логика во ViewModels/SlideshowEditorViewModel; здесь перетаскивание.</summary>
public partial class SlideshowEditorWindow : Window
{
    private readonly SlideshowEditorViewModel _viewModel;
    private Point? _dragStart;
    private LibraryItem? _dragItem;

    public SlideshowEditorWindow(MainViewModel main, WallpaperMonitor monitor)
    {
        InitializeComponent();
        DataContext = _viewModel = new SlideshowEditorViewModel(main, monitor);
    }

    private void OnLibraryMouseDown(object sender, MouseButtonEventArgs e)
    {
        // «✕» у картинки — обычная кнопка, не начало перетаскивания
        _dragItem = FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is null
            ? (e.OriginalSource as FrameworkElement)?.DataContext as LibraryItem
            : null;
        _dragStart = _dragItem is null ? null : e.GetPosition(this);
    }

    private void OnLibraryMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragItem is not { } item || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        DragDrop.DoDragDrop(LibraryList, new DataObject(typeof(LibraryItem), item), DragDropEffects.Copy);
    }

    private void OnSlotDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LibraryItem)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(LibraryItem)) is LibraryItem item && (sender as FrameworkElement)?.DataContext is SlotItem slot)
            _viewModel.DropImage(slot, item.Path);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}

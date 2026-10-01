using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Панель быстрых таймеров (докладка 2026-10-01) — логика в ViewModels/MainViewModel.QuickPanel. Одна на программу:
/// клавиша или кнопка в окне «Быстрые таймеры» показывает/скрывает её, крестик в заголовке закрывает. Размер — по содержимому
/// (SizeToContent) × «Размер панели». Открыта при выходе — откроется при запуске. Место запоминается после перетаскивания.
/// </summary>
public partial class QuickPanelWindow : Window
{
    private static QuickPanelWindow? _current;
    private readonly MainViewModel _viewModel;

    private QuickPanelWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        ApplyScale();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => { viewModel.PropertyChanged -= OnViewModelPropertyChanged; _current = null; };
    }

    /// <summary>Клавиша / кнопка: открыта — скрыть, нет — показать.</summary>
    public static void Toggle(MainViewModel main)
    {
        if (_current is { IsVisible: true }) HidePanel(main);
        else ShowPanel(main);
    }

    /// <summary>Показать на запомненном месте (первый раз — у правого нижнего угла основного монитора).</summary>
    public static void ShowPanel(MainViewModel main)
    {
        _current ??= new QuickPanelWindow(main);
        var w = _current;
        if (!w.IsVisible)
        {
            var saved = main.QuickPanelPosition;
            if (saved is { } p) { w.Left = p.Left; w.Top = p.Top; }
            w.Show();
            if (saved is null) w.PlaceDefault(); // размер известен только после показа
            w.EnsureOnScreen();
        }
        main.SetQuickPanelOpen(true);
    }

    /// <summary>Сброс настроек: панель по умолчанию скрыта.</summary>
    public static void SyncWithSettings(MainViewModel main)
    {
        if (!main.QuickPanelOpen) _current?.Hide();
    }

    private static void HidePanel(MainViewModel main)
    {
        _current?.Hide();
        main.SetQuickPanelOpen(false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.QuickPanelScale) or "") ApplyScale();
    }

    private void ApplyScale() => ViewScale.ScaleX = ViewScale.ScaleY = _viewModel.QuickPanelScale;

    private void PlaceDefault()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 16;
        Top = work.Bottom - ActualHeight - 16;
    }

    /// <summary>Перетаскивание за любое место — пока панель не закреплена (клик ПКМ по кнопке в заголовке).</summary>
    private void OnChromeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || _viewModel.QuickPanelPinned) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        _viewModel.SaveQuickPanelPosition(Left, Top);
        EnsureOnScreen();
    }

    /// <summary>Крестик, клик ЛКМ — закрыть панель (клик ПКМ — «открывать снова, когда таймер закончится», команда VM).</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => HidePanel(_viewModel);

    /// <summary>Клик ПКМ по кнопкам заголовка — их собственное действие, меню сортировки виджета не открывается.</summary>
    private void OnHeaderButtonContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e) => e.Handled = true;

    /// <summary>Место вне мониторов (монитор отключили) — к правому нижнему углу основного.</summary>
    private void EnsureOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
        if (MonitorFromRect(ref r, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero) return;
        PlaceDefault();
        _viewModel.SaveQuickPanelPosition(Left, Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
}

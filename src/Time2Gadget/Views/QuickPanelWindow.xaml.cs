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
    public static void ShowPanel(MainViewModel main, bool forPreview = false)
    {
        // виджет включили (кнопкой, запуском синего/оранжевого таймера) — клавиша «Вкл/выкл виджет» становится активной сама;
        // временный показ ради просмотра эффекта её не включает
        if (!forPreview) main.QuickPanelHotkeyEnabled = true;
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

    public static bool IsShown => _current is { IsVisible: true };

    /// <summary>Просмотр эффекта закончился, а виджет открывался только ради него — скрыть.</summary>
    public static void HideForPreview(MainViewModel main) => HidePanel(main);

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

    // ---- Быстрое управление кликами по таймеру (галочка в окне «Быстрые таймеры», когда меню по ПКМ выключено) ----
    // Кнопки строки работают как обычно; клик по остальной строке — управление, перетаскивание виджета — за заголовок и края.

    private bool _rowPressed;

    private static bool OnButton(object? source, DependencyObject row)
    {
        for (var d = source as DependencyObject; d is not null && !ReferenceEquals(d, row); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private void OnRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.QuickPanelClickControlActive || OnButton(e.OriginalSource, (DependencyObject)sender)) return;
        _rowPressed = true;
        e.Handled = true; // не перетаскивать виджет
    }

    private void OnRowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rowPressed) return;
        _rowPressed = false;
        if (_viewModel.QuickPanelClickControlActive && sender is FrameworkElement { DataContext: QuickPanelTimer timer }) timer.PlayPauseFromRow();
    }

    private void OnRowMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.QuickPanelClickControlActive || OnButton(e.OriginalSource, (DependencyObject)sender)) return;
        if (sender is FrameworkElement { DataContext: QuickPanelTimer timer }) timer.QuickRightClick();
        e.Handled = true;
    }

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

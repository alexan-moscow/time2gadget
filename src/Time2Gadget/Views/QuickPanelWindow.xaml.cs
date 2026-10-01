using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Панель быстрых таймеров (докладка 2026-10-01) — логика в ViewModels/MainViewModel.QuickPanel. Одна на программу:
/// клавиша или кнопка в окне «Быстрые таймеры» показывает/скрывает её; таймер панели закончился — панель показывается.
/// Открыта при выходе — откроется при запуске. Место запоминается после перетаскивания.
/// </summary>
public partial class QuickPanelWindow : Window
{
    private const double ContentWidth = 300, ContentHeight = 132, ChromeMargin = 8;
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

    public static void ShowPanel(MainViewModel main)
    {
        _current ??= new QuickPanelWindow(main);
        var w = _current;
        if (!w.IsVisible)
        {
            if (main.QuickPanelPosition is { } p) { w.Left = p.Left; w.Top = p.Top; }
            else w.PlaceDefault();
            w.Show();
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
        if (e.PropertyName is nameof(MainViewModel.QuickPanelViewScale) or "") ApplyScale();
    }

    private void ApplyScale()
    {
        double s = _viewModel.QuickPanelViewScale;
        ViewScale.ScaleX = ViewScale.ScaleY = s;
        Width = ContentWidth * s + 2 * ChromeMargin;
        Height = ContentHeight * s + 2 * ChromeMargin;
    }

    /// <summary>Первый показ — у правого нижнего угла рабочей области основного монитора.</summary>
    private void PlaceDefault()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 16;
        Top = work.Bottom - Height - 16;
    }

    private void OnChromeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        _viewModel.SaveQuickPanelPosition(Left, Top);
        EnsureOnScreen();
    }

    /// <summary>Клик ПКМ по панели — скрыть (показать снова — клавиша или кнопка в окне «Быстрые таймеры»).</summary>
    private void OnChromeMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HidePanel(_viewModel);
    }

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

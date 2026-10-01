using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно «Заставка и фон экрана» (докладка 2026-09-30) — логика во ViewModels/DesktopBackgroundViewModel.</summary>
public partial class DesktopBackgroundWindow : Window
{
    private static DesktopBackgroundWindow? _current;

    public DesktopBackgroundWindow(MainViewModel main)
    {
        InitializeComponent();
        WindowMemory.Attach(this, main, "DesktopBackground"); // открывается там, где оставили
        MaxHeight = SystemParameters.WorkArea.Height; // выше экрана — прокрутка, а не обрезанное окно
        var viewModel = new DesktopBackgroundViewModel(main);
        DataContext = viewModel;
        // «Монитор N» в слайдшоу — окно шагов этого монитора (модальное: пока оно открыто, настройки не меняются из-под него).
        viewModel.OpenSlideshowEditor = monitor => new SlideshowEditorWindow(main, monitor) { Owner = this }.ShowDialog();
        // Плитка монитора статичной заставки — окно выбора картинки этого монитора.
        viewModel.OpenImagePicker = monitor => new WallpaperPickerWindow(main, monitor) { Owner = this }.ShowDialog();
        // Импорт: какие мониторы из архива на какие текущие (окно сопоставления).
        viewModel.ChooseImport = data =>
        {
            var dialog = new ImportMonitorsWindow(new ImportMonitorsViewModel(data)) { Owner = this };
            return dialog.ShowDialog() == true ? (dialog.Apply, dialog.Map) : null;
        };
        // «✕», сброс настроек — перечитать список и кнопки мониторов.
        EventHandler reload = (_, _) => viewModel.Reload();
        main.WallpaperChanged += reload;
        Closed += (_, _) => main.WallpaperChanged -= reload;
    }

    /// <summary>Одно окно на программу; уже открыто — вывести вперёд. Без владельца (клавиша быстрого открытия) — по центру экрана.</summary>
    public static void ShowSingle(MainViewModel main, Window? owner)
    {
        if (_current is null)
        {
            _current = new DesktopBackgroundWindow(main) { Owner = owner };
            if (owner is null && _current.WindowStartupLocation != WindowStartupLocation.Manual) _current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        else if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }
}

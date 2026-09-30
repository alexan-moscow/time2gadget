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
        MaxHeight = SystemParameters.WorkArea.Height; // выше экрана — прокрутка, а не обрезанное окно
        var viewModel = new DesktopBackgroundViewModel(main);
        DataContext = viewModel;
        // «Монитор N» в слайдшоу — окно шагов этого монитора (модальное: пока оно открыто, настройки не меняются из-под него).
        viewModel.OpenSlideshowEditor = monitor => new SlideshowEditorWindow(main, monitor) { Owner = this }.ShowDialog();
        // «✕», сброс настроек — перечитать список и кнопки мониторов.
        EventHandler reload = (_, _) => viewModel.Reload();
        main.WallpaperChanged += reload;
        Closed += (_, _) => main.WallpaperChanged -= reload;
    }

    /// <summary>Одно окно на программу; уже открыто — вывести вперёд.</summary>
    public static void ShowSingle(MainViewModel main, Window owner)
    {
        if (_current is null)
        {
            _current = new DesktopBackgroundWindow(main) { Owner = owner };
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        _current.Activate();
    }
}

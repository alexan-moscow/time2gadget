using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно «Размер и положение окон программ» (докладка 2026-09-29) — логика во ViewModels/WindowProfilesViewModel.</summary>
public partial class WindowProfilesWindow : Window
{
    private static WindowProfilesWindow? _current;
    private readonly WindowProfilesViewModel _viewModel;

    public WindowProfilesWindow(MainViewModel main)
    {
        InitializeComponent();
        _placed = WindowMemory.Attach(this, main, "WindowProfiles"); // открывается там, где оставили
        DataContext = _viewModel = new WindowProfilesViewModel(main);
        Loaded += (_, _) => FitHeightToOptions();
    }

    /// <summary>Служба профилей применила профиль к новому окну — если это окно открыто, окно появится в списке.</summary>
    public static void NotifyWindowProfiled(IntPtr hwnd) => _current?._viewModel.OnWindowProfiled(hwnd);

    /// <summary>Окно с профилем закрыто — убрать из списка.</summary>
    public static void NotifyWindowGone(IntPtr hwnd) => _current?._viewModel.OnWindowGone(hwnd);

    /// <summary>
    /// Одно окно на программу: из настроек (owner — окно настроек) и по клавише быстрого открытия (без владельца — по центру
    /// монитора с указателем; <paramref name="select"/> — окно, активное в момент нажатия, сразу выбирается в списке).
    /// Уже открыто — вывести вперёд.
    /// </summary>
    private readonly bool _placed;

    public static void ShowSingle(MainViewModel main, Window? owner, IntPtr select = default)
    {
        if (_current is null)
        {
            _current = new WindowProfilesWindow(main) { Owner = owner };
            if (owner is null) _current.WindowStartupLocation = WindowStartupLocation.Manual; // место — FitHeightToOptions или запомненное
            _current.Closed += (_, _) => _current = null;
            if (select != IntPtr.Zero) _current._viewModel.SelectWindow(select);
            _current.Show();
        }
        else
        {
            if (select != IntPtr.Zero) _current._viewModel.SelectWindow(select);
            if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        }
        _current.Activate();
    }

    /// <summary>
    /// Высота — чтобы все настройки справа были видны целиком (докладка 2026-09-29), но не выше рабочей области экрана;
    /// список окон слева при этом прокручивается. Окно остаётся по центру окна настроек (без него — по центру монитора
    /// с указателем) и в пределах экрана.
    /// </summary>
    private void FitHeightToOptions()
    {
        RightPanel.Measure(new Size(RightScroll.ActualWidth, double.PositiveInfinity));
        double chrome = ActualHeight - ContentGrid.ActualHeight;             // заголовок, рамка и поля окна
        double wanted = RightPanel.DesiredSize.Height + chrome + 4;

        Rect area = SystemParameters.WorkArea;
        if (Owner is null && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var wa = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            var toWpf = target.TransformFromDevice;
            area = new Rect(toWpf.Transform(new Point(wa.Left, wa.Top)), toWpf.Transform(new Point(wa.Right, wa.Bottom)));
        }
        Height = Math.Min(Math.Max(wanted, MinHeight), area.Height);

        if (_placed) { Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)); return; } // запомненное место — только не ниже экрана
        double top;
        if (Owner is { } owner) top = owner.Top + (owner.ActualHeight - Height) / 2;
        else
        {
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            top = area.Top + (area.Height - Height) / 2;
        }
        Top = Math.Max(area.Top, Math.Min(top, area.Bottom - Height));
    }
}

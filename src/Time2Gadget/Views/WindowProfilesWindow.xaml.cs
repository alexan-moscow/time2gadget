using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно «Размер и положение окон программ» (докладка 2026-09-29) — логика во ViewModels/WindowProfilesViewModel.</summary>
public partial class WindowProfilesWindow : Window
{
    public WindowProfilesWindow(MainViewModel main)
    {
        InitializeComponent();
        DataContext = new WindowProfilesViewModel(main);
        Loaded += (_, _) => FitHeightToOptions();
    }

    /// <summary>
    /// Высота — чтобы все настройки справа были видны целиком (докладка 2026-09-29), но не выше рабочей области экрана;
    /// список окон слева при этом прокручивается. Окно остаётся по центру окна настроек и в пределах экрана.
    /// </summary>
    private void FitHeightToOptions()
    {
        RightPanel.Measure(new Size(RightScroll.ActualWidth, double.PositiveInfinity));
        double chrome = ActualHeight - ContentGrid.ActualHeight;             // заголовок, рамка и поля окна
        double wanted = RightPanel.DesiredSize.Height + chrome + 4;
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Math.Max(wanted, MinHeight), area.Height);

        double top = Owner is { } owner ? owner.Top + (owner.ActualHeight - Height) / 2 : Top;
        Top = Math.Max(area.Top, Math.Min(top, area.Bottom - Height));
    }
}

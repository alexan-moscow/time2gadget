using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>Окно «Профили размера окон» (докладка 2026-09-29) — логика во ViewModels/WindowProfilesViewModel.</summary>
public partial class WindowProfilesWindow : Window
{
    public WindowProfilesWindow(MainViewModel main)
    {
        InitializeComponent();
        DataContext = new WindowProfilesViewModel(main);
    }
}

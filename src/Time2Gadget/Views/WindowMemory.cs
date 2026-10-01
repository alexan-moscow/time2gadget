using System.Windows;
using System.Windows.Interop;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Окна программы открываются там, где их оставили (докладка 2026-10-01; раньше так умело только окно настроек):
/// место (и размер — у растягиваемых окон) сохраняется по концу перетаскивания человеком (WM_EXITSIZEMOVE — сдвиг от Windows
/// при гашении монитора не сохраняется) и при закрытии; при открытии — только если это место видно на экране, иначе
/// окно открывается как раньше (по центру владельца или экрана).
/// </summary>
internal static class WindowMemory
{
    private const int WmExitSizeMove = 0x0232;

    /// <summary>Подключить окно; true — поставлено на запомненное место (WindowStartupLocation = Manual).</summary>
    public static bool Attach(Window window, MainViewModel main, string key, bool rememberSize = false)
    {
        bool placed = false;
        if (main.GetWindowPlacement(key) is { } p)
        {
            if (rememberSize && p.Width > 0 && p.Height > 0)
            {
                window.Width = Math.Max(p.Width, window.MinWidth);
                window.Height = Math.Max(p.Height, window.MinHeight);
            }
            double w = double.IsNaN(window.Width) ? 300 : window.Width, h = double.IsNaN(window.Height) ? 200 : window.Height;
            if (MainWindow.IsVisibleOnScreen(p.Left, p.Top, w, h))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = p.Left;
                window.Top = p.Top;
                placed = true;
            }
        }

        void Save()
        {
            if (window.WindowState != WindowState.Normal) return;
            main.SaveWindowPlacement(key, window.Left, window.Top, rememberSize ? window.ActualWidth : 0, rememberSize ? window.ActualHeight : 0);
        }
        window.Closing += (_, _) => Save();
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(
            (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WmExitSizeMove) Save();
                return IntPtr.Zero;
            });
        return placed;
    }
}

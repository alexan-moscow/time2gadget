using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Time2Gadget.Models;

namespace Time2Gadget.Views;

/// <summary>
/// Сообщение на весь экран — действие после окончания быстрого таймера (докладка 2026-10-01): поверх всех окон на мониторе
/// с указателем мыши, свой фон, текст сверху / по центру / снизу, свой размер шрифта. Закрывается кликом или любой клавишей.
/// </summary>
public sealed class MessageOverlayWindow : Window
{
    public MessageOverlayWindow(string text, MessagePosition position, string colorHex, double fontSize, int transparencyPercent)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Topmost = true;
        ShowInTaskbar = false;
        Title = "Тайм2гаджет";
        var color = ParseColor(colorHex);
        // прозрачность фона — из настроек (0 — сплошной); совсем прозрачным не делаем — окно должно ловить клик для закрытия
        byte alpha = (byte)Math.Max(1, Math.Round(255 * (100 - Math.Clamp(transparencyPercent, 0, 100)) / 100.0));
        Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        // светлый фон — тёмный текст
        var fore = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 150 ? Brushes.Black : Brushes.White;

        var grid = new Grid();
        grid.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = Math.Clamp(fontSize, 12, 300),
            FontWeight = FontWeights.SemiBold,
            Foreground = fore,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = position switch
            {
                MessagePosition.Top => VerticalAlignment.Top,
                MessagePosition.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center,
            },
            Margin = new Thickness(60, 80, 60, 80),
        });
        grid.Children.Add(new TextBlock
        {
            Text = "Клик или любая клавиша — закрыть",
            FontSize = 14,
            Foreground = fore,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
        });
        Content = grid;

        // на мониторе с указателем: ставим окно туда и разворачиваем на весь монитор
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
        double scale = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this).DpiScaleX;
        Left = screen.Left / scale + 10;
        Top = screen.Top / scale + 10;
        Width = 400;
        Height = 300;
        Loaded += (_, _) => { WindowState = WindowState.Maximized; Activate(); Focus(); };

        MouseDown += (_, _) => Close();
        KeyDown += (_, e) => { e.Handled = true; Close(); };
    }

    private static Color ParseColor(string? hex)
    {
        try { return hex is null ? Colors.Black : (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Colors.Black; }
    }
}

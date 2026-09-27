using System.Windows.Forms;

namespace Time2Gadget.Services;

/// <inheritdoc cref="ITrayService"/>
/// <remarks>
/// Реализовано через System.Windows.Forms.NotifyIcon (docs/DECISIONS.md, 2026-09-26) — у WPF
/// нет собственного tray API; это единственное место в проекте, использующее WinForms-типы.
/// </remarks>
public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;

    public event EventHandler? ShowRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Тайм2гаджет",
            ContextMenuStrip = menu
        };
        // Левый клик — вернуть окно (пункт «Показать таймер» убран из меню, это единственный путь назад из трея).
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Берёт иконку прямо из Win32-ресурсов уже запущенного .exe (встроена туда сборкой через
    /// &lt;ApplicationIcon&gt;Assets\app.ico&lt;/ApplicationIcon&gt; в .csproj) — "уменьшенная копия"
    /// приложения без отдельного WPF pack-resource, см. docs/DECISIONS.md, 2026-09-27.
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null) return icon;
            }
        }
        catch { /* откат ниже */ }
        return System.Drawing.SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, ToolTipIcon.None);
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}

using System.Windows.Forms;

namespace TimerGadget.Services;

/// <inheritdoc cref="ITrayService"/>
/// <remarks>
/// Реализовано через System.Windows.Forms.NotifyIcon (docs/DECISIONS.md, 2026-09-26) — у WPF
/// нет собственного tray API; это единственное место в проекте, использующее WinForms-типы.
/// </remarks>
public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _alwaysOnTopItem;
    private ToolStripMenuItem? _launchAtStartupItem;

    public event EventHandler? ShowRequested;
    public event EventHandler? StartPauseRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler<bool>? AlwaysOnTopToggled;
    public event EventHandler<bool>? LaunchAtStartupToggled;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Показать таймер");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var startPauseItem = new ToolStripMenuItem("Start / Pause");
        startPauseItem.Click += (_, _) => StartPauseRequested?.Invoke(this, EventArgs.Empty);

        var resetItem = new ToolStripMenuItem("Reset");
        resetItem.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);

        _alwaysOnTopItem = new ToolStripMenuItem("Always on Top") { CheckOnClick = true };
        _alwaysOnTopItem.Click += (_, _) => AlwaysOnTopToggled?.Invoke(this, _alwaysOnTopItem.Checked);

        _launchAtStartupItem = new ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = true };
        _launchAtStartupItem.Click += (_, _) => LaunchAtStartupToggled?.Invoke(this, _launchAtStartupItem.Checked);

        var settingsItem = new ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(showItem);
        menu.Items.Add(startPauseItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_alwaysOnTopItem);
        menu.Items.Add(_launchAtStartupItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Таймер",
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
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

    public void SetAlwaysOnTopChecked(bool value)
    {
        if (_alwaysOnTopItem is not null) _alwaysOnTopItem.Checked = value;
    }

    public void SetLaunchAtStartupChecked(bool value)
    {
        if (_launchAtStartupItem is not null) _launchAtStartupItem.Checked = value;
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

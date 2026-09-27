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
            Icon = _appIcon ??= LoadAppIcon(),
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

    // ============ Живая иконка: кольцо прогресса (docs/UI-CONTRACT.md → Tray, 2026-09-27) ============
    // Цвета — из Theme.xaml (Color.Background/BorderSubtle/Accent/TextSecondary/Finish); WinForms-иконка
    // рисуется GDI+, поэтому значения продублированы здесь, а не читаются из WPF-ресурсов.
    private static readonly System.Drawing.Color IconBackground = System.Drawing.Color.FromArgb(0x0E, 0x14, 0x20);
    private static readonly System.Drawing.Color IconTrack = System.Drawing.Color.FromArgb(0x2A, 0x35, 0x47);
    private static readonly System.Drawing.Color IconAccent = System.Drawing.Color.FromArgb(0x3D, 0x8B, 0xFF);
    private static readonly System.Drawing.Color IconPaused = System.Drawing.Color.FromArgb(0x8C, 0xA0, 0xBE);
    private static readonly System.Drawing.Color IconFinish = System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x4A);
    private static readonly System.Drawing.Color IconFinishDim = System.Drawing.Color.FromArgb(0x5A, 0x2A, 0x22);
    private const int ProgressSteps = 64; // шаг перерисовки дуги — мельче на иконке 16px всё равно не видно

    private System.Drawing.Icon? _appIcon;
    private IntPtr _dynamicIconHandle;
    private (Models.TimerStatus, int, bool, bool)? _renderedKey;

    public void Update(Models.TrayIconState state)
    {
        if (_notifyIcon is null) return;

        var tooltip = state.Tooltip.Length > 63 ? state.Tooltip[..63] : state.Tooltip; // лимит NotifyIcon.Text
        if (_notifyIcon.Text != tooltip) _notifyIcon.Text = tooltip;

        var key = (state.Status, (int)Math.Round(Math.Clamp(state.ProgressFraction, 0, 1) * ProgressSteps), state.IsBlinking, state.BlinkOn);
        if (_renderedKey == key) return;
        _renderedKey = key;

        if (state.Status == Models.TimerStatus.Ready)
        {
            SetIcon(_appIcon ??= LoadAppIcon(), IntPtr.Zero); // таймер не идёт — обычная иконка приложения
            return;
        }

        int size = SystemInformation.SmallIconSize.Width; // 16 при 100%, больше при высоком DPI — рисуем без масштабирования
        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            float thickness = Math.Max(2f, size * 0.2f);
            var ring = new System.Drawing.RectangleF(thickness / 2 + 0.5f, thickness / 2 + 0.5f,
                size - thickness - 1f, size - thickness - 1f);

            using (var bg = new System.Drawing.SolidBrush(IconBackground))
                g.FillEllipse(bg, 0, 0, size - 1, size - 1);
            using (var track = new System.Drawing.Pen(IconTrack, thickness))
                g.DrawEllipse(track, ring);

            switch (state.Status)
            {
                case Models.TimerStatus.Running:
                case Models.TimerStatus.Paused:
                    float sweep = Math.Max(4f, (float)(Math.Clamp(state.ProgressFraction, 0, 1) * 360.0));
                    using (var arc = new System.Drawing.Pen(state.Status == Models.TimerStatus.Running ? IconAccent : IconPaused, thickness))
                        g.DrawArc(arc, ring, -90f, sweep); // от 12 часов по часовой — как ProgressRingControl
                    break;

                case Models.TimerStatus.Finished:
                    // Красное кольцо; если окно скрыто — мигает: яркая фаза с красной серединой, тусклая без.
                    bool dimPhase = state.IsBlinking && !state.BlinkOn;
                    using (var arc = new System.Drawing.Pen(dimPhase ? IconFinishDim : IconFinish, thickness))
                        g.DrawEllipse(arc, ring);
                    if (state.IsBlinking && state.BlinkOn)
                    {
                        float dot = size * 0.34f;
                        using var core = new System.Drawing.SolidBrush(IconFinish);
                        g.FillEllipse(core, (size - dot) / 2, (size - dot) / 2, dot, dot);
                    }
                    break;
            }
        }

        var handle = bmp.GetHicon();
        SetIcon(System.Drawing.Icon.FromHandle(handle), handle);
    }

    /// <summary>Ставит новую иконку и освобождает HICON предыдущей динамической (иначе утечка GDI-хэндлов раз в секунду).</summary>
    private void SetIcon(System.Drawing.Icon icon, IntPtr newHandle)
    {
        if (_notifyIcon is null) return;
        _notifyIcon.Icon = icon;
        if (_dynamicIconHandle != IntPtr.Zero) DestroyIcon(_dynamicIconHandle);
        _dynamicIconHandle = newHandle;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

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
        if (_dynamicIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_dynamicIconHandle);
            _dynamicIconHandle = IntPtr.Zero;
        }
    }
}

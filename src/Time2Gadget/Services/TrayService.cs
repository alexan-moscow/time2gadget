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
            Text = "Тайм2гаджет",
            ContextMenuStrip = menu
        };
        // Левый клик — вернуть окно (пункт «Показать таймер» убран из меню, это единственный путь назад из трея).
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty);
        };

        // Пустое кольцо рисуется ДО показа иконки — раньше сначала на долю секунды появлялась иконка
        // приложения (кольцо на ~70%), а потом её заменяло пустое кольцо (докладка 2026-09-27: «промаргивает»).
        Update(new Models.TrayIconState(Models.TimerStatus.Ready, 0, Models.FinishVisualEffect.None, 0, false, "Тайм2гаджет"));
        _notifyIcon.Visible = true;
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
    private static readonly System.Drawing.Color IconAmber = System.Drawing.Color.FromArgb(0xFF, 0xD5, 0x4A); // как ColorCycle в MainWindow
    private const int ProgressSteps = 64; // шаг перерисовки дуги — мельче на иконке 16px всё равно не видно

    private IntPtr _dynamicIconHandle;
    private (Models.TimerStatus, int, int, int, int)? _renderedKey;

    /// <summary>Как выглядит законченный таймер в этот момент: цвет кольца, заливка середины (с альфой), множитель толщины.</summary>
    private readonly record struct FinishLook(System.Drawing.Color Ring, System.Drawing.Color Fill, float ThicknessScale);

    /// <summary>
    /// Эффекты завершения в трее — те же, что на циферблате (MainWindow.BuildFinishEffectStoryboard), с теми
    /// же периодами: заливка середины иконки играет роль EffectOverlay, толщина кольца — роль масштаба.
    /// </summary>
    private static FinishLook GetFinishLook(Models.TrayIconState state)
    {
        double t = state.EffectSeconds;
        switch (state.ActiveEffect)
        {
            case Models.FinishVisualEffect.Flash:
            {
                // строб: 0 → 0.65 за 70мс → 0 к 160мс → пауза до 500мс
                double p = t % 0.5;
                double intensity = p < 0.07 ? p / 0.07 : p < 0.16 ? 1 - (p - 0.07) / 0.09 : 0;
                return new(IconFinish, WithAlpha(IconFinish, 0.9 * intensity), 1f);
            }
            case Models.FinishVisualEffect.Pulse:
            {
                // пульсация: период 0.9с (0.45 туда + 0.45 обратно), подсветка 0.15 → 0.55, «раздувание» кольца
                double v = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (t % 0.9) / 0.9);
                return new(IconFinish, WithAlpha(IconFinish, 0.15 + 0.4 * v), 1f + 0.7f * (float)v);
            }
            case Models.FinishVisualEffect.ColorCycle:
            {
                // волна цвета красный → янтарный → красный за 2с, подсветка 0.4
                double p = t % 2.0;
                var color = Lerp(IconFinish, IconAmber, p < 1 ? p : 2 - p);
                return new(color, WithAlpha(color, 0.4), 1f);
            }
            default:
                if (!state.BlinkWhileHidden) return new(IconFinish, System.Drawing.Color.Transparent, 1f);
                bool on = t % 1.0 < 0.5; // простое мигание: 0.5с горит с заливкой, 0.5с тускло
                return on
                    ? new(IconFinish, WithAlpha(IconFinish, 0.85), 1f)
                    : new(IconFinishDim, System.Drawing.Color.Transparent, 1f);
        }
    }

    // Альфа квантуется (16 уровней), чтобы плавные эффекты не перерисовывали иконку на каждом кадре без видимой разницы.
    private static System.Drawing.Color WithAlpha(System.Drawing.Color c, double alpha) =>
        System.Drawing.Color.FromArgb((int)Math.Round(Math.Clamp(alpha, 0, 1) * 15) * 17, c);

    private static System.Drawing.Color Lerp(System.Drawing.Color a, System.Drawing.Color b, double k)
    {
        k = Math.Round(Math.Clamp(k, 0, 1) * 24) / 24; // 24 шага цвета — плавно на глаз, без лишних перерисовок
        return System.Drawing.Color.FromArgb(
            (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    public void Update(Models.TrayIconState state)
    {
        if (_notifyIcon is null) return;

        var tooltip = state.Tooltip.Length > 63 ? state.Tooltip[..63] : state.Tooltip; // лимит NotifyIcon.Text
        if (_notifyIcon.Text != tooltip) _notifyIcon.Text = tooltip;

        var look = state.Status == Models.TimerStatus.Finished ? GetFinishLook(state) : default;
        var key = (state.Status, (int)Math.Round(Math.Clamp(state.ProgressFraction, 0, 1) * ProgressSteps),
            look.Ring.ToArgb(), look.Fill.ToArgb(), (int)Math.Round(look.ThicknessScale * 10));
        if (_renderedKey == key) return;
        _renderedKey = key;

        int size = SystemInformation.SmallIconSize.Width; // 16 при 100%, больше при высоком DPI — рисуем без масштабирования
        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Толщина — как у кольца в иконке приложения (~11% размера), 2026-09-27: 20% читалось грубо.
            float thickness = Math.Max(1.5f, size * 0.11f);
            var ring = new System.Drawing.RectangleF(thickness / 2 + 0.5f, thickness / 2 + 0.5f,
                size - thickness - 1f, size - thickness - 1f);

            using (var bg = new System.Drawing.SolidBrush(IconBackground))
                g.FillEllipse(bg, 0, 0, size - 1, size - 1);
            using (var track = new System.Drawing.Pen(IconTrack, thickness))
                g.DrawEllipse(track, ring);

            // Ready — только пустая дорожка, «незаполненный круг» (2026-09-27): иконка приложения — кольцо,
            // заполненное на ~70%, и в трее выглядела как уже идущий таймер.
            switch (state.Status)
            {
                case Models.TimerStatus.Running:
                case Models.TimerStatus.Paused:
                    float sweep = Math.Max(4f, (float)(Math.Clamp(state.ProgressFraction, 0, 1) * 360.0));
                    using (var arc = new System.Drawing.Pen(state.Status == Models.TimerStatus.Running ? IconAccent : IconPaused, thickness))
                        g.DrawArc(arc, ring, -90f, sweep); // от 12 часов по часовой — как ProgressRingControl
                    break;

                case Models.TimerStatus.Finished:
                    // Кольцо + заливка середины по текущему кадру эффекта (GetFinishLook).
                    if (look.Fill.A > 0)
                    {
                        using var fill = new System.Drawing.SolidBrush(look.Fill);
                        g.FillEllipse(fill, ring);
                    }
                    float finishThickness = thickness * look.ThicknessScale;
                    var finishRing = System.Drawing.RectangleF.Inflate(ring, (thickness - finishThickness) / 2, (thickness - finishThickness) / 2);
                    using (var arc = new System.Drawing.Pen(look.Ring, finishThickness))
                        g.DrawEllipse(arc, finishRing);
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

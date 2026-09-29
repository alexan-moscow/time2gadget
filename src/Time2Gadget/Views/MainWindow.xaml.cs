using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Time2Gadget.Controls;
using Time2Gadget.Models;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// View оболочки окна. Только визуальные вещи (docs/ARCHITECTURE.md → MVVM-слои):
/// ручной drag/клик-по-центру, переключение Normal/Compact-геометрии, визуальные эффекты
/// отсчёта, сворачивание в трей/закрытие. Вся бизнес-логика — в MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    // Размеры СОДЕРЖИМОГО (без 2*8 Chrome margin) при масштабе 1.0; окно = содержимое * масштаб + 16
    // (LayoutTransform на Chrome масштабирует содержимое, внешний margin — нет).
    private const double NormalContentWidth = 257, NormalContentHeight = 325;  // 16(top)+225(кольцо)+16(зазор)+52(кнопки)+16(bottom) — везде одинаковый отступ 16 (докладка 2026-09-27)
    private const double CompactContentWidth = 220, CompactContentHeight = 132; // подобрано замером пикселей: самый высокий вариант (таймер ММ:СС + строка часов) помещается, лишнего зазора нет
    private const double ChromeMargin = 8;

    // Угловые кнопки. В компакте — ближе к углам, автозакрытие — ПОД крестиком (докладка 2026-09-27: больше
    // места под крупный таймер/часы); вертикальный шаг = прежнему горизонтальному (22px между центрами).
    private static readonly Thickness NormalSettingsMargin = new(10), CompactSettingsMargin = new(5);
    private static readonly Thickness NormalCloseMargin = new(0, 10, 10, 0), CompactCloseMargin = new(0, 5, 5, 0);
    private static readonly Thickness NormalAutoCloseMargin = new(0, 10, 32, 0), CompactAutoCloseMargin = new(0, 27, 5, 0);

    private readonly MainViewModel _viewModel;
    private Views.SettingsWindow? _settingsWindow;

    // ---- ручное перетаскивание + различение клика от драга (docs/UI-CONTRACT.md, 2026-09-27) ----
    private Point? _mouseDownScreenPos;
    private Point? _mouseDownWindowPos;
    private bool _isDraggingWindow;
    private bool _mouseDownOnCenter;

    private Storyboard? _dialEffectStoryboard;
    private readonly HintController _hints;
    private readonly Services.WindowLayoutService _windowLayout;
    private readonly Services.AppWindowMemoryService _appWindowMemory;
    private readonly Services.WindowProfileService _windowProfileService;
    private readonly Services.CursorConfineService _cursorConfine;
    private readonly Services.GlobalHotkeyService _globalHotkeys;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SettingsRequested += (_, _) => OpenSettings();
        _viewModel.SettingsReset += (_, _) =>
        {
            OnSettingsReset();
            _settingsWindow?.CenterOnOwner();
        };

        _hints = new HintController(RootGrid, target => HintService.GetKey(target) == "AutoClose"
            ? BuildAutoCloseHint()
            : _viewModel.GetHint(HintService.GetKey(target)!, target.Tag));
        Deactivated += (_, _) => _hints.Cancel();

        // Автозакрытие после таймера — ровно как нажатие крестика (свернуть в трей или выйти по настройке).
        _viewModel.AutoCloseRequested += (_, _) => HandleCloseRequest();
        // Скрытое окно + законченный таймер → иконка трея мигает красным (MainViewModel.UpdateTray).
        IsVisibleChanged += (_, _) => UpdateHiddenState();
        StateChanged += (_, _) => UpdateHiddenState(); // свёрнуто на панель задач (Win+D) — тоже «не видно»
        // Кнопки на панели задач нет (докладка 2026-09-28: программа живёт только в трее) — свёрнутое окно
        // осталось бы полоской в углу экрана, поэтому сворачивание (Win+D и т.п.) = убрать в трей.
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) return;
            WindowState = WindowState.Normal;
            _viewModel.SaveWindowPosition(_viewModel.IsCompactMode, Left, Top);
            Hide();
        };

        // После сна/переподключения мониторов Windows переносит окна на доступный в тот момент монитор
        // (докладка 2026-09-28: компакт «уехал» на нижний монитор) — возвращаем на сохранённые места.
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Окна ДРУГИХ программ — отдельная служба, по настройке (по умолчанию выкл); нужен HWND этого окна
        // для уведомлений «экран гаснет/включился».
        _windowLayout = new Services.WindowLayoutService(this);
        SourceInitialized += (_, _) => _windowLayout.Enabled = _viewModel.RestoreOtherWindows;
        // Окна программ между их запусками — тоже по настройке (докладка 2026-09-28, по умолчанию выкл).
        _appWindowMemory = new Services.AppWindowMemoryService();
        SourceInitialized += (_, _) => _appWindowMemory.Enabled = _viewModel.RememberAppWindows;
        // Закреплённые фоны рабочего стола: слайд-шоу могли включить, пока программа не работала (докладка 2026-09-29).
        SourceInitialized += (_, _) => _viewModel.PinWallpapersNow();
        // Профили размера окон: автоприменение к программам с назначенным профилем (докладка 2026-09-29); у таких
        // программ память окон не срабатывает — профиль главнее.
        // Указатель мыши в окне (докладка 2026-09-29): одна служба на программу, её используют профили и окно профилей.
        _cursorConfine = new Services.CursorConfineService();
        _viewModel.CursorConfine = _cursorConfine;
        _windowProfileService = new Services.WindowProfileService(key => _viewModel.FindProfileFor(key), _viewModel.SetCursorConfine);
        _appWindowMemory.IsProfiled = key => _viewModel.FindProfileFor(key) is not null;
        // Окно с автоприменяемым профилем открылось/закрылось — список окна «Размер и положение окон программ» (если оно открыто).
        _windowProfileService.WindowProfiled += (_, hwnd) => WindowProfilesWindow.NotifyWindowProfiled(hwnd);
        _windowProfileService.WindowGone += (_, hwnd) => WindowProfilesWindow.NotifyWindowGone(hwnd);
        SourceInitialized += (_, _) => UpdateWindowProfileService();
        _viewModel.WindowProfilesChanged += (_, _) => UpdateWindowProfileService();
        // Быстрое открытие окна «Размер и положение окон программ» клавишей — с выбранным окном, активным в момент нажатия.
        _viewModel.WindowProfilesRequested += (_, foreground) => WindowProfilesWindow.ShowSingle(_viewModel, null, foreground);

        // Клавиши (докладка 2026-09-28): окна — здесь, пока окно в фокусе; глобальные — через RegisterHotKey/хук мыши.
        PreviewKeyDown += OnWindowKeyDown;
        PreviewMouseDown += OnWindowMouseDown;
        _globalHotkeys = new Services.GlobalHotkeyService(this);
        _globalHotkeys.Pressed += (_, id) => _viewModel.OnGlobalHotkey(id);
        _viewModel.HotkeysChanged += (_, _) => ApplyGlobalHotkeys();
        _viewModel.ToggleWindowRequested += (_, _) => ToggleWindow();
        HotkeyBox.CaptureChanged += OnHotkeyCaptureChanged;
        SourceInitialized += (_, _) => ApplyGlobalHotkeys();

        Closed += (_, _) =>
        {
            HotkeyBox.CaptureChanged -= OnHotkeyCaptureChanged;
            _globalHotkeys.Dispose();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _windowLayout.Dispose();
            _appWindowMemory.Dispose();
            _windowProfileService.Dispose();
            _cursorConfine.Dispose();
        };
    }

    // ============ Возврат окон на места после сна (docs/DECISIONS.md, 2026-09-28) ============
    // Мониторы после пробуждения инициализируются не сразу и не одновременно: пока «свой» монитор не появился,
    // окно стоит там, куда его переставила Windows. Поэтому несколько попыток с растущей паузой: как только
    // сохранённое место снова видно на экране — ставим окно туда. Сохранённое место при этом не перезаписывается
    // (переставленная Windows позиция нигде не сохраняется — сохраняем только после перетаскивания/переключения).
    // То же расписание, что у окон других программ (частые попытки в начале, 2026-09-28).
    private static readonly TimeSpan[] RestoreAttemptDelays = Services.WindowLayoutService.RestoreAttemptDelays;
    private DispatcherTimer? _restoreTimer;
    private int _restoreAttempt;

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(ScheduleRestorePositions);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ScheduleRestorePositions);

    private void ScheduleRestorePositions()
    {
        _restoreAttempt = 0;
        _restoreTimer ??= new DispatcherTimer();
        _restoreTimer.Stop();
        _restoreTimer.Tick -= OnRestoreTick;
        _restoreTimer.Tick += OnRestoreTick;
        _restoreTimer.Interval = RestoreAttemptDelays[0];
        _restoreTimer.Start();
    }

    private void OnRestoreTick(object? sender, EventArgs e)
    {
        RestoreSavedPositions();
        _restoreAttempt++;
        if (_restoreAttempt >= RestoreAttemptDelays.Length) { _restoreTimer!.Stop(); return; }
        _restoreTimer!.Interval = RestoreAttemptDelays[_restoreAttempt] - RestoreAttemptDelays[_restoreAttempt - 1];
    }

    /// <summary>Поставить главное окно и окно настроек на сохранённые места, если те снова видны на экране.</summary>
    private void RestoreSavedPositions()
    {
        if (_viewModel.GetSavedWindowPosition(_viewModel.IsCompactMode) is { } saved
            && IsVisibleOnScreen(saved.X, saved.Y, Width, Height)
            && (Math.Abs(Left - saved.X) > 1 || Math.Abs(Top - saved.Y) > 1))
        {
            Left = saved.X;
            Top = saved.Y;
            _lastAnchorOnScreen = null; // привязка «туда-обратно» больше не актуальна
        }
        _settingsWindow?.RestoreSavedPosition();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModeLayout(_viewModel.IsCompactMode); // сначала размер — чтобы проверить, видно ли окно на экране

        if (_viewModel.GetSavedWindowPosition(_viewModel.IsCompactMode, allowLegacy: true) is { } saved
            && IsVisibleOnScreen(saved.X, saved.Y, Width, Height))
        {
            Left = saved.X;
            Top = saved.Y;
        }
        UpdateCompactProgressBar();
        ApplyDialEffect();

        // Перезапуск ради смены прав был из открытых настроек — открыть их снова с той же прокруткой (место окна
        // настроек восстанавливается как обычно).
        if (_viewModel.TakeReopenSettingsScroll() is { } scroll)
        {
            OpenSettings();
            _settingsWindow?.ScrollToOffset(scroll);
        }
    }

    /// <summary>Служба профилей работает, пока есть хоть одно назначение; изменения — сразу применить к открытым окнам.</summary>
    private void UpdateWindowProfileService()
    {
        bool wasEnabled = _windowProfileService.Enabled;
        _windowProfileService.Enabled = _viewModel.Settings.WindowProfileAssignments.Count > 0;
        if (wasEnabled && _windowProfileService.Enabled) _windowProfileService.ApplyToOpenWindows();
    }

    /// <summary>
    /// Перед перезапуском (смена прав): запомнить, открыты ли настройки, их прокрутку и место — заранее, до запуска
    /// новой копии: она может прочитать настройки раньше, чем эта закроется.
    /// </summary>
    public void RememberSettingsForRestart()
    {
        if (_settingsWindow is { } settings)
        {
            _viewModel.SaveSettingsWindowPosition(settings.Left, settings.Top);
            _viewModel.RememberSettingsForRestart(settings.ScrollOffset);
        }
        else
        {
            _viewModel.RememberSettingsForRestart(null);
        }
    }

    private void UpdateHiddenState() =>
        _viewModel.IsWindowHidden = !IsVisible || WindowState == WindowState.Minimized;

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Views.SettingsWindow(_viewModel) { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }

        // Мигающая шестерёнка = найдено обновление: открываем настройки сразу на разделе с кнопкой установки
        // (докладка 2026-09-27), иначе пользователь листает до низа сам.
        if (_viewModel.IsUpdateAvailable) _settingsWindow.ScrollToUpdates();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsCompactMode):
            case nameof(MainViewModel.FullViewScale):
            case nameof(MainViewModel.CompactViewScale):
                ApplyModeLayout(_viewModel.IsCompactMode);
                break;
            case nameof(MainViewModel.ProgressFraction):
                UpdateCompactProgressBar();
                break;
            case nameof(MainViewModel.RestoreOtherWindows):
            case nameof(MainViewModel.RememberAppWindows):
            case "": // сброс настроек — перечитать всё
                _windowLayout.Enabled = _viewModel.RestoreOtherWindows;
                _appWindowMemory.Enabled = _viewModel.RememberAppWindows;
                break;
            case nameof(MainViewModel.IsFinished):
                UpdateCompactProgressBar();
                ApplyDialEffect();
                break;
            case nameof(MainViewModel.IsRunning):
            case nameof(MainViewModel.RunningEffect):
            case nameof(MainViewModel.FinishEffect):
            case nameof(MainViewModel.ActiveFinishEffect):
            case nameof(MainViewModel.PreviewEffect):
            case nameof(MainViewModel.IsFinishEffectActive):
                ApplyDialEffect();
                break;
        }
    }

    /// <summary>
    /// Normal ↔ Compact (docs/UI-CONTRACT.md → Compact Mode, обновлено 2026-09-27: Compact —
    /// скруглённый прямоугольник, не круг). MVP: мгновенное переключение без анимации.
    /// </summary>
    // Центр циферблата полного вида от верха содержимого: отступ 16 + радиус кольца 112.5 (225/2), см. XAML RingGroup.
    private const double NormalDialCenterY = 16 + 225 / 2.0;

    private (bool IsCompact, double Scale)? _appliedLayout;

    // Точная (дробная) точка привязки и позиция, которую мы сами выставили. Координаты окна целые, и пересчёт
    // привязки из округлённой позиции смещал окно на ~1px за каждое «туда-обратно» — при частых переключениях
    // оно бы ползло. Пока окно не двигали, берём запомненную точку.
    private Point? _lastAnchorOnScreen;
    private (double Left, double Top)? _lastSetPosition;

    /// <summary>
    /// Точка привязки внутри окна: полный вид — центр циферблата, компакт — центр прямоугольника. При смене
    /// режима/размера окно ставится так, чтобы эта точка осталась на том же месте экрана (докладка 2026-09-27:
    /// компакт появлялся со смещением от циферблата).
    /// </summary>
    private static Point AnchorInWindow(bool isCompact, double scale) => isCompact
        ? new Point(ChromeMargin + CompactContentWidth * scale / 2, ChromeMargin + CompactContentHeight * scale / 2)
        : new Point(ChromeMargin + NormalContentWidth * scale / 2, ChromeMargin + NormalDialCenterY * scale);

    private void ApplyModeLayout(bool isCompact)
    {
        double scale = isCompact ? _viewModel.CompactViewScale : _viewModel.FullViewScale;

        // Смена вида: у целевого вида есть своё сохранённое место (докладка 2026-09-28) — ставим туда. Нет (первое
        // переключение) — центрируем по точке привязки прежнего вида (циферблат ↔ центр компакта). Смена масштаба
        // в том же виде — тоже держим центр. Первая раскладка окна при старте — ничего не двигаем.
        Point? anchorOnScreen = null;
        Point? savedTarget = null;
        if (_appliedLayout is { } prev)
        {
            bool modeChanged = prev.IsCompact != isCompact;
            if (modeChanged)
            {
                _viewModel.SaveWindowPosition(prev.IsCompact, Left, Top);
                savedTarget = _viewModel.GetSavedWindowPosition(isCompact);
            }

            if (savedTarget is null)
            {
                bool notMovedSinceLastApply = _lastSetPosition is { } set
                                              && Math.Abs(set.Left - Left) < 0.5 && Math.Abs(set.Top - Top) < 0.5;
                if (notMovedSinceLastApply && _lastAnchorOnScreen is { } exact)
                {
                    anchorOnScreen = exact;
                }
                else
                {
                    var a = AnchorInWindow(prev.IsCompact, prev.Scale);
                    anchorOnScreen = new Point(Left + a.X, Top + a.Y);
                }
            }
        }

        ViewScale.ScaleX = ViewScale.ScaleY = scale;
        Width = (isCompact ? CompactContentWidth : NormalContentWidth) * scale + 2 * ChromeMargin;
        Height = (isCompact ? CompactContentHeight : NormalContentHeight) * scale + 2 * ChromeMargin;

        if (savedTarget is { } s && IsVisibleOnScreen(s.X, s.Y, Width, Height))
        {
            Left = s.X;
            Top = s.Y;
            _lastAnchorOnScreen = null;
        }
        else if (anchorOnScreen is { } target)
        {
            var a = AnchorInWindow(isCompact, scale);
            Left = target.X - a.X;
            Top = target.Y - a.Y;
            _lastAnchorOnScreen = target;
            _lastSetPosition = (Left, Top);
        }
        if (_appliedLayout is not null) _viewModel.SaveWindowPosition(isCompact, Left, Top);
        _appliedLayout = (isCompact, scale);

        NormalContent.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        CompactContent.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;

        SettingsButton.Margin = isCompact ? CompactSettingsMargin : NormalSettingsMargin;
        CloseButton.Margin = isCompact ? CompactCloseMargin : NormalCloseMargin;
        AutoCloseButton.Margin = isCompact ? CompactAutoCloseMargin : NormalAutoCloseMargin;
    }

    /// <summary>
    /// Окно хотя бы частично (≥40px) видно на каком-либо экране. Сохранённое место могло оказаться за краем —
    /// отключили второй монитор, сменили разрешение; тогда позицию не восстанавливаем.
    /// </summary>
    internal static bool IsVisibleOnScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(left, top, width, height));
        return !visible.IsEmpty && visible.Width >= 40 && visible.Height >= 40;
    }

    /// <summary>Сброс настроек: вид по умолчанию, окно по центру рабочей области основного экрана.</summary>
    private void OnSettingsReset()
    {
        _appliedLayout = null;
        _lastAnchorOnScreen = null;
        ApplyModeLayout(_viewModel.IsCompactMode);
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        UpdateCompactProgressBar();
        ApplyDialEffect();
    }

    private void UpdateCompactProgressBar()
    {
        if (!_viewModel.IsCompactMode) return;

        double trackWidth = 220 - 2 * 12; // Border.Width - 2*Grid.Margin (см. XAML)
        double fraction = Math.Clamp(_viewModel.ProgressFraction, 0.0, 1.0);
        CompactProgressBar.Width = trackWidth * fraction;
        CompactProgressBar.Background = _viewModel.IsFinished
            ? (Brush)FindResource("Brush.Finish")
            : (Brush)FindResource("Brush.Accent");
    }

    /// <summary>
    /// Визуальный эффект циферблата — либо во время отсчёта (RunningEffect), либо при завершении
    /// (ActiveFinishEffect, пока звонит будильник), либо просмотр из настроек (PreviewEffect — поверх состояния).
    /// Статусы Running/Finished взаимоисключающие, поэтому конфликта между наборами эффектов не бывает.
    /// Эффекты-заливки (Flash, ColorBreathe, эффекты завершения Pulse/Flash/ColorCycle) — на EffectOverlay;
    /// «Волны»/«Змейка»/«Радужная змейка» (2026-09-28) — бегущий пунктир по контурам (RunDashLoop).
    /// Не лезут во внутренности ProgressRingControl/SectorRingControl (docs/UI-CONTRACT.md).
    /// </summary>
    private void ApplyDialEffect()
    {
        _dialEffectStoryboard?.Stop(this);
        _dialEffectStoryboard = null;
        StopPulseScale();
        StopShapeEffects();
        EffectOverlay.Opacity = CompactEffectOverlay.Opacity = 0;

        object? effect = _viewModel.PreviewEffect
                         ?? (_viewModel.IsRunning && _viewModel.RunningEffect != RunningVisualEffect.None ? _viewModel.RunningEffect
                             : _viewModel.IsFinishEffectActive ? _viewModel.ActiveFinishEffect // у быстрого таймера — свой
                             : null);

        Storyboard? sb = null;
        bool coversDial = false; // эффект закрашивает циферблат — тёмные сегменты-подложки «88» прячем
        switch (effect)
        {
            case RunningVisualEffect.Waves or FinishVisualEffect.Waves:
                StartWaves(effect is FinishVisualEffect);
                break;
            case RunningVisualEffect.Snake or FinishVisualEffect.Snake:
                StartSnake(effect is FinishVisualEffect, rainbow: false);
                break;
            case RunningVisualEffect.RainbowSnake or FinishVisualEffect.RainbowSnake:
                StartSnake(effect is FinishVisualEffect, rainbow: true);
                break;
            case RunningVisualEffect running and not RunningVisualEffect.None:
                SetOverlayBrush((Brush)FindResource("Brush.Accent"));
                sb = BuildRunningEffectStoryboard(running);
                coversDial = running is RunningVisualEffect.Flash or RunningVisualEffect.ColorBreathe;
                break;
            case FinishVisualEffect finish and not FinishVisualEffect.None:
                sb = BuildFinishEffectStoryboard(finish);
                coversDial = true;
                break;
        }

        // Подложку прячем подменой ресурса окна (стили берут её через DynamicResource) — докладка 2026-09-28.
        if (coversDial) Resources["Brush.SegmentGhostLive"] = Brushes.Transparent;
        else Resources.Remove("Brush.SegmentGhostLive");

        if (sb is null) return;
        _dialEffectStoryboard = sb;
        sb.Begin(this, true);
    }

    // ---- Эффекты идут одновременно на кольце полного режима и на прямоугольнике компакта (докладка
    // 2026-09-27: в компакте их не было видно). Скрытый режим анимируется вхолостую — это дёшево и
    // избавляет от перезапуска эффекта при переключении режима. ----

    /// <summary>У каждой подложки своя кисть (цвет одинаковый) — чтобы анимацию цвета можно было запускать на каждой.</summary>
    private void SetOverlayBrush(Brush brush)
    {
        EffectOverlay.Fill = brush;
        CompactEffectOverlay.Background = brush.CloneCurrentValue();
    }

    private void AddOverlayOpacity(Storyboard sb, AnimationTimeline animation)
    {
        var compact = animation.Clone();
        Storyboard.SetTarget(animation, EffectOverlay);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        Storyboard.SetTarget(compact, CompactEffectOverlay);
        Storyboard.SetTargetProperty(compact, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(animation);
        sb.Children.Add(compact);
    }

    /// <summary>
    /// Пульсация масштабом. Компакт — втрое слабее: прямоугольник 220px в окне 236px при полном размахе
    /// вылез бы за край окна и обрезался. Анимация — прямо на ScaleTransform (BeginAnimation): Storyboard с целью-
    /// трансформацией (не элементом окна) молча не играл — «Пульсация» отсчёта не работала (найдено 2026-09-28).
    /// </summary>
    private void StartPulseScale(double to, TimeSpan halfPeriod)
    {
        double compactTo = 1 + (to - 1) / 3;
        foreach (var (target, max) in new[] { (RingGroupScale, to), (CompactScale, compactTo) })
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            target.BeginAnimation(property, new DoubleAnimation(1.0, max, halfPeriod)
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase()
            });
        }
    }

    private void StopPulseScale()
    {
        foreach (var target in new[] { RingGroupScale, CompactScale })
        {
            target.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            target.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            target.ScaleX = target.ScaleY = 1;
        }
    }

    private Storyboard BuildRunningEffectStoryboard(RunningVisualEffect effect)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        switch (effect)
        {
            case RunningVisualEffect.Pulse:
                StartPulseScale(1.05, TimeSpan.FromSeconds(0.9));
                break;

            case RunningVisualEffect.Flash:
                AddOverlayOpacity(sb, new DoubleAnimation(0.0, 0.5, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, EasingFunction = new SineEase() });
                break;

            case RunningVisualEffect.ColorBreathe:
                AddOverlayOpacity(sb, new DoubleAnimation(0.08, 0.32, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, EasingFunction = new SineEase() });
                break;
        }

        return sb;
    }

    /// <summary>
    /// ВАЖНО: те же эффекты (периоды и цвета) повторены в трее — Services/TrayService.GetFinishLook; менять вместе.
    /// Эффекты завершения — красно-оранжевая гамма (Brush.Finish), докладка 2026-09-27: "в основном
    /// эффект изменения цвета, цикличность, вспышки, красные эффекты". Pulse/Flash — те же приёмы,
    /// что и у running-эффектов, но быстрее и заметнее (сигнал "звонит будильник", а не фоновый
    /// индикатор); ColorCycle — «Радужная волна» (докладка 2026-09-27): яркий круг всех цветов из
    /// Models/RainbowPalette для привлечения внимания; анимирует ЦВЕТ собственной (не общей/замороженной) кисти.
    /// </summary>
    private Storyboard BuildFinishEffectStoryboard(FinishVisualEffect effect)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var finishColor = ((SolidColorBrush)FindResource("Brush.Finish")).Color;
        SetOverlayBrush(new SolidColorBrush(finishColor));

        switch (effect)
        {
            case FinishVisualEffect.Pulse:
                StartPulseScale(1.09, TimeSpan.FromSeconds(0.45));
                AddOverlayOpacity(sb, new DoubleAnimation(0.15, 0.55, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, EasingFunction = new SineEase() });
                break;

            case FinishVisualEffect.Flash:
                var flash = new DoubleAnimationUsingKeyFrames();
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.07))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.16))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
                AddOverlayOpacity(sb, flash);
                break;

            case FinishVisualEffect.ColorCycle:
                EffectOverlay.Opacity = CompactEffectOverlay.Opacity = 0.7; // ярко (привлечь внимание), белые цифры поверх читаются
                // Цвет анимируется прямо на кистях (BeginAnimation), а не через Storyboard: Storyboard с целью-кистью
                // (не элементом окна) молча не играл — заливка стояла одним цветом (найдено снимками 2026-09-27).
                // Кисти новые при каждом ApplyDialEffect, поэтому их анимации уходят вместе с ними — останавливать не нужно.
                var rainbow = BuildRainbowAnimation();
                ((SolidColorBrush)EffectOverlay.Fill).BeginAnimation(SolidColorBrush.ColorProperty, rainbow);
                ((SolidColorBrush)CompactEffectOverlay.Background).BeginAnimation(SolidColorBrush.ColorProperty, rainbow);
                break;
        }

        return sb;
    }

    /// <summary>Круг цветов Models/RainbowPalette; последний кадр = первый цвет — цикл без скачка.</summary>
    private static ColorAnimationUsingKeyFrames BuildRainbowAnimation(double speed = 1)
    {
        var rainbow = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        for (int i = 0; i <= RainbowPalette.Colors.Length; i++)
        {
            var (r, g, b) = RainbowPalette.Colors[i % RainbowPalette.Colors.Length];
            rainbow.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(r, g, b),
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * RainbowPalette.StepSeconds / speed))));
        }
        return rainbow;
    }

    // ---- «Встречные волны», «Змейка», «Радужная змейка» (докладка 2026-09-28) ----
    // Бегущий пунктир: StrokeDashArray с N штрихами на весь контур и анимация StrokeDashOffset на длину контура —
    // цикл без скачка. Размеры контуров заданы в XAML явно (скрытый вид имеет ActualWidth = 0).

    private IEnumerable<System.Windows.Shapes.Shape> EffectShapes =>
        new System.Windows.Shapes.Shape[] { WaveRingA, WaveRingB, FrameSnake, CompactWaveA, CompactWaveB, CompactSnake };

    private void StopShapeEffects()
    {
        foreach (var shape in EffectShapes)
        {
            shape.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
            shape.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Длина контура по средней линии штриха (штрих рисуется внутри границ фигуры).</summary>
    private static double ContourLength(System.Windows.Shapes.Shape shape)
    {
        double t = shape.StrokeThickness, w = shape.Width - t, h = shape.Height - t;
        if (shape is System.Windows.Shapes.Ellipse) return Math.PI * w;
        double r = Math.Clamp(((System.Windows.Shapes.Rectangle)shape).RadiusX - t / 2, 0, Math.Min(w, h) / 2);
        return 2 * (w + h) - 8 * r + 2 * Math.PI * r;
    }

    /// <summary>count штрихов длиной dashPx бегут по контуру; один круг за seconds; reverse — в обратную сторону.</summary>
    private static void RunDashLoop(System.Windows.Shapes.Shape shape, Brush stroke, int count, double dashPx, double seconds, bool reverse)
    {
        double t = shape.StrokeThickness;
        double length = ContourLength(shape) / t; // StrokeDashArray/Offset — в толщинах штриха
        double period = length / count, dash = Math.Min(dashPx / t, period * 0.85);
        var dashes = new DoubleCollection();
        for (int i = 0; i < count; i++) { dashes.Add(dash); dashes.Add(period - dash); }
        shape.StrokeDashArray = dashes;
        shape.Stroke = stroke;
        shape.Visibility = Visibility.Visible;
        shape.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, reverse ? -length : length, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>Две волны навстречу друг другу с разной скоростью: по кольцу секторов и по краю компакта.</summary>
    private void StartWaves(bool finish)
    {
        var brush = (Brush)FindResource(finish ? "Brush.Finish" : "Brush.Accent");
        double k = finish ? 0.6 : 1; // по окончании — быстрее, как сигнал
        RunDashLoop(WaveRingA, brush, 2, 70, 3.2 * k, reverse: false);
        RunDashLoop(WaveRingB, brush, 1, 95, 5.3 * k, reverse: true);
        RunDashLoop(CompactWaveA, brush, 2, 60, 2.8 * k, reverse: false);
        RunDashLoop(CompactWaveB, brush, 1, 85, 4.4 * k, reverse: true);
    }

    /// <summary>Узкая змейка по рамке окна; радужная — толще, переливается цветами палитры и светится.</summary>
    private void StartSnake(bool finish, bool rainbow)
    {
        Brush brush;
        if (rainbow)
        {
            var solid = new SolidColorBrush(Colors.White);
            solid.BeginAnimation(SolidColorBrush.ColorProperty, BuildRainbowAnimation(speed: 2));
            brush = solid;
        }
        else
        {
            brush = (Brush)FindResource(finish ? "Brush.Finish" : "Brush.Accent");
        }

        double k = finish ? 0.6 : 1;
        foreach (var (shape, dash, seconds) in new[] { (FrameSnake, rainbow ? 170.0 : 110.0, 3.6), (CompactSnake, rainbow ? 120.0 : 80.0, 2.6) })
        {
            shape.StrokeThickness = rainbow ? 3.5 : 2.5;
            shape.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = rainbow ? Colors.White : ((SolidColorBrush)brush).Color, BlurRadius = rainbow ? 12 : 8,
                ShadowDepth = 0, Opacity = rainbow ? 0.55 : 0.8
            };
            RunDashLoop(shape, brush, 1, dash, seconds * k, reverse: false);
        }
    }

    // ============ Drag / клик-по-центру (docs/UI-CONTRACT.md, обновлено 2026-09-27) ============
    // Ручной захват мыши вместо DragMove(): позволяет отличить клик без движения (переключает
    // Compact-режим, если клик пришёлся на центр) от перетаскивания (движение мышью).

    private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        bool onBackground = ReferenceEquals(source, RootGrid) || ReferenceEquals(source, NormalContent) || ReferenceEquals(source, CompactContent);
        bool onCenter = TreeHelper.IsDescendantOf(source, CenterDisplay) || TreeHelper.IsDescendantOf(source, CompactCenterDisplay);
        if (!onBackground && !onCenter) return; // клик на секторе/кнопке — не наше дело, событие продолжит идти к ним

        _mouseDownScreenPos = PointToScreen(e.GetPosition(this));
        _mouseDownWindowPos = new Point(Left, Top);
        _isDraggingWindow = false;
        _mouseDownOnCenter = onCenter;
        RootGrid.CaptureMouse();
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (_mouseDownScreenPos is null || e.LeftButton != MouseButtonState.Pressed) return;

        var current = PointToScreen(e.GetPosition(this));
        var delta = current - _mouseDownScreenPos.Value;
        if (!_isDraggingWindow && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
            _isDraggingWindow = true;

        if (_isDraggingWindow)
        {
            Left = _mouseDownWindowPos!.Value.X + delta.X;
            Top = _mouseDownWindowPos!.Value.Y + delta.Y;
        }
    }

    private void OnRootMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mouseDownScreenPos is null) return;
        RootGrid.ReleaseMouseCapture();

        if (!_isDraggingWindow && _mouseDownOnCenter)
            _viewModel.ToggleCompactModeCommand.Execute(null);
        else if (_isDraggingWindow)
            _viewModel.SaveWindowPosition(_viewModel.IsCompactMode, Left, Top); // место вида запоминается сразу после перетаскивания

        _mouseDownScreenPos = null;
        _isDraggingWindow = false;
        _mouseDownOnCenter = false;
    }

    /// <summary>
    /// Колесо мыши ±1 мин (docs/UI-CONTRACT.md). Упрощение MVP: реагирует в любой точке окна.
    /// </summary>
    private void OnRootMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _viewModel.AdjustTimeCommand.Execute(e.Delta > 0 ? 1 : -1);
    }

    // ============ Закрытие / сворачивание (docs/UI-CONTRACT.md → Settings → Поведение при закрытии) ============

    private void HandleCloseRequest()
    {
        if (_viewModel.CloseBehavior == CloseBehavior.Exit)
        {
            _viewModel.ExitCommand.Execute(null);
        }
        else
        {
            _viewModel.SaveWindowPosition(_viewModel.IsCompactMode, Left, Top);
            Hide();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true; // системное закрытие (Alt+F4 и т.п.) тоже уважает CloseBehavior
        HandleCloseRequest();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => HandleCloseRequest();

    /// <summary>ПКМ по кнопке автозакрытия — режим «свернуть в трей / закрыть» (ЛКМ — компактный вид, команда кнопки).</summary>
    private void OnAutoCloseRightClick(object sender, MouseButtonEventArgs e)
    {
        _hints.Cancel();
        _viewModel.ToggleAutoCloseCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Подсказка кнопки автозакрытия — с легендой из тех же значков (докладка 2026-09-28): что включено сейчас
    /// и что делают левый/правый щелчки.
    /// </summary>
    private FrameworkElement BuildAutoCloseHint()
    {
        string trayOrExit = _viewModel.CloseBehavior == CloseBehavior.Exit ? "закрыть программу" : "свернуть в трей";
        string state = _viewModel.AutoCompactAfterFinish ? "перейти в компактный вид"
            : _viewModel.AutoCloseAfterFinish ? trayOrExit
            : "ничего не делать (выключено)";

        var textStyle = (Style)FindResource("Style.HintText");
        var panel = new StackPanel { MaxWidth = 250 };
        panel.Children.Add(new TextBlock { Style = textStyle, Text = $"После окончания таймера: {state}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(LegendRow("Brush.Accent", "ЛКМ — перейти в компактный вид (на его место)"));
        panel.Children.Add(LegendRow("Brush.AutoClose", $"ПКМ — {trayOrExit}"));
        panel.Children.Add(LegendRow("Brush.TextSecondary", "Выключено"));
        panel.Children.Add(new TextBlock
        {
            Style = textStyle, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("Brush.TextSecondary"),
            Text = "Срабатывает, когда звонок отыграл. Повторный щелчок той же кнопкой — выключить."
        });
        return panel;

        FrameworkElement LegendRow(string brushKey, string text)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("Geometry.Power"), Fill = (Brush)FindResource(brushKey),
                Width = 11, Height = 11, Stretch = Stretch.Uniform, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top
            };
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Style = textStyle, Text = text });
            return row;
        }
    }

    // ============ Клавиши (docs/UI-CONTRACT.md → Клавиши, 2026-09-28) ============

    private void ApplyGlobalHotkeys() => _viewModel.SetFailedHotkeys(_globalHotkeys.Apply(_viewModel.GetGlobalHotkeys()));

    /// <summary>Идёт ввод сочетания в настройках — глобальные клавиши молчат, иначе сработали бы вместо записи.</summary>
    private void OnHotkeyCaptureChanged(object? sender, bool capturing) => _globalHotkeys.Suspend(capturing);

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat) return; // зажатый пробел не должен дёргать старт/паузу
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_viewModel.HandleWindowKey(HotkeyBinding.FromKey(key, Keyboard.Modifiers))) e.Handled = true;
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        var button = e.ChangedButton switch
        {
            MouseButton.Middle => HotkeyMouseButton.Middle,
            MouseButton.XButton1 => HotkeyMouseButton.XButton1,
            MouseButton.XButton2 => HotkeyMouseButton.XButton2,
            _ => HotkeyMouseButton.None
        };
        if (button != HotkeyMouseButton.None
            && _viewModel.HandleWindowKey(new HotkeyBinding { MouseButton = button, Modifiers = Keyboard.Modifiers }))
            e.Handled = true;
    }

    /// <summary>Глобальная «Показать / скрыть окно»: видно — убрать в трей, нет — показать.</summary>
    private void ToggleWindow()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _viewModel.SaveWindowPosition(_viewModel.IsCompactMode, Left, Top);
            Hide();
        }
        else
        {
            _viewModel.RequestShowWindow(); // как из трея: App показывает и активирует окно
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        _hints = new HintController(RootGrid, target => _viewModel.GetHint(HintService.GetKey(target)!, target.Tag));
        Deactivated += (_, _) => _hints.Cancel();

        // Автозакрытие после таймера — ровно как нажатие крестика (свернуть в трей или выйти по настройке).
        _viewModel.AutoCloseRequested += (_, _) => HandleCloseRequest();
        // Скрытое окно + законченный таймер → иконка трея мигает красным (MainViewModel.UpdateTray).
        IsVisibleChanged += (_, _) => UpdateHiddenState();
        StateChanged += (_, _) => UpdateHiddenState(); // свёрнуто на панель задач (Win+D) — тоже «не видно»
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
            case nameof(MainViewModel.IsFinished):
                UpdateCompactProgressBar();
                ApplyDialEffect();
                break;
            case nameof(MainViewModel.IsRunning):
            case nameof(MainViewModel.RunningEffect):
            case nameof(MainViewModel.FinishEffect):
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
    /// (FinishEffect, пока звонит будильник); статусы Running/Finished взаимоисключающие, поэтому
    /// конфликта между двумя наборами эффектов на одних и тех же элементах не бывает. По умолчанию
    /// оба отключены. Все реализации намеренно просты (Opacity/Scale/Color на EffectOverlay) — не
    /// лезут во внутренности ProgressRingControl/SectorRingControl (docs/UI-CONTRACT.md).
    /// </summary>
    private void ApplyDialEffect()
    {
        _dialEffectStoryboard?.Stop(this);
        _dialEffectStoryboard = null;
        RingGroupScale.ScaleX = RingGroupScale.ScaleY = 1;
        CompactScale.ScaleX = CompactScale.ScaleY = 1;
        EffectOverlay.Opacity = CompactEffectOverlay.Opacity = 0;

        Storyboard? sb = null;
        if (_viewModel.IsRunning && _viewModel.RunningEffect != RunningVisualEffect.None)
        {
            SetOverlayBrush((Brush)FindResource("Brush.Accent"));
            sb = BuildRunningEffectStoryboard(_viewModel.RunningEffect);
        }
        else if (_viewModel.IsFinishEffectActive) // Finished + эффект выбран + длительность не истекла
        {
            sb = BuildFinishEffectStoryboard(_viewModel.FinishEffect);
        }

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
    /// вылез бы за край окна и обрезался.
    /// </summary>
    private void AddPulseScale(Storyboard sb, double to, TimeSpan halfPeriod)
    {
        double compactTo = 1 + (to - 1) / 3;
        foreach (var (target, max) in new[] { (RingGroupScale, to), (CompactScale, compactTo) })
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var scale = new DoubleAnimation(1.0, max, halfPeriod) { AutoReverse = true, EasingFunction = new SineEase() };
            Storyboard.SetTarget(scale, target);
            Storyboard.SetTargetProperty(scale, new PropertyPath(property));
            sb.Children.Add(scale);
        }
    }

    private Storyboard BuildRunningEffectStoryboard(RunningVisualEffect effect)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        switch (effect)
        {
            case RunningVisualEffect.Pulse:
                AddPulseScale(sb, 1.05, TimeSpan.FromSeconds(0.9));
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
                AddPulseScale(sb, 1.09, TimeSpan.FromSeconds(0.45));
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
                var rainbow = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                for (int i = 0; i <= RainbowPalette.Colors.Length; i++) // последний кадр = первый цвет: цикл без скачка
                {
                    var (r, g, b) = RainbowPalette.Colors[i % RainbowPalette.Colors.Length];
                    rainbow.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(r, g, b),
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * RainbowPalette.StepSeconds))));
                }
                ((SolidColorBrush)EffectOverlay.Fill).BeginAnimation(SolidColorBrush.ColorProperty, rainbow);
                ((SolidColorBrush)CompactEffectOverlay.Background).BeginAnimation(SolidColorBrush.ColorProperty, rainbow);
                break;
        }

        return sb;
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
}

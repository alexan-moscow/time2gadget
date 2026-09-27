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
    private const double NormalWindowWidth = 273, NormalWindowHeight = 341;   // 16(top)+225(кольцо)+16(зазор)+52(кнопки)+16(bottom) + 2*8 Chrome margin — везде одинаковый отступ 16 (докладка 2026-09-27)
    private const double CompactWindowWidth = 236, CompactWindowHeight = 156; // содержимое 220x140 + 2*8 margin

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
        if (_viewModel.InitialWindowLeft is { } left && _viewModel.InitialWindowTop is { } top)
        {
            Left = left;
            Top = top;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        ApplyModeLayout(_viewModel.IsCompactMode);
        UpdateCompactProgressBar();
        ApplyDialEffect();
    }

    private void UpdateHiddenState() =>
        _viewModel.IsWindowHidden = !IsVisible || WindowState == WindowState.Minimized;

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new Views.SettingsWindow(_viewModel) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsCompactMode):
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
    private void ApplyModeLayout(bool isCompact)
    {
        Width = isCompact ? CompactWindowWidth : NormalWindowWidth;
        Height = isCompact ? CompactWindowHeight : NormalWindowHeight;

        NormalContent.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        CompactContent.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCompactProgressBar()
    {
        if (!_viewModel.IsCompactMode) return;

        double trackWidth = 220 - 2 * 14; // Border.Width - 2*Grid.Margin (см. XAML)
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
        RingGroupScale.ScaleX = 1;
        RingGroupScale.ScaleY = 1;
        EffectOverlay.Opacity = 0;

        Storyboard? sb = null;
        if (_viewModel.IsRunning && _viewModel.RunningEffect != RunningVisualEffect.None)
        {
            EffectOverlay.Fill = (Brush)FindResource("Brush.Accent");
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

    private Storyboard BuildRunningEffectStoryboard(RunningVisualEffect effect)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        switch (effect)
        {
            case RunningVisualEffect.Pulse:
                var scaleX = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, EasingFunction = new SineEase() };
                var scaleY = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, EasingFunction = new SineEase() };
                Storyboard.SetTarget(scaleX, RingGroupScale);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
                Storyboard.SetTarget(scaleY, RingGroupScale);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                break;

            case RunningVisualEffect.Flash:
                var flash = new DoubleAnimation(0.0, 0.5, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, EasingFunction = new SineEase() };
                Storyboard.SetTarget(flash, EffectOverlay);
                Storyboard.SetTargetProperty(flash, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(flash);
                break;

            case RunningVisualEffect.ColorBreathe:
                var breathe = new DoubleAnimation(0.08, 0.32, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, EasingFunction = new SineEase() };
                Storyboard.SetTarget(breathe, EffectOverlay);
                Storyboard.SetTargetProperty(breathe, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(breathe);
                break;
        }

        return sb;
    }

    /// <summary>
    /// ВАЖНО: те же эффекты (периоды и цвета) повторены в трее — Services/TrayService.GetFinishLook; менять вместе.
    /// Эффекты завершения — красно-оранжевая гамма (Brush.Finish), докладка 2026-09-27: "в основном
    /// эффект изменения цвета, цикличность, вспышки, красные эффекты". Pulse/Flash — те же приёмы,
    /// что и у running-эффектов, но быстрее и заметнее (сигнал "звонит будильник", а не фоновый
    /// индикатор); ColorCycle — единственный эффект, реально анимирующий ЦВЕТ (не только Opacity/Scale) —
    /// для этого EffectOverlay.Fill временно подменяется на собственный (не общий/замороженный) SolidColorBrush.
    /// </summary>
    private Storyboard BuildFinishEffectStoryboard(FinishVisualEffect effect)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var finishColor = ((SolidColorBrush)FindResource("Brush.Finish")).Color;
        EffectOverlay.Fill = new SolidColorBrush(finishColor);

        switch (effect)
        {
            case FinishVisualEffect.Pulse:
                var scaleX = new DoubleAnimation(1.0, 1.09, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, EasingFunction = new SineEase() };
                var scaleY = new DoubleAnimation(1.0, 1.09, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, EasingFunction = new SineEase() };
                var pulseOpacity = new DoubleAnimation(0.15, 0.55, TimeSpan.FromSeconds(0.45)) { AutoReverse = true, EasingFunction = new SineEase() };
                Storyboard.SetTarget(scaleX, RingGroupScale);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
                Storyboard.SetTarget(scaleY, RingGroupScale);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
                Storyboard.SetTarget(pulseOpacity, EffectOverlay);
                Storyboard.SetTargetProperty(pulseOpacity, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Children.Add(pulseOpacity);
                break;

            case FinishVisualEffect.Flash:
                var flash = new DoubleAnimationUsingKeyFrames();
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.07))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.16))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
                Storyboard.SetTarget(flash, EffectOverlay);
                Storyboard.SetTargetProperty(flash, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(flash);
                break;

            case FinishVisualEffect.ColorCycle:
                EffectOverlay.Opacity = 0.4;
                var amber = Color.FromRgb(0xFF, 0xD5, 0x4A);
                var colorCycle = new ColorAnimationUsingKeyFrames();
                colorCycle.KeyFrames.Add(new LinearColorKeyFrame(finishColor, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                colorCycle.KeyFrames.Add(new LinearColorKeyFrame(amber, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0))));
                colorCycle.KeyFrames.Add(new LinearColorKeyFrame(finishColor, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
                Storyboard.SetTarget(colorCycle, EffectOverlay.Fill);
                Storyboard.SetTargetProperty(colorCycle, new PropertyPath(SolidColorBrush.ColorProperty));
                sb.Children.Add(colorCycle);
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
            _viewModel.UpdateWindowPosition(Left, Top);
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

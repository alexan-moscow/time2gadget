using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Time2Gadget.Controls;

/// <summary>
/// Тонкое кольцо прогресса отсчёта (docs/UI-CONTRACT.md → ProgressRingControl).
/// Единственный источник визуализации хода отсчёта — не перекрашивает сектора.
/// </summary>
public partial class ProgressRingControl : UserControl
{
    public static readonly DependencyProperty ProgressFractionProperty = DependencyProperty.Register(
        nameof(ProgressFraction), typeof(double), typeof(ProgressRingControl),
        new PropertyMetadata(0.0, OnVisualStateChanged));

    public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
        nameof(IsFinished), typeof(bool), typeof(ProgressRingControl),
        new PropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty OuterRadiusProperty = DependencyProperty.Register(
        nameof(OuterRadius), typeof(double), typeof(ProgressRingControl),
        new PropertyMetadata(100.0, OnLayoutChanged));

    public static readonly DependencyProperty InnerRadiusProperty = DependencyProperty.Register(
        nameof(InnerRadius), typeof(double), typeof(ProgressRingControl),
        new PropertyMetadata(94.0, OnLayoutChanged));

    public double ProgressFraction
    {
        get => (double)GetValue(ProgressFractionProperty);
        set => SetValue(ProgressFractionProperty, value);
    }

    public bool IsFinished
    {
        get => (bool)GetValue(IsFinishedProperty);
        set => SetValue(IsFinishedProperty, value);
    }

    public double OuterRadius
    {
        get => (double)GetValue(OuterRadiusProperty);
        set => SetValue(OuterRadiusProperty, value);
    }

    public double InnerRadius
    {
        get => (double)GetValue(InnerRadiusProperty);
        set => SetValue(InnerRadiusProperty, value);
    }

    public ProgressRingControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { BuildTrack(); UpdateProgress(); };
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ProgressRingControl)d;
        control.BuildTrack();
        control.UpdateProgress();
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressRingControl)d).UpdateProgress();

    private Point Center => new(OuterRadius + 2, OuterRadius + 2);

    private void BuildTrack()
    {
        if (!IsLoaded) return;

        double size = OuterRadius * 2 + 4;
        RootCanvas.Width = size;
        RootCanvas.Height = size;

        var trackBrush = TryFindResource("Brush.BorderSubtle") as Brush ?? Brushes.Gray;
        TrackPath.Data = GeometryHelper.CreateDonutSegment(Center, InnerRadius, OuterRadius, 0, 359.9);
        TrackPath.Fill = trackBrush;
    }

    private void UpdateProgress()
    {
        if (!IsLoaded) return;

        var accentBrush = TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue;
        var finishBrush = TryFindResource("Brush.Finish") as Brush ?? Brushes.OrangeRed;

        double fraction = Math.Clamp(ProgressFraction, 0.0, 1.0);
        double sweep = fraction * 360.0;

        ProgressPath.Data = GeometryHelper.CreateDonutSegment(Center, InnerRadius, OuterRadius, 0, sweep);
        ProgressPath.Fill = IsFinished ? finishBrush : accentBrush;
    }
}

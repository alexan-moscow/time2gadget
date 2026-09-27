using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TimerGadget.Models;

namespace TimerGadget.Controls;

/// <summary>
/// Кольцо из N кликабельных секторов-пресетов (docs/UI-CONTRACT.md → SectorRingControl).
/// Чисто визуальный компонент: построение геометрии + делегирование клика в SelectCommand.
/// Никакой бизнес-логики (выбор/старт) здесь нет — она в MainViewModel.SelectPresetCommand.
/// </summary>
public partial class SectorRingControl : UserControl
{
    public static readonly DependencyProperty PresetsProperty = DependencyProperty.Register(
        nameof(Presets), typeof(System.Collections.IEnumerable), typeof(SectorRingControl),
        new PropertyMetadata(null, OnLayoutAffectingChanged));

    public static readonly DependencyProperty SelectedPresetProperty = DependencyProperty.Register(
        nameof(SelectedPreset), typeof(TimerPreset), typeof(SectorRingControl),
        new PropertyMetadata(null, OnSelectionChanged));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(SectorRingControl), new PropertyMetadata(null));

    public static readonly DependencyProperty OuterRadiusProperty = DependencyProperty.Register(
        nameof(OuterRadius), typeof(double), typeof(SectorRingControl),
        new PropertyMetadata(148.0, OnLayoutAffectingChanged));

    public static readonly DependencyProperty InnerRadiusProperty = DependencyProperty.Register(
        nameof(InnerRadius), typeof(double), typeof(SectorRingControl),
        new PropertyMetadata(112.0, OnLayoutAffectingChanged));

    public System.Collections.IEnumerable? Presets
    {
        get => (System.Collections.IEnumerable?)GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public TimerPreset? SelectedPreset
    {
        get => (TimerPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
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

    private const double GapDegrees = 2.0; // docs/DESIGN-SYSTEM.md → Геометрия

    private readonly List<(TimerPreset Preset, Path Path)> _sectorPaths = new();

    public SectorRingControl()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildSectors();
    }

    private static void OnLayoutAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectorRingControl)d).BuildSectors();

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectorRingControl)d).UpdateSelectionVisuals();

    private void BuildSectors()
    {
        if (!IsLoaded) return;

        RootCanvas.Children.Clear();
        _sectorPaths.Clear();

        var list = Presets?.Cast<TimerPreset>().ToList();
        if (list is null || list.Count == 0) return;

        double size = OuterRadius * 2 + 4;
        RootCanvas.Width = size;
        RootCanvas.Height = size;
        var center = new Point(OuterRadius + 2, OuterRadius + 2);

        double sweep = 360.0 / list.Count;

        var sectorBrush = TryFindResource("Brush.SurfaceSector") as Brush ?? Brushes.DarkSlateGray;
        var hoverBrush = TryFindResource("Brush.SurfaceSectorHover") as Brush ?? Brushes.SlateGray;
        var accentBrush = TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue;
        var borderBrush = TryFindResource("Brush.BorderSubtle") as Brush ?? Brushes.Gray;
        var valueStyle = TryFindResource("Style.SectorValue") as Style;

        for (int i = 0; i < list.Count; i++)
        {
            var preset = list[i];
            double start = i * sweep + GapDegrees / 2;
            double segSweep = sweep - GapDegrees;

            var geometry = GeometryHelper.CreateDonutSegment(center, InnerRadius, OuterRadius, start, segSweep);
            var isSelected = preset.Equals(SelectedPreset);

            var path = new Path
            {
                Data = geometry,
                Fill = isSelected ? accentBrush : sectorBrush,
                Stroke = borderBrush,
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
                Tag = preset
            };
            path.MouseEnter += (_, _) => { if (!preset.Equals(SelectedPreset)) path.Fill = hoverBrush; };
            path.MouseLeave += (_, _) => { if (!preset.Equals(SelectedPreset)) path.Fill = sectorBrush; };
            path.MouseLeftButtonUp += (_, _) => SelectCommand?.Execute(preset);

            RootCanvas.Children.Add(path);
            _sectorPaths.Add((preset, path));

            double midAngleRad = GeometryHelper.DegToRad(start + segSweep / 2);
            double labelRadius = (InnerRadius + OuterRadius) / 2;
            var labelCenter = GeometryHelper.PointOnCircle(center, labelRadius, midAngleRad);

            // Только число, без подписи "МИН" (докладка 2026-09-27 — убрали совсем, так проще и
            // не тесно). Число целиком в Viewbox по всей площади сектора (и ширина, и высота) —
            // гарантированно не вылезает за пределы кольца при любых радиусах, без ручного подбора px.
            double segSweepRad = GeometryHelper.DegToRad(segSweep);
            double availableWidth = Math.Max(2 * InnerRadius * Math.Sin(segSweepRad / 2) - 4, 10);
            double availableHeight = Math.Max(OuterRadius - InnerRadius - 4, 10);

            var valueBlock = new TextBlock { Text = preset.ValueLabel, Style = valueStyle, HorizontalAlignment = HorizontalAlignment.Center };
            var valueViewbox = new Viewbox
            {
                Width = availableWidth,
                Height = availableHeight,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                Child = valueBlock
            };
            Canvas.SetLeft(valueViewbox, labelCenter.X - availableWidth / 2);
            Canvas.SetTop(valueViewbox, labelCenter.Y - availableHeight / 2);
            RootCanvas.Children.Add(valueViewbox);
        }
    }

    private void UpdateSelectionVisuals()
    {
        var sectorBrush = TryFindResource("Brush.SurfaceSector") as Brush ?? Brushes.DarkSlateGray;
        var accentBrush = TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue;

        foreach (var (preset, path) in _sectorPaths)
            path.Fill = preset.Equals(SelectedPreset) ? accentBrush : sectorBrush;
    }
}

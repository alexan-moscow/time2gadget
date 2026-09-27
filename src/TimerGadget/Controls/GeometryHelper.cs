using System.Windows;
using System.Windows.Media;

namespace TimerGadget.Controls;

/// <summary>
/// Общая геометрия donut-сегментов, переиспользуется SectorRingControl и ProgressRingControl
/// (docs/COMPONENTS.md — формула арки не дублируется в двух местах).
/// Угол 0° = "12 часов" (верх), направление — по часовой стрелке.
/// </summary>
internal static class GeometryHelper
{
    /// <summary>
    /// Сегмент кольца (annular sector) между innerRadius и outerRadius, от startAngleDeg
    /// на sweepAngleDeg градусов по часовой стрелке. sweepAngleDeg &gt;= 360 обрезается до 359.9,
    /// т.к. WPF ArcSegment не может корректно нарисовать полный круг одним сегментом.
    /// </summary>
    public static Geometry? CreateDonutSegment(Point center, double innerRadius, double outerRadius, double startAngleDeg, double sweepAngleDeg)
    {
        if (sweepAngleDeg <= 0.01 || outerRadius <= innerRadius) return null;
        if (sweepAngleDeg >= 360.0) sweepAngleDeg = 359.9;

        double startRad = DegToRad(startAngleDeg);
        double endRad = DegToRad(startAngleDeg + sweepAngleDeg);

        var outerStart = PointOnCircle(center, outerRadius, startRad);
        var outerEnd = PointOnCircle(center, outerRadius, endRad);
        var innerEnd = PointOnCircle(center, innerRadius, endRad);
        var innerStart = PointOnCircle(center, innerRadius, startRad);

        bool isLargeArc = sweepAngleDeg > 180.0;

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new LineSegment(outerStart, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    public static Point PointOnCircle(Point center, double radius, double angleRad) =>
        new(center.X + radius * Math.Sin(angleRad), center.Y - radius * Math.Cos(angleRad));

    public static double DegToRad(double deg) => deg * Math.PI / 180.0;
}

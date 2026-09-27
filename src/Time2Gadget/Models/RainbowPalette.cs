namespace Time2Gadget.Models;

/// <summary>
/// Эффект завершения «Радужная волна» (FinishVisualEffect.ColorCycle, докладка 2026-09-27: яркие циклы
/// всех цветов для привлечения внимания). Один источник для окна (WPF-анимация) и трея (GDI+), чтобы
/// цвета и темп не разъезжались.
/// </summary>
public static class RainbowPalette
{
    /// <summary>Насыщенные цвета по кругу; первый повторяется в конце анимации — цикл замкнут.</summary>
    public static readonly (byte R, byte G, byte B)[] Colors =
    {
        (0xFF, 0x2D, 0x2D), // красный
        (0xFF, 0x8A, 0x00), // оранжевый
        (0xFF, 0xE6, 0x00), // жёлтый
        (0x2D, 0xE0, 0x4E), // зелёный
        (0x00, 0xD5, 0xFF), // голубой
        (0x3D, 0x5A, 0xFF), // синий
        (0xC0, 0x2D, 0xFF), // фиолетовый
        (0xFF, 0x2D, 0xB4), // малиновый
    };

    /// <summary>Время перехода между соседними цветами; полный круг = Colors.Length * StepSeconds.</summary>
    public const double StepSeconds = 0.3;

    public static double CycleSeconds => Colors.Length * StepSeconds;

    /// <summary>Непрерывный цвет в момент t (секунды) — линейный переход между соседними цветами.</summary>
    public static (byte R, byte G, byte B) At(double t)
    {
        double pos = (t % CycleSeconds) / StepSeconds;
        int i = (int)pos % Colors.Length;
        var a = Colors[i];
        var b = Colors[(i + 1) % Colors.Length];
        double k = pos - Math.Floor(pos);
        return ((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
    }
}

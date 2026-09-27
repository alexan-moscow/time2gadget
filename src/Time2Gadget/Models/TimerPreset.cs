namespace Time2Gadget.Models;

/// <summary>
/// Один из 8 пресетов кольца выбора времени (docs/BRIEF.md §4).
/// </summary>
public sealed class TimerPreset
{
    public int Minutes { get; }
    public string ValueLabel => Minutes.ToString();
    public string UnitLabel => "МИН";

    public TimerPreset(int minutes) => Minutes = minutes;

    public static readonly IReadOnlyList<TimerPreset> All = new[]
    {
        new TimerPreset(5),
        new TimerPreset(10),
        new TimerPreset(15),
        new TimerPreset(20),
        new TimerPreset(30),
        new TimerPreset(45),
        new TimerPreset(60),
        new TimerPreset(90),
    };

    public override bool Equals(object? obj) => obj is TimerPreset p && p.Minutes == Minutes;
    public override int GetHashCode() => Minutes.GetHashCode();
}

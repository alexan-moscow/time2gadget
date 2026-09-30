using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <summary>
/// Расписание «Динамичной заставки / слайдшоу» (докладка 2026-09-30). Все варианты сводятся к одному: смены происходят в моменты
/// «опорное время + k × период» (местное время):
/// раз в час от времени старта — опора в это время, период 1 ч (важны минуты и секунды); каждый реальный час — опора 00:00, 1 ч;
/// раз в сутки в указанное время — опора в это время, 24 ч; в начале суток — 00:00, 24 ч;
/// свой интервал — опора в момент запуска («старт сейчас») или в указанное время («старт в»), период — интервал.
/// Шаг 1 — с момента запуска (<see cref="SlideshowSettings.Epoch"/>), дальше +1 на каждой смене, по кругу.
/// </summary>
public static class SlideshowSchedule
{
    /// <summary>
    /// Самый короткий интервал: подложкой — 1 с; средствами Windows — 2 с: чаще Windows не успевает — у неё своя плавная смена
    /// фона, и при смене раз в секунду она пропускает картинки (проверено 2026-10-01: 1 с — видны 2 цвета из 3; 2 с — все по кругу).
    /// </summary>
    public static TimeSpan MinInterval(SlideshowSettings s) => s.UseUnderlay ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2);

    public static TimeSpan Period(SlideshowSettings s) => s.Kind switch
    {
        SlideshowKind.Hourly => TimeSpan.FromHours(1),
        SlideshowKind.Daily => TimeSpan.FromDays(1),
        _ => s.Interval < MinInterval(s) ? MinInterval(s) : s.Interval,
    };

    private static DateTime Anchor(SlideshowSettings s) => s.Kind switch
    {
        SlideshowKind.Hourly => s.Epoch.Date + (s.HourlyFromTime ? s.HourlyStart : TimeSpan.Zero),
        SlideshowKind.Daily => s.Epoch.Date + (s.DailyAtTime ? s.DailyTime : TimeSpan.Zero),
        _ => s.IntervalStartAt ? s.Epoch.Date + s.IntervalStart : s.Epoch,
    };

    /// <summary>Номер смены, к которой относится момент <paramref name="t"/> (последняя смена не позже t).</summary>
    private static long ChangeIndex(SlideshowSettings s, DateTime t) =>
        (long)Math.Floor((t - Anchor(s)).Ticks / (double)Period(s).Ticks);

    /// <summary>Сколько смен прошло с запуска (0 — ещё шаг 1).</summary>
    public static long StepsSinceStart(SlideshowSettings s, DateTime now) =>
        Math.Max(0, ChangeIndex(s, now) - ChangeIndex(s, s.Epoch));

    /// <summary>Индекс шага (0-based) при <paramref name="count"/> шагах в круге.</summary>
    public static int CurrentStep(SlideshowSettings s, DateTime now, int count) =>
        count <= 0 ? 0 : (int)(StepsSinceStart(s, now) % count);

    /// <summary>Следующая смена после <paramref name="now"/>.</summary>
    public static DateTime NextChange(SlideshowSettings s, DateTime now) =>
        Anchor(s) + TimeSpan.FromTicks(Period(s).Ticks * (ChangeIndex(s, now) + 1));
}

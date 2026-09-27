using System.Globalization;
using System.Windows.Data;

namespace Time2Gadget.Converters;

/// <summary>Форматирование TimeSpan → "MM:SS" (docs/COMPONENTS.md). Часы сверх 99 минут — "H:MM:SS".</summary>
public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Format(value as TimeSpan? ?? TimeSpan.Zero);

    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{(int)span.TotalMinutes:D2}:{span.Seconds:D2}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;

namespace Time2Gadget.Converters;

/// <summary>
/// Текст для сегментного циферблата (шрифт DSEG7, docs/DESIGN-SYSTEM.md → Циферблат).
/// values[0] — оставшееся время (TimeSpan), values[1] — горит ли двоеточие (bool, мигание при отсчёте).
/// ConverterParameter="Ghost" — «подложка» из неактивных сегментов: каждая цифра заменена на 8,
/// поэтому число символов всегда совпадает с горящим текстом (и для "MM:SS", и для "H:MM:SS").
/// </summary>
public sealed class SegmentTimeConverter : IMultiValueConverter
{
    // В DSEG символ '!' — пустое место шириной ровно с ':' (погасшее двоеточие без сдвига цифр).
    private const char BlankColon = '!';

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var span = values.Length > 0 && values[0] is TimeSpan t ? t : TimeSpan.Zero;
        var text = TimeSpanToStringConverter.Format(span);

        if (parameter as string == "Ghost")
            return new string(text.Select(c => char.IsDigit(c) ? '8' : c).ToArray());

        bool colonLit = values.Length < 2 || values[1] is not false;
        return colonLit ? text : text.Replace(':', BlankColon);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

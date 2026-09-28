using System.Globalization;
using System.Windows.Data;

namespace Time2Gadget.Converters;

/// <summary>bool → текст; ConverterParameter — "ТекстЕслиTrue|ТекстЕслиFalse" (подсказки, меняющиеся по состоянию).</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texts = (parameter as string ?? string.Empty).Split('|');
        return texts.Length == 2 ? texts[value is true ? 0 : 1] : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Time2Gadget.Models;

namespace Time2Gadget.Converters;

/// <summary>
/// Кнопка ▶ в шаблоне звонка видна только в строке раскрытого списка (values[0] — ComboBoxItem-предок;
/// в закрытом поле его нет) и не у пункта «Свой файл…» (values[1] — Id звонка).
/// </summary>
public sealed class RingtonePreviewVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is ComboBoxItem && values[1] as string != RingtoneCatalog.CustomId
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

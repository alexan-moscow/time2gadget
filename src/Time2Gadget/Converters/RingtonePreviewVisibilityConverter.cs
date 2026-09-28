using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Time2Gadget.Models;

namespace Time2Gadget.Converters;

/// <summary>
/// Кнопка ▶ (и длительность) в шаблоне звонка видны только в строке раскрытого списка (values[0] — ComboBoxItem-предок;
/// в закрытом поле его нет) и когда есть что слушать (values[1] — RingtoneOption.CanPreview; у «Свой файл…» — если файл выбран).
/// </summary>
public sealed class RingtonePreviewVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is ComboBoxItem && values[1] is true
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

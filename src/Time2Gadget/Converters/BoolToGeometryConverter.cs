using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Time2Gadget.Converters;

/// <summary>
/// bool → векторная иконка из Theme.xaml. ConverterParameter — "КлючЕслиTrue|КлючЕслиFalse",
/// например "Geometry.VolumeOff|Geometry.VolumeUp" для Mute или "Geometry.Pause|Geometry.Play" для Start/Pause.
/// </summary>
public sealed class BoolToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var keys = (parameter as string ?? string.Empty).Split('|');
        if (keys.Length != 2) return null;
        return Application.Current.TryFindResource(value is true ? keys[0] : keys[1]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;

namespace TimerGadget.Converters;

/// <summary>bool IsMuted → иконка динамика (docs/UI-CONTRACT.md → Control Buttons → Mute).</summary>
public sealed class MuteToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "🔇" : "🔊";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

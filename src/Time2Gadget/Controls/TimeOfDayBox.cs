using System.Windows;
using System.Windows.Controls;

namespace Time2Gadget.Controls;

/// <summary>
/// Время «ЧЧ:ММ:СС» из трёх <see cref="TimeSpinner"/> — стрелки, колесо мыши, ввод цифрами (как у быстрых таймеров).
/// Для времени старта/смены слайдшоу и его интервала (докладка 2026-09-30).
/// </summary>
public sealed class TimeOfDayBox : StackPanel
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TimeSpan), typeof(TimeOfDayBox),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TimeOfDayBox)d).SyncParts()));

    public TimeSpan Value { get => (TimeSpan)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private readonly TimeSpinner _hours = new() { Maximum = 23, ToolTip = "Часы" };
    private readonly TimeSpinner _minutes = new() { ToolTip = "Минуты" };
    private readonly TimeSpinner _seconds = new() { ToolTip = "Секунды" };
    private bool _syncing;

    public TimeOfDayBox()
    {
        Orientation = Orientation.Horizontal;
        foreach (var (spinner, index) in new[] { (_hours, 0), (_minutes, 1), (_seconds, 2) })
        {
            if (index > 0) Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
            spinner.Width = 30;
            spinner.Height = 26;
            Children.Add(spinner);
            DependencyPropertyDescriptorFor(spinner);
        }
        SyncParts();
    }

    private void DependencyPropertyDescriptorFor(TimeSpinner spinner) =>
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TimeSpinner.ValueProperty, typeof(TimeSpinner))
            .AddValueChanged(spinner, (_, _) => OnPartChanged());

    private void OnPartChanged()
    {
        if (_syncing) return;
        Value = new TimeSpan(_hours.Value, _minutes.Value, _seconds.Value);
    }

    private void SyncParts()
    {
        _syncing = true;
        _hours.Value = Math.Clamp(Value.Hours, 0, 23);
        _minutes.Value = Value.Minutes;
        _seconds.Value = Value.Seconds;
        _syncing = false;
    }
}

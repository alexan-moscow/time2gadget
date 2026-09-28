using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Time2Gadget.Controls;

/// <summary>
/// Компактное поле числа 0..Maximum для часов/минут/секунд быстрого таймера (докладка 2026-09-28): ввод цифрами,
/// ▲▼ справа, стрелки вверх/вниз на клавиатуре и колесо мыши. Значение всегда двумя цифрами («05»).
/// </summary>
public sealed class TimeSpinner : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(TimeSpinner),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TimeSpinner)d).SyncText()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(TimeSpinner), new PropertyMetadata(59));

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    private readonly TextBox _box;

    public TimeSpinner()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition());

        _box = new TextBox
        {
            MaxLength = 2,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Text = "00"
        };
        SetRowSpan(_box, 2);
        _box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _box.LostFocus += (_, _) => CommitText();
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Step(+1); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
            else if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
        };
        _box.GotKeyboardFocus += (_, _) => _box.SelectAll();
        Children.Add(_box);

        Children.Add(MakeArrow("▲", +1, 0));
        Children.Add(MakeArrow("▼", -1, 1));

        PreviewMouseWheel += (_, e) => { Step(e.Delta > 0 ? +1 : -1); e.Handled = true; };
    }

    private RepeatButton MakeArrow(string glyph, int delta, int row)
    {
        var b = new RepeatButton
        {
            Content = new TextBlock { Text = glyph, FontSize = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            Delay = 350,
            Interval = 60,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        b.Template = BuildArrowTemplate();
        b.Click += (_, _) => Step(delta);
        SetColumn(b, 1);
        SetRow(b, row);
        return b;
    }

    /// <summary>Плоская стрелка: без системной рамки, подсветка при наведении — как у остальных кнопок темы.</summary>
    private static ControlTemplate BuildArrowTemplate()
    {
        var template = new ControlTemplate(typeof(RepeatButton));
        var border = new FrameworkElementFactory(typeof(Border), "Bg");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Application.Current.TryFindResource("Brush.SurfaceSectorHover"), "Bg"));
        template.Triggers.Add(hover);
        return template;
    }

    /// <summary>Шаг по кругу: после максимума — 0, ниже 0 — максимум (удобно крутить колесом).</summary>
    private void Step(int delta)
    {
        CommitText();
        int range = Maximum + 1;
        Value = ((Value + delta) % range + range) % range;
    }

    private void CommitText()
    {
        int v = int.TryParse(_box.Text, out var parsed) ? Math.Clamp(parsed, 0, Maximum) : 0;
        if (v != Value) Value = v; else SyncText();
    }

    private void SyncText() => _box.Text = Value.ToString("00");
}

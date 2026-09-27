using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Time2Gadget.Converters;

namespace Time2Gadget.Controls;

/// <summary>
/// Время для сегментного циферблата (docs/DESIGN-SYSTEM.md → Циферблат). Строится из Run'ов, чтобы
/// мигающее двоеточие гасло сменой ЦВЕТА (прозрачный), а не подменой символа: любая подмена меняет
/// ширину строки, и Viewbox вокруг перемасштабирует цифры каждые полсекунды («разъезжается»,
/// докладка 2026-09-27). IsGhost — слой погасших сегментов: цифры заменены на 8, двоеточие горит всегда.
/// </summary>
public sealed class SegmentTimeText : TextBlock
{
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(TimeSpan), typeof(SegmentTimeText), new PropertyMetadata(TimeSpan.Zero, OnChanged));

    public static readonly DependencyProperty IsColonLitProperty = DependencyProperty.Register(
        nameof(IsColonLit), typeof(bool), typeof(SegmentTimeText), new PropertyMetadata(true, OnChanged));

    public static readonly DependencyProperty IsGhostProperty = DependencyProperty.Register(
        nameof(IsGhost), typeof(bool), typeof(SegmentTimeText), new PropertyMetadata(false, OnChanged));

    public TimeSpan Time
    {
        get => (TimeSpan)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public bool IsColonLit
    {
        get => (bool)GetValue(IsColonLitProperty);
        set => SetValue(IsColonLitProperty, value);
    }

    public bool IsGhost
    {
        get => (bool)GetValue(IsGhostProperty);
        set => SetValue(IsGhostProperty, value);
    }

    public SegmentTimeText() => Rebuild();

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SegmentTimeText)d).Rebuild();

    private void Rebuild()
    {
        var text = TimeSpanToStringConverter.Format(Time);
        if (IsGhost) text = new string(text.Select(c => char.IsDigit(c) ? '8' : c).ToArray());

        Inlines.Clear();
        foreach (var part in SplitKeepingColons(text))
        {
            var run = new Run(part);
            if (part == ":" && !IsGhost && !IsColonLit) run.Foreground = Brushes.Transparent;
            Inlines.Add(run);
        }
    }

    private static IEnumerable<string> SplitKeepingColons(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ':') continue;
            if (i > start) yield return text[start..i];
            yield return ":";
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }
}

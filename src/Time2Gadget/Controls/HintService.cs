using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Time2Gadget.Controls;

/// <summary>
/// Собственные всплывающие подсказки (docs/UI-CONTRACT.md → Подсказки, докладка 2026-09-27) вместо
/// стандартного ToolTip: стандартный не умеет «показать только после 2 сек неподвижной мыши и сразу
/// погаснуть при движении», и его текст статичен, а подсказки зависят от состояния таймера
/// (первый клик по сектору — «запустить», повторный — «добавить N минут»).
///
/// Элемент помечается ключом (HintService.Key), сам текст по ключу выдаёт resolver окна —
/// в момент показа, поэтому он всегда соответствует текущему состоянию.
/// </summary>
public static class HintService
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(HintService), new PropertyMetadata(null));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string? value) => d.SetValue(KeyProperty, value);

    /// <summary>Ближайший предок (включая сам элемент) с заданным ключом подсказки.</summary>
    public static FrameworkElement? FindHintTarget(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement fe && GetKey(fe) is not null) return fe;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}

/// <summary>
/// Показ/скрытие подсказок одного окна. Мышь неподвижна над помеченным элементом <see cref="ShowDelay"/> —
/// появляется подсказка (короткий fade-in); любое заметное движение, клик или уход мыши — быстро гаснет.
/// </summary>
public sealed class HintController
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(90);
    private const double MoveTolerance = 3; // px — дрожание руки не считается движением
    private const double CursorOffset = 18; // px — подсказка под курсором, не перекрывает его

    private readonly FrameworkElement _host;
    private readonly Func<FrameworkElement, string?> _resolveText;
    private readonly DispatcherTimer _delayTimer;
    private readonly Popup _popup;
    private readonly Border _bubble;
    private readonly TextBlock _text;
    private Point? _restPoint;

    public HintController(FrameworkElement host, Func<FrameworkElement, string?> resolveText)
    {
        _host = host;
        _resolveText = resolveText;

        _text = new TextBlock { Style = host.TryFindResource("Style.HintText") as Style };
        _bubble = new Border { Style = host.TryFindResource("Style.HintBubble") as Style, Child = _text, Opacity = 0 };
        _popup = new Popup
        {
            Child = _bubble,
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = host,
            IsHitTestVisible = false,
            Focusable = false
        };

        _delayTimer = new DispatcherTimer { Interval = ShowDelay };
        _delayTimer.Tick += (_, _) => { _delayTimer.Stop(); TryShow(); };

        host.PreviewMouseMove += OnMouseMove;
        host.PreviewMouseDown += (_, _) => Cancel();
        host.PreviewMouseWheel += (_, _) => Cancel();
        host.MouseLeave += (_, _) => Cancel();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) { Cancel(); return; } // идёт перетаскивание окна

        var p = e.GetPosition(_host);
        if (_restPoint is { } rest && Math.Abs(p.X - rest.X) <= MoveTolerance && Math.Abs(p.Y - rest.Y) <= MoveTolerance)
            return;

        _restPoint = p;
        Hide();
        _delayTimer.Stop();
        _delayTimer.Start();
    }

    /// <summary>Спрятать подсказку и не показывать, пока мышь снова не сдвинется и не замрёт.</summary>
    public void Cancel()
    {
        _delayTimer.Stop();
        _restPoint = null;
        Hide();
    }

    private void TryShow()
    {
        if (_restPoint is not { } p || !_host.IsMouseOver) return;

        var hit = _host.InputHitTest(p) as DependencyObject;
        var target = HintService.FindHintTarget(hit);
        var text = target is null ? null : _resolveText(target);
        if (string.IsNullOrEmpty(text)) return;

        _text.Text = text;
        _popup.HorizontalOffset = p.X + CursorOffset / 2;
        _popup.VerticalOffset = p.Y + CursorOffset;
        _popup.IsOpen = true;
        _bubble.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, FadeIn) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Hide()
    {
        if (!_popup.IsOpen) return;

        var fade = new DoubleAnimation(_bubble.Opacity, 0, FadeOut);
        fade.Completed += (_, _) =>
        {
            // Мышь могла замереть и показать новую подсказку, пока гасла старая — её не закрываем.
            if (_bubble.Opacity == 0) _popup.IsOpen = false;
        };
        _bubble.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

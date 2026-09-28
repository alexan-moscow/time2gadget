using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Time2Gadget.Models;

namespace Time2Gadget.Controls;

/// <summary>
/// Поле назначения клавиши (докладка 2026-09-28): щёлкнуть → «Нажмите сочетание…» → нажать клавишу с
/// Ctrl/Shift/Alt/Win или кнопку мыши (средняя, «Назад», «Вперёд») с модификаторами. Esc — отмена,
/// Backspace/Delete — очистить. Пока идёт ввод, глобальные клавиши программы приостановлены
/// (<see cref="CaptureChanged"/>), иначе уже назначенное сочетание сработало бы вместо записи.
/// </summary>
public sealed class HotkeyBox : Border
{
    public static readonly DependencyProperty BindingValueProperty = DependencyProperty.Register(
        nameof(BindingValue), typeof(HotkeyBinding), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(HotkeyBinding.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).Render()));

    public HotkeyBinding BindingValue
    {
        get => (HotkeyBinding)GetValue(BindingValueProperty);
        set => SetValue(BindingValueProperty, value);
    }

    /// <summary>Начался (true) / закончился (false) ввод в каком-либо поле — для приостановки глобальных клавиш.</summary>
    public static event EventHandler<bool>? CaptureChanged;

    private readonly TextBlock _text;
    private bool _capturing;

    public HotkeyBox()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        Height = 24;
        Padding = new Thickness(6, 0, 6, 0);
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Brush.SurfaceSector");
        SetResourceReference(BorderBrushProperty, "Brush.BorderSubtle");
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        Child = _text;
        Render();

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        LostKeyboardFocus += (_, _) => EndCapture();
        ToolTip = "Щёлкните и нажмите сочетание клавиш или кнопку мыши. Esc — отмена, Backspace — очистить.";
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_capturing)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Focus();
            BeginCapture();
            e.Handled = true;
            return;
        }

        var button = e.ChangedButton switch
        {
            MouseButton.Middle => HotkeyMouseButton.Middle,
            MouseButton.XButton1 => HotkeyMouseButton.XButton1,
            MouseButton.XButton2 => HotkeyMouseButton.XButton2,
            _ => HotkeyMouseButton.None // левую/правую назначать нельзя — ими кликают
        };
        e.Handled = true;
        if (button == HotkeyMouseButton.None) return;
        BindingValue = new HotkeyBinding { MouseButton = button, Modifiers = Keyboard.Modifiers };
        EndCapture();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            if (e.Key is Key.Enter or Key.Space) { BeginCapture(); e.Handled = true; }
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed)
            return; // ждём основную клавишу

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key == Key.Escape) { EndCapture(); return; }
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete) { BindingValue = HotkeyBinding.Empty; EndCapture(); return; }

        BindingValue = HotkeyBinding.FromKey(key, modifiers);
        EndCapture();
    }

    private void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        CaptureChanged?.Invoke(this, true);
        Render();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        CaptureChanged?.Invoke(this, false);
        Render();
    }

    private void Render()
    {
        if (_capturing)
        {
            _text.Text = "Нажмите сочетание…";
            _text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            SetResourceReference(BorderBrushProperty, "Brush.Accent");
        }
        else
        {
            _text.Text = BindingValue?.ToString() ?? "Не задано";
            _text.SetResourceReference(TextBlock.ForegroundProperty,
                BindingValue is null || BindingValue.IsEmpty ? "Brush.TextSecondary" : "Brush.TextPrimary");
            SetResourceReference(BorderBrushProperty, "Brush.BorderSubtle");
        }
    }
}

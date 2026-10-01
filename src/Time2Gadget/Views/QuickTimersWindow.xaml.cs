using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Окно «Быстрые таймеры» (докладка 2026-10-01: раздел вынесен из настроек). Привязка — к MainViewModel, как в настройках;
/// обработчики меню звука/эффекта строки таймера — перенесены из SettingsWindow.
/// </summary>
public partial class QuickTimersWindow : Window
{
    private static QuickTimersWindow? _current;
    private readonly MainViewModel _viewModel;

    public QuickTimersWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        WindowMemory.Attach(this, viewModel, "QuickTimers"); // открывается там, где оставили
        _viewModel = viewModel;
        DataContext = viewModel;
        MaxHeight = SystemParameters.WorkArea.Height;
        // Окно закрыли — прослушивание и показ эффекта не должны продолжаться без него.
        Closed += (_, _) =>
        {
            _viewModel.StopPreview();
            _viewModel.StopEffectPreview();
            _viewModel.StopPanelPreview();
        };
    }

    /// <summary>Одно окно на программу; уже открыто — вывести вперёд. Без владельца (клавиша) — по центру экрана.</summary>
    public static void ShowSingle(MainViewModel main, Window? owner)
    {
        if (_current is null)
        {
            _current = new QuickTimersWindow(main) { Owner = owner };
            if (owner is null && _current.WindowStartupLocation != WindowStartupLocation.Manual) _current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        else if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }

    /// <summary>▶ во всплывашке быстрого таймера: звонок на устройстве этого таймера; строку списка не выбирает.</summary>
    private void OnQuickSoundPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string ringtoneId } button && FindQuickTimer(button) is { } item) item.Preview(ringtoneId);
    }

    /// <summary>Строка быстрого таймера, к которой относится элемент меню (меню — всплывашка, ищем и по логическому дереву).</summary>
    private static QuickTimerItem? FindQuickTimer(DependencyObject start)
    {
        for (DependencyObject? d = start; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: QuickTimerItem item }) return item;
        return null;
    }

    /// <summary>Щелчок пришёлся на кнопку ▶ внутри пункта — это просмотр, а не выбор.</summary>
    private static bool IsOnButton(object? source, DependencyObject container)
    {
        for (var d = source as DependencyObject; d is not null && !ReferenceEquals(d, container); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    /// <summary>Щелчок по звонку в меню быстрого таймера: выбрать, включить звук, закрыть меню.</summary>
    private void OnQuickSoundItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: RingtoneOption option } container
            || IsOnButton(e.OriginalSource, container)) return;
        FindQuickTimer(container)?.ChooseRingtone(option.Id);
    }

    /// <summary>Щелчок по эффекту в меню быстрого таймера: выбрать, прекратить показ, закрыть меню.</summary>
    private void OnQuickEffectItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: EffectOption option } container
            || IsOnButton(e.OriginalSource, container)) return;
        FindQuickTimer(container)?.ChooseEffect(option.Value);
    }

    /// <summary>▶/■ у эффекта в меню быстрого таймера — показ на циферблате.</summary>
    private void OnEffectPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: EffectOption option } button) return;
        for (DependencyObject? d = button; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is not System.Windows.Controls.ListBoxItem) continue;
            if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(d)?.ItemsSource is IReadOnlyList<EffectOption> list)
                _viewModel.TogglePreviewEffect(list, option);
            return;
        }
    }

    private void OnQuickEffectPopupClosed(object? sender, EventArgs e) => _viewModel.StopEffectPreview();

    /// <summary>▶/■ у эффекта виджета (списки хода и окончания): показ этого таймера в виджете; пункт не выбирается.</summary>
    private void OnPanelEffectPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: EffectOption option } button && FindQuickTimer(button) is { } item)
            item.TogglePanelPreview(option);
    }

    /// <summary>Список эффектов виджета закрыли (или выбрали пункт) — просмотр прекращается, всё как было.</summary>
    private void OnPanelEffectDropDownClosed(object? sender, EventArgs e) => _viewModel.StopPanelPreview();

    /// <summary>Закрыли меню звука быстрого таймера — играющий звук гаснет (с затуханием).</summary>
    private void OnQuickSoundPopupClosed(object? sender, EventArgs e) => _viewModel.StopPreview();

    private void OnRingtonePreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    // ---- Имя таймера: применяется по Enter и по клику в сторону (раньше — только когда фокус уходил в другое поле) ----

    private void OnNameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not System.Windows.Controls.TextBox box) return;
        CommitName(box);
        e.Handled = true;
    }

    private void OnWindowPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox { Tag: "TimerName" } box) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, box)) return; // клик в самом поле
        CommitName(box);
    }

    private void CommitName(System.Windows.Controls.TextBox box)
    {
        box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        System.Windows.Input.Keyboard.ClearFocus();
        System.Windows.Input.FocusManager.SetFocusedElement(this, null);
    }
}

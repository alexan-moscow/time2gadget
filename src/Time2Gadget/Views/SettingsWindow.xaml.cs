using System.Windows;
using Time2Gadget.ViewModels;

namespace Time2Gadget.Views;

/// <summary>
/// Окно настроек. Биндится НАПРЯМУЮ на MainViewModel (не отдельная ViewModel) — изменения
/// применяются мгновенно в рантайме без синхронизирующего механизма, см. docs/ARCHITECTURE.md.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // Настройки закрыли — прослушивание и показ эффекта не должны продолжаться без них.
        Closed += (_, _) =>
        {
            _viewModel.StopPreview();
            _viewModel.StopEffectPreview();
        };

        // Окно настроек открывается там, где его оставили (докладка 2026-09-28), если это место видно на экране;
        // иначе — по центру главного окна (CenterOwner из XAML).
        if (_viewModel.SavedSettingsWindowPosition is { } saved
            && MainWindow.IsVisibleOnScreen(saved.X, saved.Y, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.X;
            Top = saved.Y;
        }
        Closing += (_, _) => _viewModel.SaveSettingsWindowPosition(Left, Top);
    }

    /// <summary>После сна/смены мониторов — вернуть на сохранённое место, если оно снова видно (см. MainWindow).</summary>
    public void RestoreSavedPosition()
    {
        if (_viewModel.SavedSettingsWindowPosition is { } saved
            && MainWindow.IsVisibleOnScreen(saved.X, saved.Y, Width, Height)
            && (Math.Abs(Left - saved.X) > 1 || Math.Abs(Top - saved.Y) > 1))
        {
            Left = saved.X;
            Top = saved.Y;
        }
    }

    /// <summary>После «Сбросить настройки» — на исходное место: по центру главного окна.</summary>
    public void CenterOnOwner()
    {
        if (Owner is null) return;
        Left = Owner.Left + (Owner.Width - Width) / 2;
        Top = Owner.Top + (Owner.Height - Height) / 2;
    }

    /// <summary>
    /// ▶ в строке списка звонков: слушать, НЕ выбирая. Обычный Command не годится — строка списка
    /// (ComboBoxItem) успевала выбрать звонок и закрыть список раньше, чем срабатывала кнопка (найдено
    /// тестом 2026-09-27). Перехват на Preview-стадии гасит нажатие до строки: выбор не меняется,
    /// список остаётся открытым — можно прослушать несколько подряд.
    /// </summary>
    private void OnRingtonePreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string ringtoneId })
            _viewModel.PreviewRingtoneCommand.Execute(ringtoneId);
        e.Handled = true;
    }

    /// <summary>▶ во всплывашке быстрого таймера: звонок на устройстве этого таймера; строку списка не выбирает.</summary>
    private void OnQuickSoundPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string ringtoneId } button && FindQuickTimer(button) is { } item) item.Preview(ringtoneId);
    }

    /// <summary>Строка быстрого таймера, к которой относится элемент меню (меню — всплывашка, ищем и по логическому дереву).</summary>
    private static ViewModels.QuickTimerItem? FindQuickTimer(DependencyObject start)
    {
        for (DependencyObject? d = start; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: ViewModels.QuickTimerItem item }) return item;
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
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: ViewModels.RingtoneOption option } container
            || IsOnButton(e.OriginalSource, container)) return;
        FindQuickTimer(container)?.ChooseRingtone(option.Id);
    }

    /// <summary>Щелчок по эффекту в меню быстрого таймера: выбрать, прекратить показ, закрыть меню.</summary>
    private void OnQuickEffectItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: ViewModels.EffectOption option } container
            || IsOnButton(e.OriginalSource, container)) return;
        FindQuickTimer(container)?.ChooseEffect(option.Value);
    }

    /// <summary>▶/■ у эффекта (меню быстрого таймера или выпадающий список раздела эффектов) — показ на циферблате.</summary>
    private void OnEffectPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: ViewModels.EffectOption option } button) return;
        for (DependencyObject? d = button; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is not System.Windows.Controls.ListBoxItem and not System.Windows.Controls.ComboBoxItem) continue;
            if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(d)?.ItemsSource is IReadOnlyList<ViewModels.EffectOption> list)
                _viewModel.TogglePreviewEffect(list, option);
            return;
        }
    }

    private void OnQuickEffectPopupClosed(object? sender, EventArgs e) => _viewModel.StopEffectPreview();

    /// <summary>Список звонков раздела «Звук» закрыт (выбор или щелчок мимо) — прослушивание гаснет с затуханием.</summary>
    private void OnRingtoneDropDownClosed(object? sender, EventArgs e) => _viewModel.StopPreview();

    private void OnEffectDropDownClosed(object? sender, EventArgs e) => _viewModel.StopEffectPreview();

    /// <summary>Закрыли меню звука быстрого таймера — играющий звук гаснет (с затуханием).</summary>
    private void OnQuickSoundPopupClosed(object? sender, EventArgs e) => _viewModel.StopPreview();

    private void OnRingtonePreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>
    /// Прокрутить так, чтобы раздел «Обновления» оказался вверху окна (клик по мигающей шестерёнке, 2026-09-27).
    /// Отложено до окончания раскладки: сразу после Show() позиции элементов ещё не посчитаны.
    /// </summary>
    public double ScrollOffset => Scroller.VerticalOffset;

    /// <summary>Прокрутить на позицию (после раскладки — сразу после Show() высота содержимого ещё не посчитана).</summary>
    public void ScrollToOffset(double offset) =>
        Dispatcher.BeginInvoke(() => Scroller.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);

    public void ScrollToUpdates()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Scroller.Content is not UIElement content) return;
            var y = UpdatesSection.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
            Scroller.ScrollToVerticalOffset(Math.Max(0, y - 8));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}

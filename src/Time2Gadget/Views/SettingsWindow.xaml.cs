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

    private void OnRingtonePreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;
}

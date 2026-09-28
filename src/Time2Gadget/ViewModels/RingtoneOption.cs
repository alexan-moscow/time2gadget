using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Пункт списка звонков (раздел «Звук» и всплывашка быстрого таймера, 2026-09-28): название, длительность
/// («45 сек», «1 мин 24 сек») и признак «сейчас играет» — кнопка ▶ превращается в ■ и повторное нажатие гасит звук.
/// </summary>
public sealed class RingtoneOption : INotifyPropertyChanged
{
    public RingtoneOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }

    private string _durationText = string.Empty;
    public string DurationText { get => _durationText; set { if (_durationText != value) { _durationText = value; OnPropertyChanged(); } } }

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } } }

    /// <summary>Есть что слушать (у «Свой файл…» — только когда файл выбран).</summary>
    private bool _canPreview = true;
    public bool CanPreview { get => _canPreview; set { if (_canPreview != value) { _canPreview = value; OnPropertyChanged(); } } }

    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } d) return string.Empty;
        int total = (int)Math.Round(d.TotalSeconds);
        if (total < 60) return $"{Math.Max(total, 1)} сек";
        int min = total / 60, sec = total % 60;
        return sec == 0 ? $"{min} мин" : $"{min} мин {sec} сек";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

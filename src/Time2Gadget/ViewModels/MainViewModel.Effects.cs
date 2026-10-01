using System.ComponentModel;
using System.Runtime.CompilerServices;
using Time2Gadget.Models;

namespace Time2Gadget.ViewModels;

/// <summary>Пункт списка эффектов (RunningVisualEffect или FinishVisualEffect) с кнопкой просмотра ▶/■.</summary>
public sealed class EffectOption : INotifyPropertyChanged
{
    public EffectOption(object value, string label)
    {
        Value = value;
        Label = label;
    }

    public object Value { get; }
    public string Label { get; }

    /// <summary>«Нет эффекта» смотреть нечего.</summary>
    public bool CanPreview => Convert.ToInt32(Value) != 0;

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Эффекты (докладка 2026-09-28): просмотр эффекта на циферблате из настроек (▶/■ в списках; закрытие списка или
/// выбор пункта прекращает показ) и свой эффект завершения у быстрых таймеров.
/// </summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<EffectOption> RunningEffectOptions { get; } = new[]
    {
        new EffectOption(RunningVisualEffect.None, "Отключено"),
        new EffectOption(RunningVisualEffect.Pulse, "Пульсация"),
        new EffectOption(RunningVisualEffect.Flash, "Вспышка акцентом"),
        new EffectOption(RunningVisualEffect.ColorBreathe, "Дыхание цветом"),
        new EffectOption(RunningVisualEffect.Waves, "Встречные волны"),
        new EffectOption(RunningVisualEffect.Snake, "Змейка по рамке"),
        new EffectOption(RunningVisualEffect.RainbowSnake, "Радужная змейка"),
    };

    public IReadOnlyList<EffectOption> FinishEffectOptions { get; } = CreateFinishEffectOptions("Отключено");

    internal static IReadOnlyList<EffectOption> CreateFinishEffectOptions(string noneLabel) => new[]
    {
        new EffectOption(FinishVisualEffect.None, noneLabel),
        new EffectOption(FinishVisualEffect.Pulse, "Пульсация"),
        new EffectOption(FinishVisualEffect.Flash, "Строб-вспышка"),
        new EffectOption(FinishVisualEffect.ColorCycle, "Радужная волна"),
        new EffectOption(FinishVisualEffect.Waves, "Встречные волны"),
        new EffectOption(FinishVisualEffect.Snake, "Змейка по рамке"),
        new EffectOption(FinishVisualEffect.RainbowSnake, "Радужная змейка"),
    };

    // ---- Просмотр эффекта ----

    private IReadOnlyList<EffectOption>? _effectPreviewList;
    private object? _previewEffect;

    /// <summary>Эффект, который сейчас показывается для просмотра (RunningVisualEffect/FinishVisualEffect); null — нет.
    /// Главное окно показывает его вместо эффекта по состоянию таймера.</summary>
    public object? PreviewEffect
    {
        get => _previewEffect;
        private set { if (Equals(_previewEffect, value)) return; _previewEffect = value; OnPropertyChanged(); }
    }

    /// <summary>▶/■ у эффекта в списке: тот же ещё раз — остановить, другой — показать его.</summary>
    internal void TogglePreviewEffect(IReadOnlyList<EffectOption> list, EffectOption option)
    {
        if (ReferenceEquals(_effectPreviewList, list) && Equals(PreviewEffect, option.Value)) { StopEffectPreview(); return; }
        if (!option.CanPreview) return;
        SetEffectPreview(list, option.Value);
    }

    /// <summary>Закрыт список/меню эффектов или выбран пункт — показ прекращается.</summary>
    public void StopEffectPreview() => SetEffectPreview(null, null);

    private void SetEffectPreview(IReadOnlyList<EffectOption>? list, object? value)
    {
        _effectPreviewList = list;
        PreviewEffect = value;
        var lists = new List<IReadOnlyList<EffectOption>> { RunningEffectOptions, FinishEffectOptions };
        lists.AddRange(QuickTimers.Select(q => q.EffectOptions));
        foreach (var l in lists)
            foreach (var option in l)
                option.IsPlaying = ReferenceEquals(l, list) && Equals(option.Value, value);
    }

    // ---- Эффект и звук быстрого таймера для идущего отсчёта ----

    private FinishVisualEffect? _quickFinishEffect;
    private bool _quickSilent;

    /// <summary>Эффект завершения для окна и трея: свой у отсчёта, запущенного быстрым таймером, иначе — из настроек.</summary>
    public FinishVisualEffect ActiveFinishEffect => _quickFinishEffect ?? FinishEffect;

    /// <summary>Быстрый таймер, который сейчас идёт на главном таймере (кнопки строки в окне «Быстрые таймеры» управляют им).</summary>
    internal QuickTimer? MainQuickTimer { get; private set; }

    private void SetQuickTimerOverrides(QuickTimer? timer)
    {
        MainQuickTimer = timer;
        foreach (var q in QuickTimers) q.RaiseRowState();
        _alarmChoice = timer?.Sound;
        _quickSilent = timer is { SoundEnabled: false };
        _quickFinishEffect = timer?.FinishEffect;
        OnPropertyChanged(nameof(ActiveFinishEffect));
        RefreshFinishEffectActive();
    }

    /// <summary>Время выбрали иначе (сектор, колесо, сброс) — звук и эффект снова общие.</summary>
    private void ClearQuickTimerOverrides()
    {
        if (MainQuickTimer is null && _alarmChoice is null && !_quickSilent && _quickFinishEffect is null) return;
        SetQuickTimerOverrides(null);
    }
}

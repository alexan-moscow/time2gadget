using Microsoft.Win32;
using Time2Gadget.Models;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Прослушивание звонков и длительности (докладка 2026-09-28). Любая кнопка ▶ — переключатель: пока звук играет,
/// она показывает ■, повторное нажатие гасит звук с затуханием (SoundService.StopAlarm). Играет только один звук сразу.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Встроенные звонки + «Свой файл…» последним пунктом (раздел «Звук»).</summary>
    public IReadOnlyList<RingtoneOption> RingtoneOptions => _ringtoneOptions ??= CreateRingtoneOptions(general: false, _settings.CustomSoundFilePath);
    private IReadOnlyList<RingtoneOption>? _ringtoneOptions;

    /// <summary>Список звонков; general — с пунктом «Общий звонок» первым (быстрые таймеры).</summary>
    internal IReadOnlyList<RingtoneOption> CreateRingtoneOptions(bool general, string? customPath)
    {
        var list = new List<RingtoneOption>();
        if (general) list.Add(new RingtoneOption(string.Empty, "Общий звонок"));
        list.AddRange(RingtoneCatalog.BuiltIn.Select(r => new RingtoneOption(r.Id, r.Title)
        {
            DurationText = _durationTexts.TryGetValue(r.Id, out var d) ? d : string.Empty
        }));
        list.Add(new RingtoneOption(RingtoneCatalog.CustomId, "Свой файл…"));
        UpdateCustomOption(list, customPath);
        return list;
    }

    /// <summary>Пункт «Свой файл…»: слушать можно, только если файл выбран; длительность — считаем в фоне.</summary>
    internal void UpdateCustomOption(IReadOnlyList<RingtoneOption> list, string? customPath)
    {
        var custom = list.FirstOrDefault(o => o.Id == RingtoneCatalog.CustomId);
        if (custom is null) return;
        bool exists = !string.IsNullOrWhiteSpace(customPath) && System.IO.File.Exists(customPath);
        custom.CanPreview = exists;
        custom.DurationText = string.Empty;
        if (!exists) return;
        Task.Run(() => RingtoneOption.FormatDuration(_soundService.GetDuration(RingtoneCatalog.CustomId, customPath)))
            .ContinueWith(t => custom.DurationText = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // Длительности встроенных — один раз в фоне при старте (распаковка mp3 в кэш + чтение заголовков).
    private readonly Dictionary<string, string> _durationTexts = new();

    private void LoadBuiltInDurations()
    {
        Task.Run(() => RingtoneCatalog.BuiltIn.ToDictionary(r => r.Id,
                r => RingtoneOption.FormatDuration(_soundService.GetDuration(r.Id, null))))
            .ContinueWith(t =>
            {
                foreach (var (id, text) in t.Result) _durationTexts[id] = text;
                foreach (var option in AllRingtoneOptions())
                    if (_durationTexts.TryGetValue(option.Id, out var d)) option.DurationText = d;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private IEnumerable<RingtoneOption> AllRingtoneOptions() =>
        RingtoneOptions.Concat(QuickTimers.SelectMany(q => q.SoundOptions));

    // ---- Прослушивание ----

    private object? _previewOwner;   // чей список (VM — раздел «Звук», QuickTimerItem — всплывашка, "button" — кнопка «Прослушать»)
    private string? _previewId;      // какой звонок
    private int _previewNumber;      // номер от SoundService — чтобы конец старого прослушивания не погасил новое

    /// <summary>Что-то играет — кнопка «Прослушать» показывает «Стоп».</summary>
    public bool IsPreviewPlaying => _previewOwner is not null;

    /// <summary>▶/■: тот же звук ещё раз — остановить; другой — остановить текущий и включить этот.</summary>
    internal void TogglePreview(object owner, string id, SoundChoice? choice)
    {
        if (ReferenceEquals(_previewOwner, owner) && _previewId == id) { StopPreview(); return; }
        int number = _soundService.PlayPreview(_settings, choice);
        SetPreview(number == 0 ? null : owner, id, number);
    }

    /// <summary>Погасить прослушивание (закрыта всплывашка, повторное нажатие) — с затуханием. Звонок таймера не трогает.</summary>
    public void StopPreview()
    {
        if (_previewOwner is null) return;
        _soundService.StopAlarm();
        SetPreview(null, null, 0);
    }

    private void SetPreview(object? owner, string? id, int number)
    {
        (_previewOwner, _previewId, _previewNumber) = (owner, id, number);
        foreach (var option in RingtoneOptions)
            option.IsPlaying = ReferenceEquals(owner, this) && option.Id == id;
        foreach (var q in QuickTimers)
            foreach (var option in q.SoundOptions)
                option.IsPlaying = ReferenceEquals(owner, q) && option.Id == id;
        OnPropertyChanged(nameof(IsPreviewPlaying));
    }

    private void OnPreviewEnded(object? sender, int number) => _dispatcher.BeginInvoke(() =>
    {
        if (number == _previewNumber && _previewOwner is not null) SetPreview(null, null, 0); // доиграл сам
    });

    /// <summary>▶ в выпадающем списке раздела «Звук» (id) или кнопка «Прослушать» (null — выбранный звонок).</summary>
    private void PreviewFromSoundSection(object? parameter)
    {
        if (parameter is string { Length: > 0 } id)
        {
            TogglePreview(this, id, new SoundChoice(id, _settings.CustomSoundFilePath));
            return;
        }
        if (IsPreviewPlaying) StopPreview(); // «Стоп»
        else TogglePreview("button", SelectedRingtoneId, null);
    }

    // ---- Свои файлы ----

    /// <summary>Выбрать звуковой файл и скопировать к программе; null — отмена или не удалось.</summary>
    internal string? PickAndImportSound()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите звуковой файл",
            Filter = "Аудио файлы (*.wav;*.mp3)|*.wav;*.mp3|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return null;
        StopPreview();
        // Играем копию из папки программы, а не оригинал (докладка 2026-09-27); если скопировать
        // не удалось — хотя бы оригинал, чтобы выбор не потерялся.
        return _soundService.ImportCustomSound(dialog.FileName) ?? dialog.FileName;
    }

    /// <summary>Прежняя копия больше никому не нужна (ни разделу «Звук», ни быстрым таймерам) — удалить.</summary>
    internal void DeleteSoundIfUnused(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        bool used = PathEquals(_settings.CustomSoundFilePath, path)
                    || _settings.QuickTimers.Any(q => PathEquals(q.CustomSoundFilePath, path));
        if (!used) _soundService.DeleteImportedSound(path);

        static bool PathEquals(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

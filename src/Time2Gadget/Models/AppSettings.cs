namespace Time2Gadget.Models;

/// <summary>
/// Персистентные настройки приложения. Сериализуется в %APPDATA%\Time2Gadget\settings.json.
/// См. docs/ARCHITECTURE.md → Settings.
/// </summary>
public sealed class AppSettings
{
    public int LastPresetMinutes { get; set; } = 30;
    public bool IsMuted { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool LaunchAtStartup { get; set; } = false;

    /// <summary>Поведение при закрытии окна — сворачивать (по умолчанию) или закрывать приложение.</summary>
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;

    /// <summary>
    /// Переключатель слева от крестика: после отработавшего таймера (когда звонок отыграл) само
    /// закрыть/свернуть приложение — что именно, решает <see cref="CloseBehavior"/>. По умолчанию выключен.
    /// </summary>
    public bool AutoCloseAfterFinish { get; set; } = false;

    /// <summary>Громкость звонка, 0..1 (слайдер слева от кнопки Mute / в Settings).</summary>
    public double AlarmVolume { get; set; } = 0.5;

    /// <summary>Id устройства вывода звука (NAudio MMDevice.ID) или "" для системного по умолчанию.</summary>
    public string AudioDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Id звонка из Models/RingtoneCatalog (или RingtoneCatalog.CustomId — свой файл). С 2026-09-27 вместо
    /// прежнего enum SelectedRingtone (синтезированные звонки убраны); старое поле в settings.json просто игнорируется.
    /// </summary>
    public string RingtoneId { get; set; } = RingtoneCatalog.DefaultId;
    public string? CustomSoundFilePath { get; set; }

    /// <summary>
    /// Сколько раз звонить при завершении таймера (1..10) — не бесконечно. Каждое повторение —
    /// полное проигрывание звука от начала до конца (докладка 2026-09-27: заменяет прежнюю пару
    /// "длительность в секундах" + "интервал в мс", которая не привязывалась к реальной длине звука).
    /// </summary>
    public int AlarmRepeatCount { get; set; } = 3;

    /// <summary>Визуальный эффект циферблата во время отсчёта. По умолчанию отключён.</summary>
    public RunningVisualEffect RunningEffect { get; set; } = RunningVisualEffect.None;

    /// <summary>Визуальный эффект циферблата при завершении. По умолчанию — «Радужная волна» (решение пользователя 2026-09-27).</summary>
    public FinishVisualEffect FinishEffect { get; set; } = FinishVisualEffect.ColorCycle;

    /// <summary>
    /// Сколько секунд играет эффект завершения (в окне и в трее); 0 — бесконечно, до сброса/перезапуска
    /// таймера. По умолчанию 10 с (решение пользователя 2026-09-27). Шкала — MainViewModel.FinishEffectDurationSteps.
    /// </summary>
    public int FinishEffectDurationSeconds { get; set; } = 10;

    /// <summary>Показывать текущее время (часы) под статусом «ГОТОВО/ОСТАЛОСЬ» — в обоих режимах.</summary>
    public bool ShowClock { get; set; } = true;

    /// <summary>Часы с секундами (ЧЧ:ММ:СС) или без (ЧЧ:ММ).</summary>
    public bool ShowClockSeconds { get; set; } = false;

    /// <summary>Показывать дату (под часами в полном режиме, справа от часов в компакте).</summary>
    public bool ShowDate { get; set; } = true;

    /// <summary>
    /// Когда последний раз УСПЕШНО проверяли обновления (UTC). Автопроверка — если прошло ≥7 дней;
    /// неудачная проверка (нет сети) дату не двигает — повторим при следующей возможности.
    /// </summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    // "Об авторе" — тестовые плейсхолдеры (docs/DECISIONS.md, 2026-09-27).
    public string AuthorName { get; set; } = "Автор (заглушка)";
    public string VirusTotalUrl { get; set; } = "https://www.virustotal.com/gui/file/0000000000000000000000000000000000000000000000000000000000000000";
}

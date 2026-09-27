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

    public RingtoneChoice SelectedRingtone { get; set; } = RingtoneChoice.SoftChime;
    public string? CustomSoundFilePath { get; set; }

    /// <summary>
    /// Сколько раз звонить при завершении таймера (1..10) — не бесконечно. Каждое повторение —
    /// полное проигрывание звука от начала до конца (докладка 2026-09-27: заменяет прежнюю пару
    /// "длительность в секундах" + "интервал в мс", которая не привязывалась к реальной длине звука).
    /// </summary>
    public int AlarmRepeatCount { get; set; } = 3;

    /// <summary>Визуальный эффект циферблата во время отсчёта. По умолчанию отключён.</summary>
    public RunningVisualEffect RunningEffect { get; set; } = RunningVisualEffect.None;

    /// <summary>Визуальный эффект циферблата при завершении (пока звонит будильник). По умолчанию — строб-вспышка (2026-09-27).</summary>
    public FinishVisualEffect FinishEffect { get; set; } = FinishVisualEffect.Flash;

    /// <summary>
    /// Сколько секунд играет эффект завершения (в окне и в трее); 0 — бесконечно, до сброса/перезапуска
    /// таймера (так было до появления настройки, поэтому это и значение по умолчанию). Шкала — MainViewModel.FinishEffectDurationSteps.
    /// </summary>
    public int FinishEffectDurationSeconds { get; set; } = 0;

    // "Об авторе" — тестовые плейсхолдеры (docs/DECISIONS.md, 2026-09-27).
    public string AuthorName { get; set; } = "Автор (заглушка)";
    public string GitHubUrl { get; set; } = "https://github.com/example/Time2Gadget";
    public string VirusTotalUrl { get; set; } = "https://www.virustotal.com/gui/file/0000000000000000000000000000000000000000000000000000000000000000";
}

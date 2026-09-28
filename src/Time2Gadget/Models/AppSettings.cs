namespace Time2Gadget.Models;

/// <summary>
/// Персистентные настройки приложения. Сериализуется в %APPDATA%\Time2Gadget\settings.json.
/// См. docs/ARCHITECTURE.md → Settings. Значения по умолчанию утверждены пользователем 2026-09-27
/// (действуют для новых установок / без settings.json; сохранённые настройки не перезаписываются).
/// </summary>
public sealed class AppSettings
{
    public int LastPresetMinutes { get; set; } = 30;
    public bool IsMuted { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    /// <summary>Прежняя общая позиция окна (до 2026-09-28) — используется, только пока нет раздельных ниже.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    // Раздельные позиции полного и компактного вида (докладка 2026-09-28): первое переключение центрирует
    // компакт по циферблату, дальше каждый вид открывается там, где его оставили.
    public double? FullWindowLeft { get; set; }
    public double? FullWindowTop { get; set; }
    public double? CompactWindowLeft { get; set; }
    public double? CompactWindowTop { get; set; }

    /// <summary>Где было окно настроек (докладка 2026-09-28); null — по центру главного окна.</summary>
    public double? SettingsWindowLeft { get; set; }
    public double? SettingsWindowTop { get; set; }

    /// <summary>
    /// Будить компьютер из сна к моменту окончания таймера (системный таймер пробуждения). По умолчанию да.
    /// Сработает, только если в схеме питания разрешены таймеры пробуждения.
    /// </summary>
    public bool WakeFromSleepOnFinish { get; set; } = true;

    /// <summary>Включать погасшие мониторы, когда таймер закончился (компьютер работает, экраны выключены по простою). По умолчанию да.</summary>
    public bool WakeDisplayOnFinish { get; set; } = true;

    /// <summary>Возвращать окна других программ на их мониторы после сна/гашения мониторов (Services/WindowLayoutService). По умолчанию выкл.</summary>
    public bool RestoreOtherWindows { get; set; } = false;

    /// <summary>
    /// Запускать с правами администратора (через задачу Планировщика, без UAC при каждом запуске) — чтобы возвращать
    /// и окна программ, запущенных от администратора. По умолчанию выкл. Services/ElevationService.
    /// </summary>
    public bool RunElevated { get; set; } = false;

    /// <summary>
    /// Служебное: перезапуск ради смены прав (галочка «с правами администратора») был из открытых настроек — новая копия
    /// сразу открывает их с этой прокруткой и очищает поле (докладка 2026-09-28). null — не открывать.
    /// </summary>
    public double? ReopenSettingsScroll { get; set; }

    /// <summary>Для какого exe создана задача запуска с правами (портативная и установленная копии делят настройки).</summary>
    public string? ElevationTaskExePath { get; set; }

    /// <summary>
    /// Поля, которых эта версия не знает (записаны более новой версией), — сохраняются как есть (докладка 2026-09-28:
    /// старая 1.0.3 при сохранении стёрла новые поля, и после обновления они вернулись к значениям по умолчанию).
    /// </summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownFields { get; set; }

    // ---- Клавиши (докладка 2026-09-28). *Global = работает везде (RegisterHotKey), иначе — когда окно таймера
    // в фокусе; Esc в окне — всегда ещё и сброс. По умолчанию глобальная только «Показать / скрыть». ----
    public HotkeyBinding StartPauseKey { get; set; } = HotkeyBinding.FromKey(System.Windows.Input.Key.Space);
    public bool StartPauseGlobal { get; set; } = false;
    public HotkeyBinding ResetKey { get; set; } = HotkeyBinding.FromKey(System.Windows.Input.Key.R);
    public bool ResetGlobal { get; set; } = false;
    public HotkeyBinding CompactKey { get; set; } = HotkeyBinding.Empty;
    public bool CompactGlobal { get; set; } = false;

    /// <summary>Показать/скрыть окно таймера. По умолчанию не задана.</summary>
    public HotkeyBinding ShowHideKey { get; set; } = HotkeyBinding.Empty;
    public bool ShowHideGlobal { get; set; } = true;

    /// <summary>Быстрые таймеры (глобальные клавиши), до 5; по умолчанию одна пустая строка.</summary>
    public List<QuickTimer> QuickTimers { get; set; } = new() { new QuickTimer() };
    /// <summary>По умолчанию — да; при первом запуске App включает автозапуск в реестре (см. App.xaml.cs).</summary>
    public bool LaunchAtStartup { get; set; } = true;

    /// <summary>Поведение при закрытии окна — сворачивать (по умолчанию) или закрывать приложение.</summary>
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;

    /// <summary>
    /// Переключатель слева от крестика: после отработавшего таймера (когда звонок отыграл) само
    /// закрыть/свернуть приложение — что именно, решает <see cref="CloseBehavior"/>. По умолчанию выключен.
    /// </summary>
    public bool AutoCloseAfterFinish { get; set; } = false;

    /// <summary>
    /// Та же кнопка, левый щелчок (докладка 2026-09-28): после отработавшего таймера перейти в компактный вид (на его
    /// сохранённое место). Взаимоисключается с <see cref="AutoCloseAfterFinish"/> (правый щелчок). По умолчанию выкл.
    /// </summary>
    public bool AutoCompactAfterFinish { get; set; } = false;

    /// <summary>Громкость звонка, 0..1 (слайдер слева от кнопки Mute / в Settings).</summary>
    public double AlarmVolume { get; set; } = 0.6;

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
    public int AlarmRepeatCount { get; set; } = 2;

    /// <summary>Визуальный эффект циферблата во время отсчёта. По умолчанию — «Встречные волны» (решение пользователя 2026-09-28).</summary>
    public RunningVisualEffect RunningEffect { get; set; } = RunningVisualEffect.Waves;

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
    public bool ShowDate { get; set; } = false;

    /// <summary>
    /// Когда последний раз УСПЕШНО проверяли обновления (UTC). Автопроверка — если прошло ≥7 дней;
    /// неудачная проверка (нет сети) дату не двигает — повторим при следующей возможности.
    /// </summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Масштаб полного вида (ползунок в настройках), 1.0 = исходный размер.</summary>
    public double FullViewScale { get; set; } = 1.0;

    /// <summary>Масштаб компактного вида — отдельно от полного.</summary>
    public double CompactViewScale { get; set; } = 1.0;

    // «Об авторе» больше не настройка (2026-09-27): автор/ссылки — константы в MainViewModel; старые поля
    // AuthorName/GitHubUrl/VirusTotalUrl в settings.json просто игнорируются.
}

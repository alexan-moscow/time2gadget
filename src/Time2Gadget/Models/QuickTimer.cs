namespace Time2Gadget.Models;

/// <summary>
/// Быстрый таймер (докладка 2026-09-28): время ЧЧ:ММ:СС + глобальная клавиша/кнопка мыши; нажатие — запустить
/// таймер на это время заново. До 5 штук; по умолчанию одна пустая строка (нули, клавиша не задана).
/// Звук по умолчанию выключен — таймером можно пользоваться только с визуальным эффектом (решение пользователя
/// 2026-09-28); при включении — свой звонок, устройство и громкость. Эффект завершения — свой у каждого.
/// </summary>
public sealed class QuickTimer
{
    /// <summary>Звонок, подсвеченный в меню, пока звук не выбран (решение пользователя 2026-09-28).</summary>
    public const string DefaultRingtoneId = "timer-finished";

    /// <summary>Имя (докладка 2026-10-01): по умолчанию «Таймер N» — даётся при создании и не сдвигается, когда другие удаляют.</summary>
    public string? Name { get; set; }

    /// <summary>Дни (докладка 2026-10-01). Клавиша запускает главный таймер — он считает не больше суток; панель — все дни.</summary>
    public int Days { get; set; }
    public int Hours { get; set; }
    public int Minutes { get; set; }
    public int Seconds { get; set; }
    public HotkeyBinding Binding { get; set; } = HotkeyBinding.Empty;

    /// <summary>Звонить по окончании. Выкл — только эффект (колокольчик перечёркнут).</summary>
    public bool SoundEnabled { get; set; }

    /// <summary>Эффект циферблата и трея по окончании этого таймера (длительность — общая из настроек).</summary>
    public FinishVisualEffect FinishEffect { get; set; } = FinishVisualEffect.ColorCycle;

    /// <summary>Id из RingtoneCatalog (или CustomId — файл <see cref="CustomSoundFilePath"/>); null — общий звонок из раздела «Звук».</summary>
    public string? RingtoneId { get; set; } = DefaultRingtoneId;
    /// <summary>Свой файл таймера — копия в папке звуков программы (как у раздела «Звук»).</summary>
    public string? CustomSoundFilePath { get; set; }
    /// <summary>Своё устройство вывода (AudioDeviceInfo.Id, "" — системное); null — как в разделе «Звук».</summary>
    public string? AudioDeviceId { get; set; }
    /// <summary>Своя громкость 0..1; null — общая громкость (галочка «общая» в меню звука).</summary>
    public double? Volume { get; set; }

    // ---- Панель быстрых таймеров (докладка 2026-10-01) ----

    /// <summary>Показывать в панели быстрых таймеров.</summary>
    public bool ShowInPanel { get; set; }

    /// <summary>
    /// Эффекты и цвета виджета уже заданы (новым таймерам — случайные при создании). Первый таймер при первом включении в
    /// виджет получает «тонкие линии» голубым и строб красным (решение пользователя 2026-10-01).
    /// </summary>
    public bool PanelConfigured { get; set; }

    /// <summary>Эффект хода в панели и его цвет (#RRGGBB).</summary>
    public QuickPanelProgress PanelProgress { get; set; } = QuickPanelProgress.Fill;
    public string PanelColor { get; set; } = "#3D8BFF";

    /// <summary>Эффект окончания в панели, его цвет и длительность (0–20 с).</summary>
    public QuickPanelFinish PanelFinish { get; set; } = QuickPanelFinish.Blink;
    public string PanelFinishColor { get; set; } = "#FFD600";
    public int PanelFinishSeconds { get; set; } = 5;

    /// <summary>
    /// В панели постоянно (переключатель оранжевый, клик ПКМ). Иначе (синий) — по окончании таймер пропадает из панели,
    /// когда отыграют и эффект окончания, и звук (что дольше).
    /// </summary>
    public bool PanelPermanent { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public TimeSpan Duration => new(Days, Hours, Minutes, Seconds);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsUsable => Duration > TimeSpan.Zero && !Binding.IsEmpty;

    [System.Text.Json.Serialization.JsonIgnore]
    public SoundChoice Sound => new(RingtoneId, CustomSoundFilePath, AudioDeviceId, Volume);
}

/// <summary>Эффект хода быстрого таймера в панели.</summary>
public enum QuickPanelProgress
{
    None,
    /// <summary>Строка заливается цветом слева направо.</summary>
    Fill,
    /// <summary>Заливка строки убывает — остаток времени.</summary>
    Drain,
    /// <summary>Тонкая полоска под строкой.</summary>
    Line,
    /// <summary>Полоска из десяти делений под строкой.</summary>
    Segments,
    // Докладка 2026-10-01 — эффекты главного таймера на строке (в конец: сохранённые значения не сдвигаются).
    /// <summary>Строка мягко вспыхивает цветом.</summary>
    Flash,
    /// <summary>Медленное «дыхание» цвета.</summary>
    Breathe,
    /// <summary>Встречные волны по контуру строки.</summary>
    Waves,
    /// <summary>Змейка по контуру строки.</summary>
    Snake,
    /// <summary>Радужная змейка по контуру строки (цвет не выбирается).</summary>
    RainbowSnake,
    /// <summary>Тонкие вертикальные линии под строкой (2 через 2, высота 6).</summary>
    ThinLines,
}

/// <summary>Эффект окончания быстрого таймера в панели.</summary>
public enum QuickPanelFinish
{
    None,
    /// <summary>Строка мигает.</summary>
    Blink,
    /// <summary>Строка вспыхивает цветом таймера.</summary>
    Flash,
    /// <summary>Время пульсирует цветом таймера.</summary>
    Pulse,
    // Докладка 2026-10-01 — эффекты главного таймера на строке (в конец: сохранённые значения не сдвигаются).
    /// <summary>Резкие короткие вспышки.</summary>
    Strobe,
    /// <summary>Строка переливается всеми цветами (цвет не выбирается).</summary>
    Rainbow,
    /// <summary>Встречные волны по контуру строки.</summary>
    Waves,
    /// <summary>Змейка по контуру строки.</summary>
    Snake,
    /// <summary>Радужная змейка по контуру строки (цвет не выбирается).</summary>
    RainbowSnake,
}

/// <summary>Порядок таймеров в виджете быстрых таймеров.</summary>
public enum QuickPanelSort
{
    /// <summary>По порядку создания (как строки в окне «Быстрые таймеры»).</summary>
    Created,
    RemainingDescending,
    RemainingAscending,
    StartedDescending,
    StartedAscending,
}

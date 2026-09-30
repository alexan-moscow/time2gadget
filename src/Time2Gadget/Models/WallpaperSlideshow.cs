namespace Time2Gadget.Models;

/// <summary>Как часто «Динамичная заставка / слайдшоу» меняет картинки (окно «Заставка и фон экрана», докладка 2026-09-30).</summary>
public enum SlideshowKind
{
    /// <summary>Раз в час: с указанного времени старта или каждый реальный час (в :00 по местному времени).</summary>
    Hourly,
    /// <summary>Раз в сутки: в указанное время (по умолчанию 9:00) или в начале суток (00:00).</summary>
    Daily,
    /// <summary>Свой интервал: старт сейчас или в указанное время.</summary>
    Interval,
}

/// <summary>Эффект смены картинок слайдшоу (только при показе своим окном-подложкой, докладка 2026-10-01).</summary>
public enum SlideshowEffect
{
    /// <summary>Мгновенно.</summary>
    Instant,
    /// <summary>Плавное растворение (по умолчанию — ближе всего к смене средствами Windows).</summary>
    Fade,
    /// <summary>Сдвиг: новая картинка выезжает справа, старая уходит влево.</summary>
    Slide,
    /// <summary>Шторка: новая картинка открывается сверху вниз.</summary>
    Curtain,
    /// <summary>Наплыв: новая проявляется с лёгким приближением.</summary>
    Zoom,
}

/// <summary>Шаг слайдшоу на одном мониторе: картинка (в своём режиме) или сплошной цвет; пусто — прежний фон монитора.</summary>
public sealed class SlideshowSlot
{
    public string? Image { get; set; }
    public string? Color { get; set; }
    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;

    public bool IsEmpty => Image is null && Color is null;

    public SlideshowSlot Clone() => new() { Image = Image, Color = Color, Fit = Fit };
}

/// <summary>Настройки слайдшоу: расписание и 30 шагов на каждый монитор.</summary>
public sealed class SlideshowSettings
{
    public const int SlotCount = 30;

    public SlideshowKind Kind { get; set; } = SlideshowKind.Daily;

    /// <summary>
    /// Показ: false — средствами Windows (фон рабочего стола, без эффектов, не чаще раза в 2 с; по умолчанию — решение пользователя
    /// 2026-10-01), true — своим окном-подложкой за значками рабочего стола (эффекты, смена хоть раз в секунду).
    /// </summary>
    public bool UseUnderlay { get; set; }

    public SlideshowEffect Effect { get; set; } = SlideshowEffect.Fade;

    /// <summary>Раз в час: true — от указанного времени старта, false — каждый реальный час.</summary>
    public bool HourlyFromTime { get; set; }
    public TimeSpan HourlyStart { get; set; }

    /// <summary>Раз в сутки: true — в указанное время, false — в начале суток (00:00, по умолчанию).</summary>
    public bool DailyAtTime { get; set; }
    public TimeSpan DailyTime { get; set; } = TimeSpan.FromHours(9);

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Свой интервал: true — старт в указанное время, false — старт сейчас.</summary>
    public bool IntervalStartAt { get; set; }
    public TimeSpan IntervalStart { get; set; }

    /// <summary>Когда слайдшоу запущено (или изменено расписание) — отсюда считается шаг 1.</summary>
    public DateTime Epoch { get; set; }

    /// <summary>Путь устройства монитора → шаги (до <see cref="SlotCount"/>, null — пусто).</summary>
    public Dictionary<string, List<SlideshowSlot?>> Monitors { get; set; } = new();

    /// <summary>
    /// Своя длина цикла монитора (тумблер «Шагов в цикле» в окне шагов, 2–30); нет записи — длина по последнему заполненному шагу.
    /// </summary>
    public Dictionary<string, int> CycleLengths { get; set; } = new();

    /// <summary>Шаги монитора до «✕» (левый щелчок) — правый щелчок возвращает их.</summary>
    public Dictionary<string, List<SlideshowSlot?>> Cleared { get; set; } = new();
}

namespace Time2Gadget.Models;

/// <summary>
/// Какой звук и куда играть (быстрые таймеры и прослушивание, 2026-09-28). Незаданное поле (null) — из раздела «Звук»:
/// RingtoneId null — общий звонок; CustomPath — файл для RingtoneId = RingtoneCatalog.CustomId.
/// </summary>
public sealed record SoundChoice(string? RingtoneId, string? CustomPath = null, string? DeviceId = null, double? Volume = null);

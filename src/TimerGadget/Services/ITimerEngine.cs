using TimerGadget.Models;

namespace TimerGadget.Services;

/// <summary>
/// Ядро отсчёта времени. Инвариант (docs/ARCHITECTURE.md → Timer Engine):
/// источник истины — реальные DateTime-метки начала/окончания, НЕ количество тиков UI-таймера.
/// </summary>
public interface ITimerEngine
{
    TimerStatus Status { get; }
    TimeSpan TotalDuration { get; }

    /// <summary>Устанавливает длительность (заменяет). Разрешено только когда Status != Running (см. UI-CONTRACT.md).</summary>
    void SetDuration(TimeSpan duration);

    /// <summary>
    /// Добавляет delta к текущей длительности (докладка времени, docs/UI-CONTRACT.md → SectorRingControl).
    /// Работает в ЛЮБОМ статусе: Running — продлевает дедлайн на лету; Paused — увеличивает
    /// отложенный остаток; Ready/Finished — увеличивает базовую длительность.
    /// </summary>
    void AddDuration(TimeSpan delta);

    void Start();
    void Pause();

    /// <summary>
    /// Безусловный сброс на новую длительность и немедленный запуск — в отличие от SetDuration,
    /// работает и во время Running (docs/UI-CONTRACT.md → клик на ДРУГОЙ сектор во время отсчёта,
    /// докладка 2026-09-27: пользователь ожидает мгновенной замены, а не игнорирования клика).
    /// </summary>
    void Restart(TimeSpan duration);

    /// <summary>Сброс к Ready с текущим TotalDuration (выбранное время не меняется).</summary>
    void Reset();

    /// <summary>Точный расчёт оставшегося времени на момент вызова (не кэшируется).</summary>
    TimeSpan GetRemaining();

    event EventHandler? Finished;
    event EventHandler? StatusChanged;
}

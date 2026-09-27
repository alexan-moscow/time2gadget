namespace Time2Gadget.Services;

public enum UpdateCheckStatus { UpToDate, Available, Failed }

/// <summary>Итог проверки: для Available — версия и страница выпуска на GitHub.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? Version = null, string? ReleasePageUrl = null);

/// <summary>
/// Обновления из GitHub Releases (docs/DECISIONS.md, 2026-09-27). Установленная копия (Velopack) обновляется
/// на месте с перезапуском; портативный exe так не умеет — для него только проверка и ссылка на выпуск.
/// </summary>
public interface IUpdateService
{
    /// <summary>Текущая версия программы («1.0.0»).</summary>
    string CurrentVersion { get; }

    /// <summary>Установлено через Velopack — можно скачать и поставить обновление прямо из программы.</summary>
    bool CanInstallInPlace { get; }

    /// <summary>Проверка без исключений: нет сети/GitHub недоступен → Failed.</summary>
    Task<UpdateCheckResult> CheckAsync();

    /// <summary>
    /// Скачать найденное обновление, поставить и перезапустить программу (процесс завершится).
    /// Только если CanInstallInPlace и последняя проверка вернула Available.
    /// </summary>
    Task DownloadAndRestartAsync(Action<int>? progress);
}

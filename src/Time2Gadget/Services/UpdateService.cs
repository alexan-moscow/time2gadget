using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Time2Gadget.Services;

/// <inheritdoc cref="IUpdateService"/>
public sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/alexan-moscow/time2gadget";
    private const string ReleasesApi = "https://api.github.com/repos/alexan-moscow/time2gadget/releases";
    private const string LatestReleaseApi = ReleasesApi + "/latest";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    public bool CanInstallInPlace => _manager.IsInstalled;

    public string CurrentVersion =>
        (_manager.IsInstalled ? _manager.CurrentVersion?.ToString() : null) ?? AssemblyVersion();

    private static string AssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            return CanInstallInPlace ? await CheckInstalledAsync() : await CheckPortableAsync();
        }
        catch
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed); // нет сети, лимит GitHub и т.п. — молча, попробуем позже
        }
    }

    private async Task<UpdateCheckResult> CheckInstalledAsync()
    {
        _pending = await _manager.CheckForUpdatesAsync();
        if (_pending is null)
        {
            Log($"проверка: обновлений нет (сейчас {CurrentVersion})");
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }
        var version = _pending.TargetFullRelease.Version.ToString();
        if (SameVersion(version, CurrentVersion)) // защита: найденная версия равна установленной — это не обновление
        {
            Log($"проверка: найдена {version}, равна установленной {CurrentVersion} — обновлением не считаем");
            _pending = null;
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }
        Log($"проверка: сейчас {CurrentVersion}, найдена {version}");
        return new UpdateCheckResult(UpdateCheckStatus.Available, version, $"{RepoUrl}/releases/tag/v{version}");
    }

    public bool IsSameAsCurrent(string? version) => version is not null && SameVersion(version, CurrentVersion);

    private static bool SameVersion(string a, string b) =>
        Version.TryParse(a.Split('-', '+')[0], out var x) && Version.TryParse(b.Split('-', '+')[0], out var y)
            ? x == y
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // update.log рядом с настройками (папка Time2Gadget в AppData) — чтобы по необъяснённой шестерёнке
    // было видно, что и когда проверялось и ставилось
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "update.log");

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 200_000) File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch { /* журнал не должен мешать работе */ }
    }

    private HttpClient CreateGitHubClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Time2Gadget/" + CurrentVersion); // без User-Agent GitHub API отвечает 403
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private const string SetupAssetName = "Time2Gadget-win-Setup.exe";

    public async Task<string?> GetVirusTotalUrlAsync()
    {
        try
        {
            using var http = CreateGitHubClient();
            using var response = await http.GetAsync($"{ReleasesApi}/tags/v{CurrentVersion}");
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != SetupAssetName) continue;
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null; // "sha256:<hex>"
                return digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? "https://www.virustotal.com/gui/file/" + digest["sha256:".Length..]
                    : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Портативный exe: смотрим последний выпуск через GitHub API и сравниваем с версией сборки.</summary>
    private async Task<UpdateCheckResult> CheckPortableAsync()
    {
        using var http = CreateGitHubClient();
        using var response = await http.GetAsync(LatestReleaseApi);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate); // выпусков ещё нет
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var page = json.RootElement.GetProperty("html_url").GetString();

        return Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
               && Version.TryParse(CurrentVersion, out var current)
               && latest > current
            ? new UpdateCheckResult(UpdateCheckStatus.Available, tag.TrimStart('v', 'V'), page)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate);
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress)
    {
        if (!CanInstallInPlace || _pending is null) return;
        Log($"установка: {CurrentVersion} → {_pending.TargetFullRelease.Version}");
        await _manager.DownloadUpdatesAsync(_pending, progress);
        _manager.ApplyUpdatesAndRestart(_pending); // завершает процесс; Velopack ставит новую версию и запускает её
    }
}

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
    private const string LatestReleaseApi = "https://api.github.com/repos/alexan-moscow/time2gadget/releases/latest";

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
        if (_pending is null) return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        var version = _pending.TargetFullRelease.Version.ToString();
        return new UpdateCheckResult(UpdateCheckStatus.Available, version, $"{RepoUrl}/releases/tag/v{version}");
    }

    /// <summary>Портативный exe: смотрим последний выпуск через GitHub API и сравниваем с версией сборки.</summary>
    private async Task<UpdateCheckResult> CheckPortableAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Time2Gadget/" + CurrentVersion); // без User-Agent GitHub API отвечает 403
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

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
        await _manager.DownloadUpdatesAsync(_pending, progress);
        _manager.ApplyUpdatesAndRestart(_pending); // завершает процесс; Velopack ставит новую версию и запускает её
    }
}

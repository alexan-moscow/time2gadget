using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Time2Gadget.Models;

namespace Time2Gadget.Services;

/// <inheritdoc cref="ISoundService"/>
/// <remarks>
/// NAudio (WASAPI) — единственный способ в .NET реально выбрать конкретное устройство вывода
/// (System.Media/MediaPlayer всегда играют через системное устройство по умолчанию), см.
/// docs/DECISIONS.md, 2026-09-27. Встроенные звонки — mp3-ресурсы exe (Models/RingtoneCatalog),
/// распаковываются в %APPDATA%\Time2Gadget\Ringtones\. Пользовательский файл — .wav/.mp3,
/// AudioFileReader декодирует оба формата и даёт Volume-контроль.
/// </remarks>
public sealed class SoundService : ISoundService, IDisposable
{
    private static readonly string RingtoneDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "Ringtones");

    // Остановка звука посреди волны даёт слышимый щелчок (докладка 2026-09-27: переключение на новый
    // таймер во время звонка) — поэтому звук сначала быстро гасится, и только потом останавливается вывод.
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(120);
    private const int OutputLatencyMs = 100; // буфер WasapiOut — затухание должно доиграть и из него

    private readonly object _lock = new();
    private CancellationTokenSource? _ringCts;
    private WasapiOut? _currentOutput;
    private FadeInOutSampleProvider? _currentFade;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var list = new List<AudioDeviceInfo> { AudioDeviceInfo.SystemDefault };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName));
                dev.Dispose();
            }
        }
        catch
        {
            // Аудио-подсистема недоступна — вернуть хотя бы SystemDefault, не ронять приложение/настройки.
        }
        return list;
    }

    private static readonly TimeSpan GapBetweenRepeats = TimeSpan.FromMilliseconds(400);

    public void PlayAlarm(AppSettings settings, SoundChoice? choice = null)
    {
        StopAlarm();
        var path = ResolveSoundPath(settings, choice);
        if (path is null)
        {
            AlarmCompleted?.Invoke(this, EventArgs.Empty); // звонить нечем (файл пропал) — «отыграл» сразу
            return;
        }

        _ringCts = new CancellationTokenSource();
        _ = RingLoopAsync(path, choice?.DeviceId ?? settings.AudioDeviceId, choice?.Volume ?? settings.AlarmVolume,
            Math.Clamp(settings.AlarmRepeatCount, 1, 10), _ringCts.Token);
    }

    public event EventHandler<int>? PreviewEnded;
    private int _previewSeq;

    public int PlayPreview(AppSettings settings, SoundChoice? choice = null)
    {
        StopAlarm();
        var path = ResolveSoundPath(settings, choice);
        if (path is null) return 0;
        int id = Interlocked.Increment(ref _previewSeq);
        PlayOnceAsync(path, choice?.DeviceId ?? settings.AudioDeviceId, choice?.Volume ?? settings.AlarmVolume, CancellationToken.None)
            .ContinueWith(_ => PreviewEnded?.Invoke(this, id), TaskScheduler.Default);
        return id;
    }

    public TimeSpan? GetDuration(string ringtoneId, string? customPath)
    {
        var path = ResolveSoundPath(ringtoneId, customPath);
        if (path is null) return null;
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime;
        }
        catch
        {
            return null; // повреждённый/неподдерживаемый файл — длительность просто не показываем
        }
    }

    /// <summary>
    /// Останавливает звонок/превью с быстрым затуханием (без щелчка). Отмена токена только прерывает
    /// цикл повторов; само текущее проигрывание гасится здесь — ровно один раз на вывод (повторный
    /// BeginFadeOut перезапустил бы затухание с полной громкости и дал бы тот же щелчок).
    /// </summary>
    public void StopAlarm()
    {
        _ringCts?.Cancel();
        _ringCts = null;

        WasapiOut? output;
        FadeInOutSampleProvider? fade;
        lock (_lock)
        {
            output = _currentOutput;
            fade = _currentFade;
            _currentOutput = null;
            _currentFade = null;
        }
        if (output is null) return;

        try { fade?.BeginFadeOut(FadeOutDuration.TotalMilliseconds); } catch { /* уже освобождён — не критично */ }
        _ = Task.Delay(FadeOutDuration + TimeSpan.FromMilliseconds(OutputLatencyMs)).ContinueWith(_ =>
        {
            try { output.Stop(); } catch { /* уже остановлен/освобождён — не критично */ }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Ровно repeatCount повторов, НЕ бесконечно (докладка 2026-09-27 — заменяет прежнюю модель
    /// "длительность в секундах" + "интервал в мс"). Каждый повтор дожидается РЕАЛЬНОГО завершения
    /// проигрывания (через PlaybackStopped), а не гадает по таймауту — раньше при интервале короче
    /// длины звука повторы могли наложиться друг на друга.
    /// </summary>
    private async Task RingLoopAsync(string path, string deviceId, double volume, int repeatCount, CancellationToken token)
    {
        for (int i = 0; i < repeatCount && !token.IsCancellationRequested; i++)
        {
            await PlayOnceAsync(path, deviceId, volume, token);
            if (token.IsCancellationRequested || i == repeatCount - 1) break;
            try { await Task.Delay(GapBetweenRepeats, token); }
            catch (TaskCanceledException) { break; }
        }
        if (!token.IsCancellationRequested) AlarmCompleted?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? AlarmCompleted;

    private Task PlayOnceAsync(string path, string deviceId, double volume, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        if (token.IsCancellationRequested) { tcs.TrySetResult(); return tcs.Task; }
        try
        {
            var reader = new AudioFileReader(path) { Volume = (float)Math.Clamp(volume, 0, 1) };
            var fade = new FadeInOutSampleProvider(reader);
            var device = ResolveDeviceOrNull(deviceId);
            var output = device is not null
                ? new WasapiOut(device, AudioClientShareMode.Shared, true, 100)
                : new WasapiOut(AudioClientShareMode.Shared, 100);

            output.PlaybackStopped += (_, _) =>
            {
                try { reader.Dispose(); } catch { }
                try { output.Dispose(); } catch { }
                tcs.TrySetResult();
            };

            lock (_lock)
            {
                // StopAlarm отменяет токен ДО захвата lock: если отмена уже была — этот повтор не стартует,
                // иначе StopAlarm гарантированно увидит его как текущий и погасит.
                if (token.IsCancellationRequested)
                {
                    reader.Dispose();
                    output.Dispose();
                    tcs.TrySetResult();
                    return tcs.Task;
                }
                _currentOutput = output;
                _currentFade = fade;
            }

            output.Init(fade);
            output.Play();
        }
        catch
        {
            // Устройство недоступно/файл повреждён/занято другим процессом — не ронять приложение.
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    // Свои звуки — в папке программы (решение пользователя, 2026-09-27) для портативной версии. Если туда
    // нельзя писать (например, exe положили в Program Files) или программа установлена через Velopack —
    // %APPDATA%\Time2Gadget\Sounds, чтобы звук не терялся.
    private static readonly string AppSoundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
    private static readonly string FallbackSoundsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "Sounds");

    /// <summary>
    /// Установлено через Velopack: рядом с папкой программы («current») лежит Update.exe. Тогда папку
    /// программы при каждом обновлении заменяют целиком — свои звуки там пропали бы, поэтому для
    /// установленной версии они живут только в %APPDATA% (docs/DECISIONS.md, 2026-09-27).
    /// </summary>
    private static bool IsInstalledByVelopack()
    {
        var parent = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return parent is not null && File.Exists(Path.Combine(parent.FullName, "Update.exe"));
    }

    public string? ImportCustomSound(string sourcePath)
    {
        StopAlarm(); // перезапись файла, который сейчас играет, упала бы на блокировке

        var dirs = IsInstalledByVelopack() ? new[] { FallbackSoundsDir } : new[] { AppSoundsDir, FallbackSoundsDir };
        foreach (var dir in dirs)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Path.GetFileName(sourcePath));
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return dest; // выбрали уже скопированный файл

                // Свои файлы теперь бывают у каждого быстрого таймера: одноимённый файл с другим содержимым
                // не затираем (им может пользоваться другой таймер) — копия получает имя «звук (2).mp3».
                dest = FreeName(dest, sourcePath);
                File.Copy(sourcePath, dest, overwrite: true);
                return dest;
            }
            catch
            {
                // нет прав на запись / файл занят — пробуем следующую папку
            }
        }
        return null;
    }

    /// <summary>Имя для копии: то же, если файла нет или он такой же длины (та же копия); иначе «имя (N).расш».</summary>
    private static string FreeName(string dest, string source)
    {
        long size = new FileInfo(source).Length;
        var dir = Path.GetDirectoryName(dest)!;
        var name = Path.GetFileNameWithoutExtension(dest);
        var ext = Path.GetExtension(dest);
        for (int n = 2; File.Exists(dest) && new FileInfo(dest).Length != size; n++)
            dest = Path.Combine(dir, $"{name} ({n}){ext}");
        return dest;
    }

    /// <summary>Удаляет прежнюю копию — только если она лежит в НАШЕЙ папке звуков (чужие файлы не трогаем).</summary>
    public void DeleteImportedSound(string? previousPath)
    {
        if (string.IsNullOrEmpty(previousPath)) return;
        try
        {
            var prev = Path.GetFullPath(previousPath);
            var prevDir = Path.GetDirectoryName(prev);
            bool isOurs = string.Equals(prevDir, Path.GetFullPath(AppSoundsDir), StringComparison.OrdinalIgnoreCase)
                       || string.Equals(prevDir, Path.GetFullPath(FallbackSoundsDir), StringComparison.OrdinalIgnoreCase);
            if (isOurs && File.Exists(prev)) File.Delete(prev);
        }
        catch
        {
            // не удалилось — не критично, просто лишний файл в папке
        }
    }

    private static MMDevice? ResolveDeviceOrNull(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId);
        }
        catch
        {
            return null; // выбранное устройство отключено — тихий откат на системное по умолчанию
        }
    }

    private static string? ResolveSoundPath(AppSettings settings, SoundChoice? choice) =>
        choice?.RingtoneId is { } id
            ? ResolveSoundPath(id, choice.CustomPath)
            : ResolveSoundPath(settings.RingtoneId, settings.CustomSoundFilePath);

    /// <summary>
    /// Путь к файлу звонка. Встроенный — распаковывается из ресурсов exe в кэш %APPDATA%\Time2Gadget\Ringtones
    /// при первом использовании (NAudio играет с диска). Неизвестный Id (например, из старых настроек) —
    /// звонок по умолчанию, чтобы таймер не остался беззвучным.
    /// </summary>
    private static string? ResolveSoundPath(string? ringtoneId, string? customPath)
    {
        if (ringtoneId == RingtoneCatalog.CustomId)
            return !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath) ? customPath : null;

        var id = RingtoneCatalog.IsBuiltIn(ringtoneId) ? ringtoneId! : RingtoneCatalog.DefaultId;
        try
        {
            Directory.CreateDirectory(RingtoneDir);
            RemoveLegacyGeneratedRingtones();
            var cachedPath = Path.Combine(RingtoneDir, id + ".mp3");
            if (!File.Exists(cachedPath))
            {
                using var resource = typeof(SoundService).Assembly.GetManifestResourceStream($"Ringtones.{id}.mp3");
                if (resource is null) return null;
                using var file = File.Create(cachedPath);
                resource.CopyTo(file);
            }
            return cachedPath;
        }
        catch
        {
            return null; // кэш недоступен для записи — не ронять приложение (AlarmCompleted всё равно придёт)
        }
    }

    /// <summary>Удаляет из кэша .wav прежних синтезированных звонков (убраны 2026-09-27) — один раз, дальше их нет.</summary>
    private static void RemoveLegacyGeneratedRingtones()
    {
        foreach (var wav in Directory.EnumerateFiles(RingtoneDir, "*.wav"))
        {
            try { File.Delete(wav); } catch { /* занят/нет прав — не критично */ }
        }
    }

    public void Dispose() => StopAlarm();
}

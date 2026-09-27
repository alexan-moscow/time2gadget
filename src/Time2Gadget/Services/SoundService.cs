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
/// docs/DECISIONS.md, 2026-09-27. Встроенные звонки — процедурно синтезированы (RingtoneGenerator),
/// кэшируются как .wav в %APPDATA%\Time2Gadget\Ringtones\. Пользовательский файл — .wav/.mp3,
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

    public void PlayAlarm(AppSettings settings)
    {
        StopAlarm();
        var path = ResolveSoundPath(settings);
        if (path is null)
        {
            AlarmCompleted?.Invoke(this, EventArgs.Empty); // звонить нечем (файл пропал) — «отыграл» сразу
            return;
        }

        _ringCts = new CancellationTokenSource();
        _ = RingLoopAsync(path, settings.AudioDeviceId, settings.AlarmVolume,
            Math.Clamp(settings.AlarmRepeatCount, 1, 10), _ringCts.Token);
    }

    public void PlayPreview(AppSettings settings)
    {
        StopAlarm();
        var path = ResolveSoundPath(settings);
        if (path is not null) _ = PlayOnceAsync(path, settings.AudioDeviceId, settings.AlarmVolume, CancellationToken.None);
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

    // Свои звуки — в папке программы (решение пользователя, 2026-09-27): приложение портативное, всё
    // своё лежит рядом с exe. Если туда нельзя писать (например, exe положили в Program Files) —
    // откат в %APPDATA%\Time2Gadget\Sounds, чтобы выбор звука не ломался.
    private static readonly string AppSoundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
    private static readonly string FallbackSoundsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Time2Gadget", "Sounds");

    public string? ImportCustomSound(string sourcePath, string? previousImportedPath)
    {
        StopAlarm(); // перезапись файла, который сейчас играет, упала бы на блокировке

        foreach (var dir in new[] { AppSoundsDir, FallbackSoundsDir })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Path.GetFileName(sourcePath));
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, dest, overwrite: true);

                DeleteOldImport(previousImportedPath, dest);
                return dest;
            }
            catch
            {
                // нет прав на запись / файл занят — пробуем следующую папку
            }
        }
        return null;
    }

    /// <summary>Удаляет прежнюю копию — только если она лежит в НАШЕЙ папке звуков (чужие файлы не трогаем).</summary>
    private static void DeleteOldImport(string? previousPath, string newPath)
    {
        if (string.IsNullOrEmpty(previousPath)) return;
        try
        {
            var prev = Path.GetFullPath(previousPath);
            if (string.Equals(prev, Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase)) return;

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

    private static string? ResolveSoundPath(AppSettings settings)
    {
        if (settings.SelectedRingtone == RingtoneChoice.Custom)
        {
            return !string.IsNullOrWhiteSpace(settings.CustomSoundFilePath) && File.Exists(settings.CustomSoundFilePath)
                ? settings.CustomSoundFilePath
                : null;
        }

        Directory.CreateDirectory(RingtoneDir);
        var cachedPath = Path.Combine(RingtoneDir, $"{settings.SelectedRingtone}.wav");
        if (!File.Exists(cachedPath))
        {
            var bytes = RingtoneGenerator.Generate(settings.SelectedRingtone);
            File.WriteAllBytes(cachedPath, bytes);
        }
        return cachedPath;
    }

    public void Dispose() => StopAlarm();
}

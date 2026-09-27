using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimerGadget.Models;

namespace TimerGadget.Services;

/// <inheritdoc cref="ISoundService"/>
/// <remarks>
/// NAudio (WASAPI) — единственный способ в .NET реально выбрать конкретное устройство вывода
/// (System.Media/MediaPlayer всегда играют через системное устройство по умолчанию), см.
/// docs/DECISIONS.md, 2026-09-27. Встроенные звонки — процедурно синтезированы (RingtoneGenerator),
/// кэшируются как .wav в %APPDATA%\TimerGadget\Ringtones\. Пользовательский файл — .wav/.mp3,
/// AudioFileReader декодирует оба формата и даёт Volume-контроль.
/// </remarks>
public sealed class SoundService : ISoundService, IDisposable
{
    private static readonly string RingtoneDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimerGadget", "Ringtones");

    private readonly object _lock = new();
    private CancellationTokenSource? _ringCts;
    private WasapiOut? _currentOutput;
    private AudioFileReader? _currentReader;

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
        if (path is null) return;

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

    public void StopAlarm()
    {
        _ringCts?.Cancel();
        _ringCts = null;
        lock (_lock)
        {
            try { _currentOutput?.Stop(); } catch { /* уже остановлен/освобождён — не критично */ }
            _currentOutput = null;
            _currentReader = null;
        }
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
    }

    private Task PlayOnceAsync(string path, string deviceId, double volume, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        try
        {
            var reader = new AudioFileReader(path) { Volume = (float)Math.Clamp(volume, 0, 1) };
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
                _currentReader = reader;
                _currentOutput = output;
            }

            var registration = token.CanBeCanceled
                ? token.Register(() => { try { output.Stop(); } catch { } })
                : default;
            if (registration != default)
            {
                _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            output.Init(reader);
            output.Play();
        }
        catch
        {
            // Устройство недоступно/файл повреждён/занято другим процессом — не ронять приложение.
            tcs.TrySetResult();
        }
        return tcs.Task;
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

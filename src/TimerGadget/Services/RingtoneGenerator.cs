using TimerGadget.Models;

namespace TimerGadget.Services;

/// <summary>
/// Процедурный синтез 4 встроенных звонков (docs/DECISIONS.md, 2026-09-27) — без внешних
/// аудио-файлов/лицензий. Результат кэшируется в %APPDATA%\TimerGadget\Ringtones\*.wav
/// при первом обращении (см. SoundService.ResolveSoundPath).
/// </summary>
internal static class RingtoneGenerator
{
    private enum Shape { Sine, Square, Sawtooth }

    public static byte[] Generate(RingtoneChoice choice) => choice switch
    {
        RingtoneChoice.ClassicBell => BuildClassicBell(),
        RingtoneChoice.DigitalBeep => BuildDigitalBeep(),
        RingtoneChoice.SoftChime => BuildSoftChime(),
        RingtoneChoice.AlarmBuzz => BuildAlarmBuzz(),
        _ => BuildClassicBell()
    };

    private static byte[] BuildClassicBell()
    {
        var buf = new List<float>();
        AppendTone(buf, 880, 0.30, 0.7, Shape.Sine, fadeIn: 0.01, fadeOut: 0.18);
        AppendSilence(buf, 0.05);
        AppendTone(buf, 660, 0.45, 0.7, Shape.Sine, fadeIn: 0.01, fadeOut: 0.30);
        return WavEncoder.Encode(buf.ToArray());
    }

    private static byte[] BuildDigitalBeep()
    {
        var buf = new List<float>();
        for (int i = 0; i < 3; i++)
        {
            AppendTone(buf, 1000, 0.12, 0.6, Shape.Square, fadeIn: 0.004, fadeOut: 0.004);
            if (i < 2) AppendSilence(buf, 0.09);
        }
        return WavEncoder.Encode(buf.ToArray());
    }

    private static byte[] BuildSoftChime()
    {
        var buf = new List<float>();
        double[] notes = { 523.25, 659.25, 783.99 }; // C5, E5, G5
        foreach (var f in notes)
        {
            AppendTone(buf, f, 0.22, 0.55, Shape.Sine, fadeIn: 0.015, fadeOut: 0.12);
            AppendSilence(buf, 0.02);
        }
        return WavEncoder.Encode(buf.ToArray());
    }

    private static byte[] BuildAlarmBuzz()
    {
        var buf = new List<float>();
        AppendTone(buf, 440, 0.55, 0.65, Shape.Sawtooth, fadeIn: 0.005, fadeOut: 0.05, tremoloHz: 9, tremoloDepth: 0.5);
        return WavEncoder.Encode(buf.ToArray());
    }

    private static void AppendSilence(List<float> buf, double seconds)
    {
        int n = (int)(seconds * WavEncoder.SampleRate);
        buf.AddRange(new float[n]);
    }

    private static void AppendTone(List<float> buf, double freqHz, double durationSec, double amplitude, Shape shape,
        double fadeIn, double fadeOut, double tremoloHz = 0, double tremoloDepth = 0)
    {
        int n = (int)(durationSec * WavEncoder.SampleRate);
        int fadeInSamples = Math.Max(1, (int)(fadeIn * WavEncoder.SampleRate));
        int fadeOutSamples = Math.Max(1, (int)(fadeOut * WavEncoder.SampleRate));

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)WavEncoder.SampleRate;
            double phase = 2 * Math.PI * freqHz * t;

            double raw = shape switch
            {
                Shape.Sine => Math.Sin(phase),
                Shape.Square => Math.Sign(Math.Sin(phase)),
                Shape.Sawtooth => 2.0 * (freqHz * t - Math.Floor(0.5 + freqHz * t)),
                _ => 0
            };

            double envelope = 1.0;
            if (i < fadeInSamples) envelope = i / (double)fadeInSamples;
            else if (i > n - fadeOutSamples) envelope = Math.Max(0, (n - i) / (double)fadeOutSamples);

            double tremolo = tremoloHz > 0
                ? 1.0 - tremoloDepth * (0.5 + 0.5 * Math.Sin(2 * Math.PI * tremoloHz * t))
                : 1.0;

            buf.Add((float)(raw * amplitude * envelope * tremolo));
        }
    }
}

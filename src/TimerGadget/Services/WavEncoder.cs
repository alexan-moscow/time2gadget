using System.IO;

namespace TimerGadget.Services;

/// <summary>Минимальный кодировщик 16-бит PCM mono WAV из float-сэмплов (-1..1). Без внешних библиотек.</summary>
internal static class WavEncoder
{
    public const int SampleRate = 44100;

    public static byte[] Encode(float[] samples)
    {
        int dataSize = samples.Length * 2;
        int fileSize = 36 + dataSize;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write("RIFF"u8.ToArray());
        w.Write(fileSize);
        w.Write("WAVE"u8.ToArray());

        w.Write("fmt "u8.ToArray());
        w.Write(16);                 // fmt chunk size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);     // byte rate = sampleRate * blockAlign
        w.Write((short)2);           // block align (16-bit mono = 2 bytes)
        w.Write((short)16);          // bits per sample

        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        foreach (var s in samples)
        {
            var clamped = Math.Clamp(s, -1f, 1f);
            w.Write((short)(clamped * short.MaxValue));
        }

        return ms.ToArray();
    }
}

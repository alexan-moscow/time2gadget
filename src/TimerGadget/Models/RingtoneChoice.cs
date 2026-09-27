namespace TimerGadget.Models;

/// <summary>
/// 4 встроенных звонка (процедурно синтезируются — см. Services/RingtoneGenerator.cs,
/// без внешних аудио-файлов/лицензий) + пользовательский файл.
/// </summary>
public enum RingtoneChoice
{
    ClassicBell,   // традиционный двухтональный "дин-дон"
    DigitalBeep,   // современный цифровой бип-бип-бип
    SoftChime,     // мягкий восходящий перезвон (ксилофон-подобный)
    AlarmBuzz,     // резкий будильник-зуммер
    Custom         // пользовательский .wav/.mp3 — см. AppSettings.CustomSoundFilePath
}

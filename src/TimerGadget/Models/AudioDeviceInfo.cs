namespace TimerGadget.Models;

/// <summary>Устройство вывода звука для выбора в настройках (docs/ARCHITECTURE.md → Sound).</summary>
public sealed record AudioDeviceInfo(string Id, string FriendlyName)
{
    /// <summary>Псевдо-устройство "по умолчанию" — Id=null означает "системное устройство по умолчанию".</summary>
    public static readonly AudioDeviceInfo SystemDefault = new(string.Empty, "Системное устройство по умолчанию");
}

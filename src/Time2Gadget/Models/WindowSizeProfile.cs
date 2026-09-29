namespace Time2Gadget.Models;

/// <summary>
/// Профиль размера окна (докладка 2026-09-29): положение, размер и рамка — не привязан к программе, применяется к любому
/// окну; программам назначается через <see cref="WindowProfileAssignment"/>. Координаты — физические пиксели экрана.
/// </summary>
public sealed class WindowSizeProfile
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Снять с окна заголовок и рамку (как «Remove borders» в Simple Runtime Window Editor).</summary>
    public bool Borderless { get; set; } = true;

    /// <summary>
    /// Послать окну сигнал «размер изменён» (WM_EXITSIZEMOVE, как «Force EXITSIZEMOVE» в SRWE): многие игры перестраивают
    /// картинку только по нему — при программной смене размера Windows его не шлёт.
    /// </summary>
    public bool NotifyResize { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>Программа (ключ «exe + вид окна», Services/NativeWindows.MakeProgramKey) → профиль, применяемый автоматически.</summary>
public sealed class WindowProfileAssignment
{
    public string ProgramKey { get; set; } = string.Empty;
    /// <summary>Имя exe — для показа в списке.</summary>
    public string ProgramName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
}

namespace Time2Gadget.Models;

/// <summary>Запомненное место окна (докладка 2026-10-01); Width/Height = 0 — размер не запоминается.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
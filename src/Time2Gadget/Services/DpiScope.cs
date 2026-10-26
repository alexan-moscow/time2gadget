using System.Runtime.InteropServices;

namespace Time2Gadget.Services;

/// <summary>
/// Настоящие координаты чужих окон (докладка 2026-10-26). Сама программа работает в режиме «один масштаб на систему»
/// (WPF без dpiAwareness в манифесте; ApplicationHighDpiMode из .csproj действует только на WinForms). В этом режиме Windows
/// отдаёт и принимает координаты окон на мониторе с другим масштабом пересчитанными: окна на стыке мониторов при возврате
/// росли ровно в масштаб верхнего монитора (Telegram, Chrome ×1,25), а GetDpiForMonitor сообщал всем мониторам один масштаб.
/// На время работы с чужими окнами поток переключается в «свой масштаб у каждого монитора» (Per-Monitor V2) и обратно.
/// </summary>
internal readonly struct DpiScope : IDisposable
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private readonly IntPtr _previous;

    private DpiScope(IntPtr previous) => _previous = previous;

    public static DpiScope PerMonitor()
    {
        try { return new DpiScope(SetThreadDpiAwarenessContext(PerMonitorAwareV2)); }
        catch (EntryPointNotFoundException) { return default; } // Windows 10 до 1703 — как раньше
    }

    public void Dispose()
    {
        if (_previous != IntPtr.Zero) SetThreadDpiAwarenessContext(_previous);
    }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}

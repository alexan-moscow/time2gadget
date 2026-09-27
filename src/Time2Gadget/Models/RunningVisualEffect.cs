namespace Time2Gadget.Models;

/// <summary>Визуальный эффект циферблата во время отсчёта (docs/UI-CONTRACT.md). По умолчанию None.</summary>
public enum RunningVisualEffect
{
    None,
    Pulse,        // мягкая пульсация масштаба
    Flash,        // периодическая вспышка акцентом
    ColorBreathe  // медленное "дыхание" цвета акцента
}

# COMPONENTS.md

Реестр переиспользуемых компонентов (аналог COMPONENTS.md в КЛЮЧ24). Перед добавлением нового визуального компонента — сверяться сюда, не дублировать.

| Компонент | Файл | Назначение | Переиспользуется в |
|---|---|---|---|
| `SectorRingControl` | `Controls/SectorRingControl.xaml(.cs)` | Кольцо из 8 кликабельных секторов-пресетов | `MainWindow` (Normal mode only) |
| `ProgressRingControl` | `Controls/ProgressRingControl.xaml(.cs)` | Тонкое кольцо прогресса отсчёта | `MainWindow` (Normal + Compact) |
| `CircleButtonStyle` | `Resources/Theme.xaml` (ControlTemplate) | Круглая кнопка (Mute/Start-Pause/Reset) | `MainWindow` (Normal + Compact) |
| `TimeSpanToStringConverter` | `Converters/TimeSpanToStringConverter.cs` | Форматирование `TimeSpan` → `MM:SS` | Биндинги центрального дисплея |

Правила:
- Новая круглая кнопка — всегда `CircleButtonStyle`, не отдельный `ControlTemplate`.
- Геометрия donut-сегментов (использована и в `SectorRingControl`, и в `ProgressRingControl`) — общий приватный helper `GeometryHelper.CreateDonutSegment(...)`, не дублировать формулу арки в двух местах.

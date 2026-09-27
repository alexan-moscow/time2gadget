# ARCHITECTURE.md

Технический контракт проекта. Аналог ARCHITECTURE.md в КЛЮЧ24, адаптирован под desktop/WPF.

## Структура решения

```
TimerGadget.sln
src/TimerGadget/
  TimerGadget.csproj        # net8.0-windows, UseWPF=true, UseWindowsForms=true (для трея)
  App.xaml / App.xaml.cs    # entry point, DI-контейнер не используется (проект мал) — простая
                             # ручная композиция сервисов в App.xaml.cs
  Views/
    MainWindow.xaml/.cs      # оболочка окна: chrome, drag, layout Normal/Compact, контекстное меню
  ViewModels/
    MainViewModel.cs         # единственная ViewModel — вся презентационная логика
    RelayCommand.cs          # минимальная ICommand-реализация
  Models/
    TimerPreset.cs           # {Minutes, Label}
    TimerStatus.cs           # enum Ready|Running|Paused|Finished
    AppSettings.cs           # POCO для JSON-персистентности
  Services/
    ITimerEngine.cs / TimerEngine.cs
    ISettingsService.cs / SettingsService.cs
    ITrayService.cs / TrayService.cs
    ISoundService.cs / SoundService.cs
    AutostartService.cs
  Controls/
    SectorRingControl.xaml/.cs   # 8-секторное кольцо выбора + клики
    ProgressRingControl.xaml/.cs # тонкое кольцо прогресса
  Converters/
    TimeSpanToStringConverter.cs
  Resources/
    Theme.xaml                # цвета/типографика/анимации (см. DESIGN-SYSTEM.md)
  Assets/
    app.ico
```

Каждый Service имеет интерфейс — не ради DI-фреймворка (его нет, проект мал), а чтобы `MainViewModel` не зависела от конкретной реализации напрямую (тестируемость/замена реализации позже).

## MVVM-слои и границы ответственности

- **View** (`MainWindow.xaml`, `*Control.xaml`) — только разметка + чисто визуальные code-behind вещи (drag-move окна, построение геометрии секторов). НЕ содержит бизнес-логики таймера/настроек.
- **ViewModel** (`MainViewModel`) — единственный источник состояния UI: `RemainingDisplay`, `StatusLabel`, `SelectedPreset`, `Presets`, `ProgressFraction`, `IsMuted`, `IsAlwaysOnTop`, `IsCompactMode`. Все команды (`SelectPresetCommand`, `StartPauseCommand`, `ResetCommand`, `ToggleMuteCommand`, `AdjustTimeCommand`, `ToggleAlwaysOnTopCommand`, `ToggleCompactModeCommand`, `ExitCommand`) живут здесь, не в code-behind окна.
- **Model/Services** — `TimerEngine` (см. ниже), `SettingsService`, `TrayService`, `SoundService`, `AutostartService`. Не знают о WPF-конкретике `MainWindow` (кроме `TrayService`, которому нужна ссылка на `Window` для показа/скрытия — принимается через интерфейс, не через прямую зависимость типа `MainWindow`).

## Timer Engine — точность отсчёта (критическое требование, BRIEF.md §19)

**Инвариант**: отсчёт НЕ считается количеством тиков `DispatcherTimer`. Источник истины — реальное время:

```csharp
DateTime? _endTimeUtc;       // вычисляется при Start()/Resume()
TimeSpan _pausedRemaining;   // сохраняется при Pause()

void Start() {
    var baseRemaining = Status == Paused ? _pausedRemaining : TotalDuration;
    _endTimeUtc = DateTime.UtcNow + baseRemaining;
    Status = Running;
}

void Pause() {
    _pausedRemaining = _endTimeUtc.Value - DateTime.UtcNow;
    if (_pausedRemaining < TimeSpan.Zero) _pausedRemaining = TimeSpan.Zero;
    Status = Paused;
}

// вызывается UI-таймером (DispatcherTimer, интервал ~100-200ms — только для ОБНОВЛЕНИЯ ОТОБРАЖЕНИЯ,
// не для арифметики отсчёта):
TimeSpan GetRemaining() =>
    Status == Running
        ? Max(_endTimeUtc.Value - DateTime.UtcNow, TimeSpan.Zero)
        : Status == Paused ? _pausedRemaining : TotalDuration;
```

Это гарантирует, что задержки/подвисания UI-потока не накапливают ошибку — при возврате к активному окну оставшееся время всегда корректно относительно реального времени. `TimerEngine` поднимает событие `Finished`, когда `GetRemaining() == TimeSpan.Zero` впервые обнаружен (проверка на каждом UI-тике, не отдельным таймером).

`ProgressFraction` в `MainViewModel` вычисляется как `1 - GetRemaining() / TotalDuration` — потребляется `ProgressRingControl`.

## Settings — персистентность

Файл: `%APPDATA%\TimerGadget\settings.json`, `System.Text.Json`, без внешних зависимостей и без БД.

```csharp
public class AppSettings {
    public int LastPresetMinutes { get; set; } = 30;
    public bool IsMuted { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool LaunchAtStartup { get; set; } = false;
}
```

`SettingsService.Load()` — читает файл, при отсутствии/ошибке парсинга возвращает `new AppSettings()` (defaults), не бросает исключение наружу. `Save()` — сериализует, создаёт директорию при необходимости.

## Autostart

`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`, значение `TimerGadget` = путь к исполняемому файлу (`Process.GetCurrentProcess().MainModule.FileName`). Включение/выключение — запись/удаление значения. Штатный Windows-механизм, никаких Task Scheduler/сторонних библиотек.

## Tray

`System.Windows.Forms.NotifyIcon` (требует `UseWindowsForms=true` в `.csproj` — WPF не имеет собственного tray API, это стандартный лёгкий подход, НЕ дополнительный тяжёлый framework). `ContextMenuStrip` строится программно в `TrayService`, привязан к тем же командам `MainViewModel`, что и UI (без дублирования логики).

## Sound

MVP: `System.Media.SystemSounds.Asterisk.Play()` (гарантированно доступно, без бандла аудио-файлов). Точка расширения на будущее — `ISoundService` допускает замену на воспроизведение собственного `.wav`-ассета из `Assets/` без изменения вызывающего кода.

## Hotkeys

MVP: локальные для окна — `Window.InputBindings` (Space/R/Esc) → команды `MainViewModel`. Точка расширения: если понадобятся глобальные (system-wide) хоткеи — добавляется отдельный `IGlobalHotkeyService` (P/Invoke `RegisterHotKey`), не переписывая `MainViewModel`/команды — те остаются общими для локального и будущего глобального пути.

## Normal / Compact — единственная адаптивная ось

В отличие от responsive-веб (несколько независимых breakpoint'ов в КЛЮЧ24), здесь всего два дискретных режима отображения, оба управляются одним and тем же `MainViewModel.IsCompactMode`:

| Режим | Размер окна | `SectorRingControl` | `ProgressRingControl` | Кнопки |
|---|---|---|---|---|
| Normal | 320×320 | Visible | Visible (радиус см. DESIGN-SYSTEM.md) | Visible |
| Compact | 200×200 | Collapsed | Visible (уменьшенный радиус) | Visible |

Переключение — не пересоздание окна/ViewModel, а изменение `Width`/`Height`/`Visibility` с анимацией (`Anim.ModeSwitch`).

## Сборка / Packaging

Portable-сборка — `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` (не self-contained по умолчанию — меньше размер, требует .NET 8 Desktop Runtime на целевой машине; self-contained вариант — точка расширения, не MVP-обязательство). Итоговый `.exe` — единственный файл + настройки создаются рядом в `%APPDATA%`, установщик не требуется.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Строка списка окон: иконка программы, exe, заголовок.</summary>
public sealed class WindowItem
{
    public WindowItem(WindowInfo info, ImageSource? icon)
    {
        Info = info;
        Icon = icon;
    }

    public WindowInfo Info { get; }
    public ImageSource? Icon { get; }
    public string ExeName => Info.ExeName;
    public string Title => string.IsNullOrEmpty(Info.Title) ? $"(без заголовка, {Info.ClassName})" : Info.Title;
}

/// <summary>Монитор для быстрых кнопок: «1 — 3840×1080 (основной)».</summary>
public sealed record MonitorOption(int Index, System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea, string Label);

/// <summary>
/// Окно «Профили размера окон» (докладка 2026-09-29, по образцу Simple Runtime Window Editor): выбрать окно другой программы
/// (из списка или «Указать окно» — для игр, спрятанных за другим процессом), задать положение/размер/рамку вручную или
/// быстрыми кнопками, сохранить как профиль, применить профиль, назначить профиль программе для автоприменения.
/// </summary>
public sealed class WindowProfilesViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _pickTimer;
    private int _pickCountdown;

    public WindowProfilesViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var p in main.Settings.WindowProfiles) Profiles.Add(p);
        RefreshMonitors();
        RefreshWindows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    // ---------------- Окна ----------------

    public ObservableCollection<WindowItem> Windows { get; } = new();

    private bool _showAllWindows;
    /// <summary>Снять фильтры списка (окна без заголовка, служебные, с владельцем) — для «хитрых» игр и движков.</summary>
    public bool ShowAllWindows
    {
        get => _showAllWindows;
        set { Set(ref _showAllWindows, value); RefreshWindows(); }
    }

    public RelayCommand RefreshCommand => _refreshCommand ??= new RelayCommand(() => { RefreshMonitors(); RefreshWindows(); });
    private RelayCommand? _refreshCommand;

    private void RefreshWindows()
    {
        var selected = SelectedWindow?.Info.Handle;
        Windows.Clear();
        foreach (var info in NativeWindows.EnumerateWindows(ShowAllWindows)
                     .OrderBy(w => w.ExeName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title))
            Windows.Add(new WindowItem(info, IconOf(info.ExePath)));
        SelectedWindow = Windows.FirstOrDefault(w => w.Info.Handle == selected);
    }

    private ImageSource? IconOf(string? exePath)
    {
        if (exePath is null) return null;
        if (_iconCache.TryGetValue(exePath, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
                image.Freeze();
            }
        }
        catch
        {
            // нет доступа к exe — без иконки
        }
        return _iconCache[exePath] = image;
    }

    private WindowItem? _selectedWindow;
    public WindowItem? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            Set(ref _selectedWindow, value);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedHeader));
            if (value is not null) TakeFromWindow(silent: true);
            RefreshAssignment();
        }
    }

    public bool HasSelection => SelectedWindow is not null;
    public string SelectedHeader => SelectedWindow is { } w ? $"{w.ExeName} — {w.Title}" : "Выберите окно слева или нажмите «Указать окно»";

    private string _currentText = string.Empty;
    public string CurrentText { get => _currentText; private set => Set(ref _currentText, value); }

    // «Указать окно»: отсчёт 3 с, за это время переключиться в нужное окно (Alt+Tab) — берём активное окно.
    // Для игр, которых нет в списке (спрятаны за другим процессом или движком).
    private string _pickButtonText = "Указать окно ⌖";
    public string PickButtonText { get => _pickButtonText; private set => Set(ref _pickButtonText, value); }

    public RelayCommand PickWindowCommand => _pickWindowCommand ??= new RelayCommand(StartPick);
    private RelayCommand? _pickWindowCommand;

    private void StartPick()
    {
        if (_pickTimer?.IsEnabled == true) return;
        _pickCountdown = 3;
        PickButtonText = $"Переключитесь в окно… {_pickCountdown}";
        _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pickTimer.Tick += (_, _) =>
        {
            if (--_pickCountdown > 0) { PickButtonText = $"Переключитесь в окно… {_pickCountdown}"; return; }
            _pickTimer!.Stop();
            PickButtonText = "Указать окно ⌖";
            PickForeground();
        };
        _pickTimer.Start();
    }

    private void PickForeground()
    {
        var info = NativeWindows.Describe(NativeWindows.ForegroundWindow);
        if (info is null || info.ProcessId == (uint)Environment.ProcessId)
        {
            Status = "Активным осталось окно Тайм2гаджета — нажмите «Указать окно» и за 3 секунды переключитесь в нужное окно (Alt+Tab).";
            return;
        }
        var item = Windows.FirstOrDefault(w => w.Info.Handle == info.Handle);
        if (item is null)
        {
            item = new WindowItem(info, IconOf(info.ExePath));
            Windows.Insert(0, item);
        }
        SelectedWindow = item;
        Status = $"Выбрано: {item.ExeName}";
    }

    // ---------------- Положение и размер ----------------

    private int _x, _y, _width, _height;
    public int X { get => _x; set => Set(ref _x, value); }
    public int Y { get => _y; set => Set(ref _y, value); }
    public int Width { get => _width; set => Set(ref _width, Math.Max(value, 50)); }
    public int Height { get => _height; set => Set(ref _height, Math.Max(value, 50)); }

    private bool _borderless = true;
    public bool Borderless { get => _borderless; set => Set(ref _borderless, value); }

    private bool _notifyResize = true;
    public bool NotifyResize { get => _notifyResize; set => Set(ref _notifyResize, value); }

    private string _status = string.Empty;
    public string Status { get => _status; private set => Set(ref _status, value); }

    public RelayCommand TakeFromWindowCommand => _takeFromWindowCommand ??= new RelayCommand(() => TakeFromWindow(silent: false));
    private RelayCommand? _takeFromWindowCommand;

    /// <summary>Поля — по тому, как окно стоит сейчас (например, выставленное SRWE — и сохранить как профиль).</summary>
    private void TakeFromWindow(bool silent)
    {
        if (SelectedWindow is not { } w || NativeWindows.GetBounds(w.Info.Handle) is not { } b)
        {
            CurrentText = "Окно закрыто — обновите список";
            return;
        }
        (X, Y, Width, Height, Borderless) = (b.X, b.Y, b.Width, b.Height, b.Borderless);
        SelectedMonitor = Monitors.ElementAtOrDefault(NativeWindows.MonitorIndexOf(b)) ?? SelectedMonitor;
        UpdateCurrentText(b);
        if (!silent) Status = "Поля заполнены по текущему окну";
    }

    private void UpdateCurrentText(WindowBounds b) =>
        CurrentText = $"Сейчас: {b.Width}×{b.Height} в ({b.X}, {b.Y}), {(b.Borderless ? "без рамки" : "с рамкой")}";

    public RelayCommand ApplyCommand => _applyCommand ??= new RelayCommand(Apply);
    private RelayCommand? _applyCommand;

    private void Apply()
    {
        if (SelectedWindow is not { } w) return;
        if (!NativeWindows.IsAlive(w.Info.Handle)) { Status = "Окно закрыто — обновите список"; return; }
        bool ok = NativeWindows.Apply(w.Info.Handle, new WindowBounds(X, Y, Width, Height, Borderless), NotifyResize);
        Status = ok
            ? "Применено"
            : "Не удалось — окно программы, запущенной от администратора? Включите «Запускать с правами администратора» в настройках.";
        if (NativeWindows.GetBounds(w.Info.Handle) is { } now) UpdateCurrentText(now);
    }

    // ---------------- Быстрые кнопки ----------------

    public ObservableCollection<MonitorOption> Monitors { get; } = new();

    private MonitorOption? _selectedMonitor;
    public MonitorOption? SelectedMonitor { get => _selectedMonitor; set => Set(ref _selectedMonitor, value); }

    private void RefreshMonitors()
    {
        int selected = SelectedMonitor?.Index ?? 0;
        Monitors.Clear();
        var list = NativeWindows.Monitors();
        for (int i = 0; i < list.Count; i++)
        {
            var (bounds, work, primary) = list[i];
            Monitors.Add(new MonitorOption(i, bounds, work, $"{i + 1} — {bounds.Width}×{bounds.Height}{(primary ? " (основной)" : "")}"));
        }
        SelectedMonitor = Monitors.ElementAtOrDefault(selected) ?? Monitors.FirstOrDefault();
    }

    /// <summary>
    /// Прижать окно к углу/стороне рабочей области выбранного монитора (без панели задач) при текущем размере; «во всю
    /// высоту слева/справа» и «во весь монитор» меняют и размер. Применяется сразу, как в SRWE.
    /// </summary>
    public RelayCommand AlignCommand => _alignCommand ??= new RelayCommand(p => Align(p as string ?? "C"));
    private RelayCommand? _alignCommand;

    private void Align(string where)
    {
        if (SelectedMonitor is not { } m) return;
        var a = m.WorkArea;
        switch (where)
        {
            case "FullLeft": Height = a.Height; break;
            case "FullRight": Height = a.Height; break;
            case "Full": Width = a.Width; Height = a.Height; break;
        }
        int left = a.Left, right = a.Right - Width, hCenter = a.Left + (a.Width - Width) / 2;
        int top = a.Top, bottom = a.Bottom - Height, vCenter = a.Top + (a.Height - Height) / 2;
        (X, Y) = where switch
        {
            "TL" => (left, top), "T" => (hCenter, top), "TR" => (right, top),
            "L" => (left, vCenter), "C" => (hCenter, vCenter), "R" => (right, vCenter),
            "BL" => (left, bottom), "B" => (hCenter, bottom), "BR" => (right, bottom),
            "FullLeft" => (left, top), "FullRight" => (right, top), "Full" => (left, top),
            _ => (X, Y)
        };
        Apply();
    }

    // ---------------- Профили ----------------

    public ObservableCollection<WindowSizeProfile> Profiles { get; } = new();

    private WindowSizeProfile? _selectedProfile;
    public WindowSizeProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { Set(ref _selectedProfile, value); OnPropertyChanged(nameof(HasProfile)); RefreshAssignment(); }
    }

    public bool HasProfile => SelectedProfile is not null;

    private string _newProfileName = string.Empty;
    public string NewProfileName { get => _newProfileName; set => Set(ref _newProfileName, value); }

    public RelayCommand SaveProfileCommand => _saveProfileCommand ??= new RelayCommand(SaveProfile);
    private RelayCommand? _saveProfileCommand;

    /// <summary>Сохранить поля как профиль; то же имя — перезаписать.</summary>
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0) { Status = "Введите имя профиля"; return; }
        var profile = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (profile is null)
        {
            profile = new WindowSizeProfile { Name = name };
            Profiles.Add(profile);
            _main.Settings.WindowProfiles.Add(profile);
        }
        (profile.X, profile.Y, profile.Width, profile.Height, profile.Borderless, profile.NotifyResize) = (X, Y, Width, Height, Borderless, NotifyResize);
        SaveAndNotify();
        SelectedProfile = null;
        SelectedProfile = profile;
        NewProfileName = string.Empty;
        Status = $"Профиль «{name}» сохранён";
    }

    public RelayCommand ApplyProfileCommand => _applyProfileCommand ??= new RelayCommand(() =>
    {
        if (SelectedProfile is not { } p) return;
        (X, Y, Width, Height, Borderless, NotifyResize) = (p.X, p.Y, p.Width, p.Height, p.Borderless, p.NotifyResize);
        Apply();
    });
    private RelayCommand? _applyProfileCommand;

    public RelayCommand DeleteProfileCommand => _deleteProfileCommand ??= new RelayCommand(() =>
    {
        if (SelectedProfile is not { } p) return;
        Profiles.Remove(p);
        _main.Settings.WindowProfiles.Remove(p);
        _main.Settings.WindowProfileAssignments.RemoveAll(a => a.ProfileName == p.Name);
        SaveAndNotify();
        SelectedProfile = null;
        Status = $"Профиль «{p.Name}» удалён";
    });
    private RelayCommand? _deleteProfileCommand;

    // ---------------- Автоприменение ----------------

    /// <summary>Выбранный профиль применяется к программе выбранного окна автоматически (при каждом её запуске и если она сама поменяет размер).</summary>
    public bool AutoApply
    {
        get => SelectedWindow?.Info.ProgramKey is { } key && SelectedProfile is { } p
               && _main.Settings.WindowProfileAssignments.Any(a => a.ProgramKey == key && a.ProfileName == p.Name);
        set
        {
            if (SelectedWindow?.Info is not { ProgramKey: { } key } info) return;
            _main.Settings.WindowProfileAssignments.RemoveAll(a => a.ProgramKey == key);
            if (value && SelectedProfile is { } p)
                _main.Settings.WindowProfileAssignments.Add(new WindowProfileAssignment { ProgramKey = key, ProgramName = info.ExeName, ProfileName = p.Name });
            SaveAndNotify();
            RefreshAssignment();
            Status = value ? $"«{SelectedProfile?.Name}» будет применяться к {info.ExeName} автоматически" : $"Автоприменение для {info.ExeName} выключено";
        }
    }

    /// <summary>Какой профиль сейчас назначен программе выбранного окна (строка под галочкой).</summary>
    public string AssignmentText =>
        SelectedWindow?.Info.ProgramKey is { } key && _main.Settings.WindowProfileAssignments.FirstOrDefault(a => a.ProgramKey == key) is { } a
            ? $"Этой программе назначен профиль «{a.ProfileName}»"
            : "Этой программе профиль не назначен";

    private void RefreshAssignment()
    {
        OnPropertyChanged(nameof(AutoApply));
        OnPropertyChanged(nameof(AssignmentText));
    }

    private void SaveAndNotify()
    {
        _main.SaveSettings();
        _main.RaiseWindowProfilesChanged();
    }
}

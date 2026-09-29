using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Строка списка окон: иконка программы, exe, заголовок и метка «профиль», если к программе он применяется постоянно.</summary>
public sealed class WindowItem : INotifyPropertyChanged
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

    private string? _permanentProfile;
    /// <summary>Имя профиля, постоянно применяемого к программе окна; null — нет (метки нет).</summary>
    public string? PermanentProfile
    {
        get => _permanentProfile;
        set
        {
            if (_permanentProfile == value) return;
            _permanentProfile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PermanentProfile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPermanentProfile)));
        }
    }
    public bool HasPermanentProfile => PermanentProfile is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Монитор для быстрых кнопок: «2 — 3840×1080, основной».</summary>
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
        RefreshWindowMarks();
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
            // Статус — последний этого окна (или пусто), а не того, что было выбрано до него.
            _status = value is not null && _statuses.TryGetValue(value.Info.Handle, out var s) ? s : string.Empty;
            OnPropertyChanged(nameof(Status));
            if (value is not null) TakeFromWindow(silent: true);
            // У программы есть постоянный профиль — выбрать его; нет — выбор в списке не сбрасывается (докладка 2026-09-29:
            // при переходе по окнам профиль «слетал» — а удобно применить один профиль к нескольким окнам подряд).
            if (value?.Info.ProgramKey is { } key
                && _main.Settings.WindowProfileAssignments.FirstOrDefault(a => a.ProgramKey == key) is { } a
                && Profiles.FirstOrDefault(p => p.Name == a.ProfileName) is { } assigned)
                SelectedProfile = assigned;
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
            RefreshWindowMarks();
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

    private bool _confineCursor;
    /// <summary>Не выпускать указатель мыши из окна, пока оно активно (Services/CursorConfineService).</summary>
    public bool ConfineCursor { get => _confineCursor; set => Set(ref _confineCursor, value); }

    /// <summary>Глобальная клавиша «указатель свободен / снова в окне» — общая, хранится в настройках программы.</summary>
    public Models.HotkeyBinding CursorConfineKey
    {
        get => _main.CursorConfineKey;
        set { _main.CursorConfineKey = value; OnPropertyChanged(); }
    }

    // Строка статуса — своя у каждого окна (докладка 2026-09-29: «Применено» оставалось при выборе другого окна).
    private readonly Dictionary<IntPtr, string> _statuses = new();
    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set
        {
            Set(ref _status, value);
            if (SelectedWindow is { } w) _statuses[w.Info.Handle] = value;
        }
    }

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
        ConfineCursor = _main.CursorConfine?.IsConfined(w.Info.Handle) == true;
        SetMonitorSilently(Monitors.ElementAtOrDefault(NativeWindows.MonitorIndexOf(b)) ?? SelectedMonitor);
        UpdateCurrentText(b);
        if (!silent) Status = "Поля заполнены по текущему окну";
    }

    private void UpdateCurrentText(WindowBounds b) =>
        CurrentText = $"Сейчас: {b.Width}×{b.Height} в ({b.X}, {b.Y}), {(b.Borderless ? "без рамки" : "с рамкой")}, монитор {NativeWindows.MonitorIndexOf(b) + 1}{(SelectedWindow is { } sw && NativeWindows.IsMinimized(sw.Info.Handle) ? " (свёрнуто)" : "")}";

    public RelayCommand ApplyCommand => _applyCommand ??= new RelayCommand(Apply);
    private RelayCommand? _applyCommand;

    private void Apply()
    {
        if (SelectedWindow is not { } w) return;
        if (!NativeWindows.IsAlive(w.Info.Handle)) { Status = "Окно закрыто — обновите список"; return; }
        var target = new WindowBounds(X, Y, Width, Height, Borderless);
        var released = AssignedProfile?.Name;
        bool autoOff = ReleaseAutoApplyForManualChange(target); // до применения — иначе служба вернула бы окно по профилю
        bool ok = NativeWindows.Apply(w.Info.Handle, target, NotifyResize);
        _main.SetCursorConfine(w.Info.Handle, ConfineCursor);
        Status = !ok
            ? "Не удалось — окно программы, запущенной от администратора? Включите «Запускать с правами администратора» в настройках."
            : autoOff
                ? $"Применено. Автоприменение «{released}» выключено: окно изменено вручную."
                : "Применено";
        if (NativeWindows.GetBounds(w.Info.Handle) is { } now) UpdateCurrentText(now);
    }

    // ---------------- Быстрые кнопки ----------------

    public ObservableCollection<MonitorOption> Monitors { get; } = new();

    private MonitorOption? _selectedMonitor;
    private bool _silentMonitorChange;

    /// <summary>
    /// Монитор для быстрых кнопок. Выбор другого монитора пользователем сразу переносит окно туда (докладка 2026-09-29):
    /// то же положение относительно угла рабочей области и тот же размер, не выходя за край.
    /// </summary>
    public MonitorOption? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (Equals(_selectedMonitor, value)) return;
            var old = _selectedMonitor;
            Set(ref _selectedMonitor, value);
            if (!_silentMonitorChange && old is not null && value is not null) MoveToMonitor(old, value);
        }
    }

    private void SetMonitorSilently(MonitorOption? monitor)
    {
        _silentMonitorChange = true;
        SelectedMonitor = monitor;
        _silentMonitorChange = false;
    }

    private void MoveToMonitor(MonitorOption from, MonitorOption to)
    {
        if (SelectedWindow is null) return;
        var (a, b) = (from.WorkArea, to.WorkArea);
        int x = b.Left + (X - a.Left), y = b.Top + (Y - a.Top);
        if (Width <= b.Width) x = Math.Clamp(x, b.Left, b.Right - Width);
        if (Height <= b.Height) y = Math.Clamp(y, b.Top, b.Bottom - Height);
        (X, Y) = (x, y);
        Apply();
    }

    private void RefreshMonitors()
    {
        int selected = SelectedMonitor?.Index ?? 0;
        Monitors.Clear();
        var list = NativeWindows.Monitors();
        for (int i = 0; i < list.Count; i++)
        {
            var (bounds, work, primary) = list[i];
            Monitors.Add(new MonitorOption(i, bounds, work, $"{i + 1} — {bounds.Width}×{bounds.Height}{(primary ? ", основной" : "")}"));
        }
        SetMonitorSilently(Monitors.ElementAtOrDefault(selected) ?? Monitors.FirstOrDefault());
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

    /// <summary>Сохранённые профили. Выбранный — «с каким профилем работаю»: применить, «Применять постоянно», удалить.</summary>
    public ObservableCollection<WindowSizeProfile> Profiles { get; } = new();

    private WindowSizeProfile? _selectedProfile;
    public WindowSizeProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            Set(ref _selectedProfile, value);
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfilePlaceholder));
            RefreshSaveButtons();
            RefreshAssignment();
        }
    }

    public bool HasProfile => SelectedProfile is not null;

    /// <summary>Приглушённая подсказка в пустом списке профилей; null — профиль выбран (подсказки нет).</summary>
    public string? ProfilePlaceholder => SelectedProfile is not null ? null
        : Profiles.Count == 0 ? "Нет созданных профилей" : "Выберите профиль";

    // «Сохранить как профиль» (докладка 2026-09-29, по отзыву): выбор в списке больше не решает, куда сохранять. Во всплывающем
    // поле — НОВОЕ имя по значениям: «Сохранить новый» (имя существующего — «Перезаписать «имя»»); если профиль выбран —
    // ещё явная кнопка «Сохранить в «выбранный»».

    private string _newProfileName = string.Empty;
    public string NewProfileName
    {
        get => _newProfileName;
        set { Set(ref _newProfileName, value); RefreshSaveButtons(); }
    }

    private WindowSizeProfile? ProfileNamed(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public string SaveNewButtonText => ProfileNamed(NewProfileName) is { } p ? $"Перезаписать «{p.Name}»" : "Сохранить новый";

    /// <summary>«Сохранить в «выбранный»» — только если профиль выбран и в поле не его имя (иначе основная кнопка и так перезапишет).</summary>
    public bool CanSaveToSelected => SelectedProfile is { } p && !ReferenceEquals(ProfileNamed(NewProfileName), p);
    public string SaveToSelectedText => SelectedProfile is { } p ? $"Сохранить в «{p.Name}»" : string.Empty;

    private void RefreshSaveButtons()
    {
        OnPropertyChanged(nameof(SaveNewButtonText));
        OnPropertyChanged(nameof(CanSaveToSelected));
        OnPropertyChanged(nameof(SaveToSelectedText));
    }

    private bool _isSavePopupOpen;
    public bool IsSavePopupOpen { get => _isSavePopupOpen; set => Set(ref _isSavePopupOpen, value); }

    public RelayCommand OpenSaveProfileCommand => _openSaveProfileCommand ??= new RelayCommand(() =>
    {
        NewProfileName = $"{Width}×{Height} в ({X}, {Y}){(Borderless ? ", без рамки" : "")}";
        IsSavePopupOpen = true;
    });
    private RelayCommand? _openSaveProfileCommand;

    /// <summary>Сохранить по имени из поля: новое — новый профиль, имя существующего — перезаписать его.</summary>
    public RelayCommand SaveProfileCommand => _saveProfileCommand ??= new RelayCommand(() =>
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0) { Status = "Введите имя профиля"; return; }
        SaveInto(ProfileNamed(name) ?? AddProfile(name));
    });
    private RelayCommand? _saveProfileCommand;

    /// <summary>Перезаписать выбранный профиль текущими значениями.</summary>
    public RelayCommand SaveToSelectedCommand => _saveToSelectedCommand ??= new RelayCommand(() =>
    {
        if (SelectedProfile is { } p) SaveInto(p);
    });
    private RelayCommand? _saveToSelectedCommand;

    private WindowSizeProfile AddProfile(string name)
    {
        var profile = new WindowSizeProfile { Name = name };
        Profiles.Add(profile);
        _main.Settings.WindowProfiles.Add(profile);
        return profile;
    }

    private void SaveInto(WindowSizeProfile profile)
    {
        (profile.X, profile.Y, profile.Width, profile.Height, profile.Borderless, profile.NotifyResize, profile.ConfineCursor) = (X, Y, Width, Height, Borderless, NotifyResize, ConfineCursor);
        SaveAndNotify(); // программы с этим профилем «постоянно» сразу получат новые значения
        SelectedProfile = null;
        SelectedProfile = profile;
        IsSavePopupOpen = false;
        Status = $"Профиль «{profile.Name}» сохранён";
    }

    public RelayCommand ApplyProfileCommand => _applyProfileCommand ??= new RelayCommand(() =>
    {
        if (SelectedProfile is not { } p) return;
        (X, Y, Width, Height, Borderless, NotifyResize, ConfineCursor) = (p.X, p.Y, p.Width, p.Height, p.Borderless, p.NotifyResize, p.ConfineCursor);
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

    // «Применять постоянно» (докладка 2026-09-29) — переключатель в строке профиля: включён — выбранный профиль применяется
    // к программе выбранного окна сам (при каждом запуске и если программа поменяет окно); повторное нажатие — выключить.

    /// <summary>Назначенный программе выбранного окна профиль (применяется постоянно) или null.</summary>
    private WindowSizeProfile? AssignedProfile =>
        SelectedWindow?.Info.ProgramKey is { } key && _main.Settings.WindowProfileAssignments.FirstOrDefault(a => a.ProgramKey == key) is { } a
            ? Profiles.FirstOrDefault(p => p.Name == a.ProfileName)
            : null;

    /// <summary>Переключатель «Применять постоянно»: включён, если программе назначен именно выбранный профиль.</summary>
    public bool ApplyPermanently
    {
        get => SelectedProfile is { } p && ReferenceEquals(AssignedProfile, p);
        set
        {
            if (value == ApplyPermanently) return;
            if (!value)
            {
                if (DisableAutoApply() is { } name) Status = $"«{name}» больше не применяется постоянно. Профиль остался в списке, окно — где стоит.";
                return;
            }
            if (SelectedWindow?.Info is not { ProgramKey: { } key } info || SelectedProfile is not { } profile) return;
            _main.Settings.WindowProfileAssignments.RemoveAll(a => a.ProgramKey == key);
            _main.Settings.WindowProfileAssignments.Add(new WindowProfileAssignment { ProgramKey = key, ProgramName = info.ExeName, ProfileName = profile.Name });
            SaveAndNotify(); // служба сразу применит профиль к открытому окну
            RefreshAssignment();
            Status = $"«{profile.Name}» применяется к {info.ExeName} постоянно — при каждом запуске и если программа сама поменяет окно";
        }
    }

    public string ApplyPermanentlyToolTip => AssignedProfile is { } p
        ? $"Сейчас к этой программе постоянно применяется «{p.Name}». Щелчок по включённой кнопке — выключить."
        : "Применять выбранный профиль к этой программе постоянно: при каждом её запуске и если она сама поменяет окно.";

    /// <summary>Снять назначение с программы выбранного окна; возвращает имя снятого профиля.</summary>
    private string? DisableAutoApply()
    {
        if (SelectedWindow?.Info.ProgramKey is not { } key || AssignedProfile is not { } p) return null;
        _main.Settings.WindowProfileAssignments.RemoveAll(a => a.ProgramKey == key);
        SaveAndNotify();
        RefreshAssignment();
        return p.Name;
    }

    /// <summary>
    /// Окно меняют вручную (быстрые кнопки, монитор, «Применить» с другими значениями, другой профиль), а программе
    /// назначен профиль — снять автоприменение (докладка 2026-09-29): иначе служба тут же вернула бы окно по профилю.
    /// </summary>
    private bool ReleaseAutoApplyForManualChange(WindowBounds target)
    {
        if (AssignedProfile is not { } p) return false;
        bool sameAsProfile = p.X == target.X && p.Y == target.Y && p.Width == target.Width && p.Height == target.Height && p.Borderless == target.Borderless;
        if (sameAsProfile) return false;
        DisableAutoApply();
        return true;
    }

    /// <summary>Метка «профиль» у окон, к программам которых профиль применяется постоянно.</summary>
    private void RefreshWindowMarks()
    {
        foreach (var w in Windows)
            w.PermanentProfile = w.Info.ProgramKey is { } key
                ? _main.Settings.WindowProfileAssignments.FirstOrDefault(a => a.ProgramKey == key)?.ProfileName
                : null;
    }

    /// <summary>«Убрать профиль» — сбросить поле на «Выберите профиль» (профиль и его постоянное применение не трогаются).</summary>
    public RelayCommand ClearProfileCommand => _clearProfileCommand ??= new RelayCommand(() => SelectedProfile = null);
    private RelayCommand? _clearProfileCommand;

    // «Переименовать»: всплывает поле с текущим именем; назначения программам переименовываются вместе с профилем.
    private bool _isRenamePopupOpen;
    public bool IsRenamePopupOpen { get => _isRenamePopupOpen; set => Set(ref _isRenamePopupOpen, value); }

    private string _renameName = string.Empty;
    public string RenameName
    {
        get => _renameName;
        set { Set(ref _renameName, value); OnPropertyChanged(nameof(CanRename)); OnPropertyChanged(nameof(RenameHint)); }
    }

    /// <summary>Новое имя не пустое, отличается и не занято другим профилем.</summary>
    public bool CanRename => SelectedProfile is { } p && RenameName.Trim().Length > 0 && RenameName.Trim() != p.Name
                             && (ProfileNamed(RenameName) is null || ReferenceEquals(ProfileNamed(RenameName), p));

    public string RenameHint => SelectedProfile is { } p && ProfileNamed(RenameName) is { } other && !ReferenceEquals(other, p)
        ? "Такое имя уже есть" : "Новое имя профиля";

    public RelayCommand OpenRenameCommand => _openRenameCommand ??= new RelayCommand(() =>
    {
        if (SelectedProfile is not { } p) return;
        RenameName = p.Name;
        IsRenamePopupOpen = true;
    });
    private RelayCommand? _openRenameCommand;

    public RelayCommand RenameCommand => _renameCommand ??= new RelayCommand(() =>
    {
        if (!CanRename || SelectedProfile is not { } p) return;
        string oldName = p.Name, newName = RenameName.Trim();
        p.Name = newName;
        foreach (var a in _main.Settings.WindowProfileAssignments.Where(a => a.ProfileName == oldName)) a.ProfileName = newName;
        SaveAndNotify();
        // Профиль не сообщает об изменении имени — переставить его в списке, чтобы поле показало новое имя.
        int index = Profiles.IndexOf(p);
        Profiles.RemoveAt(index);
        Profiles.Insert(index, p);
        SelectedProfile = p;
        IsRenamePopupOpen = false;
    });
    private RelayCommand? _renameCommand;

    private void RefreshAssignment()
    {
        OnPropertyChanged(nameof(ApplyPermanently));
        OnPropertyChanged(nameof(ApplyPermanentlyToolTip));
        RefreshWindowMarks();
    }

    private void SaveAndNotify()
    {
        _main.SaveSettings();
        _main.RaiseWindowProfilesChanged();
    }
}

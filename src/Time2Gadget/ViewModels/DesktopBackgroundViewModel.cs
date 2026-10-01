using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Пункт списка картинок: «Выбрать свой файл» (первый) или картинка из папки программы.</summary>
public sealed record WallpaperImageOption(string? Path, string Label)
{
    public bool IsBrowse => Path is null;
    public System.Windows.Media.ImageSource? Thumb => Thumbnails.For(Path);
}

/// <summary>Цвет из палитры квадратика монитора.</summary>
public sealed record WallpaperColorOption(string Hex, string Name);

/// <summary>
/// Кнопка «Мон N»: щелчок — следующий режим (растянуть → по размеру → заполнить → по центру → не отображать). Когда на мониторе
/// нет картинки (её сняли или «не отображать»), к кнопке прилипает квадратик сплошного цвета — щелчок открывает 16 цветов.
/// </summary>
public sealed class MonitorFitItem : INotifyPropertyChanged
{
    private readonly Action<MonitorFitItem> _onFitChanged;
    private readonly Action<MonitorFitItem> _onColorChanged;
    private readonly Func<bool> _hasImage;
    private WallpaperFit _fit;
    private string? _color;
    private bool _isColorMenuOpen;

    /// <summary>16 основных цветов (как в классической палитре Windows).</summary>
    public static IReadOnlyList<WallpaperColorOption> BasicColors { get; } = new WallpaperColorOption[]
    {
        new("#000000", "Чёрный"), new("#808080", "Серый"), new("#800000", "Тёмно-красный"), new("#808000", "Оливковый"),
        new("#008000", "Зелёный"), new("#008080", "Сине-зелёный"), new("#000080", "Тёмно-синий"), new("#800080", "Фиолетовый"),
        new("#FFFFFF", "Белый"), new("#C0C0C0", "Серебристый"), new("#FF0000", "Красный"), new("#FFFF00", "Жёлтый"),
        new("#00FF00", "Салатовый"), new("#00FFFF", "Голубой"), new("#0000FF", "Синий"), new("#FF00FF", "Пурпурный"),
    };

    public MonitorFitItem(WallpaperMonitor monitor, WallpaperFit fit, string? color, Func<bool> hasImage,
                          Action<MonitorFitItem> onFitChanged, Action<MonitorFitItem> onColorChanged)
    {
        Monitor = monitor;
        _fit = fit;
        _color = color;
        _hasImage = hasImage;
        _onFitChanged = onFitChanged;
        _onColorChanged = onColorChanged;
        CycleCommand = new RelayCommand(() =>
        {
            Fit = Fit == WallpaperFit.None ? WallpaperFit.Stretch : Fit + 1;
            _onFitChanged(this);
        });
        OpenColorMenuCommand = new RelayCommand(() => IsColorMenuOpen = true);
        ChooseColorCommand = new RelayCommand(p =>
        {
            IsColorMenuOpen = false;
            if (p is not string hex) return;
            _color = hex;
            Raise(nameof(ColorBrush));
            Raise(nameof(HasColor));
            Raise(nameof(ColorToolTip));
            _onColorChanged(this);
        });
    }

    public WallpaperMonitor Monitor { get; }
    public string Label => $"Мон{Monitor.Number}";
    public RelayCommand CycleCommand { get; }
    public RelayCommand OpenColorMenuCommand { get; }
    public RelayCommand ChooseColorCommand { get; }

    public WallpaperFit Fit
    {
        get => _fit;
        private set { _fit = value; Raise(nameof(Fit)); Raise(nameof(ToolTip)); Raise(nameof(ShowColor)); }
    }

    public string? Color => _color;

    /// <summary>Цвет квадратика; цвет не выбран — прозрачный с чертой («нет цвета»: на мониторе прежний фон).</summary>
    public string ColorBrush => _color ?? "Transparent";
    public bool HasColor => _color is not null;

    /// <summary>Квадратик цвета — только когда на мониторе нет картинки.</summary>
    public bool ShowColor => !_hasImage() || Fit == WallpaperFit.None;

    public bool IsColorMenuOpen
    {
        get => _isColorMenuOpen;
        set { if (_isColorMenuOpen != value) { _isColorMenuOpen = value; Raise(nameof(IsColorMenuOpen)); } }
    }

    public string ColorToolTip => _color is null
        ? $"Монитор {Monitor.Number}: прежний фон. Клик ЛКМ — выбрать сплошной цвет"
        : $"Монитор {Monitor.Number}: сплошной цвет {BasicColors.FirstOrDefault(c => c.Hex == _color)?.Name ?? _color}. Клик ЛКМ — другой цвет";

    /// <summary>Картинку выбрали или сняли — показать/скрыть квадратик.</summary>
    public void RefreshShowColor() => Raise(nameof(ShowColor));

    public static string FitName(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "по размеру",
        WallpaperFit.Fill => "заполнить",
        WallpaperFit.Center => "по центру",
        WallpaperFit.None => "не отображать",
        _ => "растянуть",
    };

    public string ToolTip =>
        $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}: {FitName(Fit)}.\n" +
        $"Клик ЛКМ — {FitName(Fit == WallpaperFit.None ? WallpaperFit.Stretch : Fit + 1)}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
/// <summary>
/// Окно «Заставка и фон экрана» (докладка 2026-09-30): «Закрепить фоны» (перенесено из настроек) и своя картинка фона —
/// список картинок (свои файлы копируются в папку программы), у каждого монитора свой режим, «✕» — вернуть прежний фон.
/// Состояние — в MainViewModel (MainViewModel.Wallpaper.cs), здесь только список и кнопки.
/// </summary>
public sealed class DesktopBackgroundViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private WallpaperImageOption? _selectedImage;
    private string _status = string.Empty;

    public DesktopBackgroundViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    /// <summary>Галочка «Закрепить фоны» привязана прямо к MainViewModel.</summary>
    public MainViewModel Main => _main;

    public ObservableCollection<WallpaperImageOption> Images { get; } = new();
    public ObservableCollection<MonitorFitItem> Monitors { get; } = new();

    /// <summary>Выбор в списке: «Выбрать свой файл» открывает выбор файлов; картинка — сразу ставится на мониторы.</summary>
    public WallpaperImageOption? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedImage)) return;
            if (value.IsBrowse)
            {
                // Выделение вернуть на прежнюю картинку (или на пусто), выбранные файлы — ниже.
                Dispatcher.CurrentDispatcher.BeginInvoke(() => { OnPropertyChanged(); Browse(); });
                return;
            }
            _selectedImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            foreach (var m in Monitors) m.RefreshShowColor();
            Status = _main.SetWallpaperImage(value.Path) ? string.Empty : "Не удалось поставить картинку — файл повреждён или недоступен.";
        }
    }

    public bool HasImage => _selectedImage is not null;

    /// <summary>Строка под кнопками — только когда что-то не получилось.</summary>
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    /// <summary>«✕» (левый щелчок): картинку убрать — на всех мониторах сплошной чёрный.</summary>
    public RelayCommand ClearCommand => _clearCommand ??= new RelayCommand(() =>
    {
        _main.BlackoutWallpaper();
        Reload();
        Status = string.Empty;
    });
    private RelayCommand? _clearCommand;

    /// <summary>Список картинок, мониторы с режимами и выбранная картинка — из настроек.</summary>
    public void Reload()
    {
        Images.Clear();
        Images.Add(new WallpaperImageOption(null, "Выбрать свой файл"));
        foreach (var file in WallpaperService.ListImages())
            Images.Add(new WallpaperImageOption(file, Path.GetFileName(file)));
        _selectedImage = _main.WallpaperImage is { } current
            ? Images.FirstOrDefault(i => string.Equals(i.Path, current, StringComparison.OrdinalIgnoreCase))
            : null;
        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(HasImage));

        Monitors.Clear();
        foreach (var m in WallpaperService.Monitors())
            Monitors.Add(new MonitorFitItem(m, _main.WallpaperFitFor(m.Id), _main.WallpaperColorFor(m.Id), () => HasImage, OnFitChanged, OnColorChanged));
        ReloadSlideshowMonitors();
    }

    private void OnFitChanged(MonitorFitItem item)
    {
        bool ok = _main.SetWallpaperFit(item.Monitor.Id, item.Fit);
        if (HasImage) Status = ok ? string.Empty : "Не удалось поставить картинку — файл повреждён или недоступен.";
    }

    private void OnColorChanged(MonitorFitItem item)
    {
        if (item.Color is { } hex)
            Status = _main.SetWallpaperColor(item.Monitor.Id, hex) ? string.Empty : "Не удалось поставить фон.";
    }

    /// <summary>«✕» (правый щелчок): вернуть фон, который был до своих настроек.</summary>
    public RelayCommand RestoreCommand => _restoreCommand ??= new RelayCommand(() =>
    {
        _main.ClearWallpaper();
        Reload();
        Status = string.Empty;
    });
    private RelayCommand? _restoreCommand;

    /// <summary>Выбрать один или несколько файлов — копии в папку программы; первая выбранная сразу ставится.</summary>
    private void Browse()
    {
        var (imported, message) = PickImages(_main);
        Status = message;
        if (imported.Count == 0) return;
        Reload();
        SelectedImage = Images.FirstOrDefault(i => string.Equals(i.Path, imported[0], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Диалог выбора картинок (jpg, jpeg, png; одну или несколько) и копирование в папку программы — не больше 30 всего.
    /// Возвращает копии и строку для пользователя (пусто — всё получилось).
    /// </summary>
    internal static (List<string> Imported, string Message) PickImages(MainViewModel main)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите картинки для фона",
            Filter = "Картинки (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return (new(), string.Empty);
        var files = dialog.FileNames;
        var batch = files.Take(WallpaperService.BatchLimit).ToList();
        var (imported, skipped) = main.ImportLibraryImages(batch);
        var notes = new List<string>();
        if (files.Length > batch.Count)
            notes.Add($"За раз можно добавить не больше {WallpaperService.BatchLimit} картинок — добавлены первые {batch.Count} из {files.Length}.");
        if (skipped > 0)
            notes.Add($"В папке программы не больше {WallpaperService.LibraryLimit} картинок — не поместились {skipped}. Лишние можно удалить в окне шагов слайдшоу (✕ у картинки).");
        if (imported.Count == 0 && notes.Count == 0) notes.Add("Не удалось скопировать файлы в папку программы.");
        return (imported, string.Join(" ", notes));
    }

    // ---------------- Динамичная заставка / слайдшоу ----------------

    public ObservableCollection<SlideshowMonitorItem> SlideshowMonitors { get; } = new();

    /// <summary>Окно шагов монитора открывает View (нужен владелец окна) — сюда оно подписывается.</summary>
    public Action<WallpaperMonitor>? OpenSlideshowEditor { get; set; }

    /// <summary>Окно сопоставления мониторов при импорте (View): null — «Отмена».</summary>
    public Func<WallpaperPackageData, (bool Apply, Dictionary<int, string> Map)?>? ChooseImport { get; set; }

    private void ReloadSlideshowMonitors()
    {
        SlideshowMonitors.Clear();
        foreach (var m in WallpaperService.Monitors())
            SlideshowMonitors.Add(new SlideshowMonitorItem(m, _main.SlideshowCycleLength(m.Id), _main.CanRestoreSlideshowMonitor(m.Id),
                open: () => OpenSlideshowEditor?.Invoke(m),
                clear: () => _main.ClearSlideshowMonitor(m.Id),
                restore: () => _main.RestoreSlideshowMonitor(m.Id)));
    }

    /// <summary>Экспорт: один архив со всеми картинками и настройками (по умолчанию — на рабочий стол).</summary>
    public RelayCommand ExportCommand => _exportCommand ??= new RelayCommand(() =>
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт заставки и фона",
            Filter = "Архив (*.zip)|*.zip",
            FileName = $"Тайм2гаджет — фоны {DateTime.Now:yyyy-MM-dd}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _main.ExportWallpaper(dialog.FileName);
            ExchangeStatus = $"Сохранено: {Path.GetFileName(dialog.FileName)} — картинок: {WallpaperService.ListImages().Count}.";
        }
        catch (Exception ex) { ExchangeStatus = "Не удалось сохранить архив: " + ex.Message; }
    });
    private RelayCommand? _exportCommand;

    /// <summary>Импорт: картинки и настройки из архива; вопрос — применить сейчас или только скопировать.</summary>
    public RelayCommand ImportCommand => _importCommand ??= new RelayCommand(() =>
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт заставки и фона",
            Filter = "Архив (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // Сначала — какие мониторы в архиве и куда их настройки (окно сопоставления), потом копирование.
            var data = WallpaperPackage.ReadData(dialog.FileName);
            var choice = data is null ? (Apply: false, Map: new Dictionary<int, string>()) : ChooseImport?.Invoke(data);
            if (choice is not { } c) return; // «Отмена»
            ExchangeStatus = _main.ImportWallpaper(dialog.FileName, c.Apply, c.Map);
        }
        catch (Exception ex) { ExchangeStatus = "Не удалось прочитать архив: " + ex.Message; }
        Reload();
    });
    private RelayCommand? _importCommand;

    private string _exchangeStatus = string.Empty;
    /// <summary>Строка под кнопками экспорта/импорта.</summary>
    public string ExchangeStatus
    {
        get => _exchangeStatus;
        private set { _exchangeStatus = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Кнопка «Монитор N» слайдшоу: щелчок — окно шагов; «✕» правее — левый щелчок убрать шаги, правый — вернуть.</summary>
public sealed class SlideshowMonitorItem
{
    public SlideshowMonitorItem(WallpaperMonitor monitor, int filled, bool canRestore, Action open, Action clear, Action restore)
    {
        Monitor = monitor;
        Filled = filled;
        CanRestore = canRestore;
        OpenCommand = new RelayCommand(open);
        ClearCommand = new RelayCommand(clear);
        RestoreCommand = new RelayCommand(restore);
    }

    public WallpaperMonitor Monitor { get; }
    public int Filled { get; }
    public bool CanRestore { get; }
    public string Label => $"Монитор {Monitor.Number}";
    public string Caption => Filled == 0 ? "пусто" : $"шагов в цикле: {Filled}";
    public string ToolTip => $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}. Клик ЛКМ — разложить картинки по шагам слайдшоу";
    public string ClearToolTip => "Клик ЛКМ — убрать шаги этого монитора (на нём — прежний фон).\nКлик ПКМ — вернуть убранные шаги"
                                  + (CanRestore ? "" : " (сейчас возвращать нечего)");
    public RelayCommand OpenCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RestoreCommand { get; }
}

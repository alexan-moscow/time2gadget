using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Цвет из палитры квадратика монитора.</summary>
public sealed record WallpaperColorOption(string Hex, string Name);

/// <summary>
/// Плитка монитора статичной заставки (докладка 2026-10-01): «Монитор N», ниже — превью его картинки в его режиме (или цвет,
/// или «не выбрано»). Клик ЛКМ по плитке — окно выбора картинки; справа — режим (растянуть → по размеру → заполнить → по центру),
/// квадратик сплошного цвета (когда картинки нет) и «✕» (клик ЛКМ — сплошной чёрный, клик ПКМ — прежний фон этого монитора).
/// </summary>
public sealed class MonitorFitItem : INotifyPropertyChanged
{
    private readonly Action<MonitorFitItem> _onFitChanged;
    private readonly Action<MonitorFitItem> _onColorChanged;
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

    public MonitorFitItem(WallpaperMonitor monitor, string? image, WallpaperFit fit, string? color,
                          Action<MonitorFitItem> onFitChanged, Action<MonitorFitItem> onColorChanged,
                          Action<MonitorFitItem> open, Action<MonitorFitItem> clear, Action<MonitorFitItem> restore)
    {
        Monitor = monitor;
        Image = image;
        _fit = fit == WallpaperFit.None ? WallpaperFit.Stretch : fit;
        _color = color;
        _onFitChanged = onFitChanged;
        _onColorChanged = onColorChanged;
        CycleCommand = new RelayCommand(() =>
        {
            Fit = NextFit(Fit);
            _onFitChanged(this);
        });
        OpenColorMenuCommand = new RelayCommand(() => IsColorMenuOpen = true);
        ChooseColorCommand = new RelayCommand(p =>
        {
            IsColorMenuOpen = false;
            if (p is not string hex) return;
            _color = hex;
            Raise(nameof(Color)); Raise(nameof(ColorBrush)); Raise(nameof(HasColor)); Raise(nameof(ColorToolTip));
            _onColorChanged(this);
        });
        OpenCommand = new RelayCommand(() => open(this));
        ClearCommand = new RelayCommand(() => clear(this));
        RestoreCommand = new RelayCommand(() => restore(this));
    }

    public WallpaperMonitor Monitor { get; }
    public string Label => $"Монитор {Monitor.Number}";
    public double MonitorWidth => Monitor.Bounds.Width;
    public double MonitorHeight => Monitor.Bounds.Height;

    /// <summary>Картинка монитора; null — нет (цвет или прежний фон).</summary>
    public string? Image { get; }
    public bool HasImage => Image is not null;

    public RelayCommand CycleCommand { get; }
    public RelayCommand OpenColorMenuCommand { get; }
    public RelayCommand ChooseColorCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RestoreCommand { get; }

    public WallpaperFit Fit
    {
        get => _fit;
        private set { _fit = value; Raise(nameof(Fit)); Raise(nameof(ToolTip)); Raise(nameof(PreviewFit)); }
    }

    public string? Color => _color;

    /// <summary>Цвет квадратика; цвет не выбран — прозрачный с чертой («нет цвета»: на мониторе прежний фон).</summary>
    public string ColorBrush => _color ?? "Transparent";
    public bool HasColor => _color is not null;

    /// <summary>Квадратик цвета — только когда на мониторе нет картинки.</summary>
    public bool ShowColor => !HasImage;

    public bool IsColorMenuOpen
    {
        get => _isColorMenuOpen;
        set { if (_isColorMenuOpen != value) { _isColorMenuOpen = value; Raise(nameof(IsColorMenuOpen)); } }
    }

    public string ColorToolTip => _color is null
        ? $"Монитор {Monitor.Number}: прежний фон. Клик ЛКМ — выбрать сплошной цвет"
        : $"Монитор {Monitor.Number}: сплошной цвет {BasicColors.FirstOrDefault(c => c.Hex == _color)?.Name ?? _color}. Клик ЛКМ — другой цвет";

    public string TileToolTip => HasImage
        ? $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}: {System.IO.Path.GetFileName(Image)}.\nКлик ЛКМ — выбрать другую картинку"
        : $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}: картинка не выбрана.\nКлик ЛКМ — выбрать картинку";

    public static WallpaperFit NextFit(WallpaperFit fit) => fit >= WallpaperFit.Center ? WallpaperFit.Stretch : fit + 1;

    public static string FitName(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "по размеру",
        WallpaperFit.Fill => "заполнить",
        WallpaperFit.Center => "по центру",
        WallpaperFit.None => "не отображать",
        _ => "растянуть",
    };

    public string ToolTip => $"Сейчас: {FitName(Fit)}. Клик ЛКМ — {FitName(NextFit(Fit))}";

    // ---- Одна картинка «Монитора 1» на все мониторы (докладка 2026-10-01) ----

    /// <summary>Способ растяжки (у всех плиток один).</summary>
    public WallpaperSpan Span { get; init; }
    /// <summary>Плитка «Монитора 1» — у неё кнопка растяжки (если мониторов больше одного).</summary>
    public bool IsSpanMain { get; init; }
    /// <summary>Остальные мониторы при растяжке показывают свой кусок картинки «Монитора 1»; их кнопки скрыты.</summary>
    public bool IsSpanFollower { get; init; }
    public bool IsSpanActive => Span != WallpaperSpan.None;
    public RelayCommand? SpanCommand { get; init; }

    /// <summary>«▶▶»: эти же картинка, режим и цвет — следующему монитору (есть следующий и есть что копировать).</summary>
    public RelayCommand? CopyNextCommand { get; init; }
    public bool ShowCopyNext => CopyNextCommand is not null && !IsSpanFollower && (HasImage || HasColor);
    public string CopyNextToolTip => $"Клик ЛКМ — эти же картинку, режим и цвет поставить на монитор {Monitor.Number + 1}";

    /// <summary>Превью: при растяжке — картинка «Монитора 1» в общем прямоугольнике (кусок этого монитора).</summary>
    public string? PreviewImage { get => _previewImage ?? Image; init => _previewImage = value; }
    private readonly string? _previewImage;
    public WallpaperFit PreviewFit => _previewFit ?? Fit;
    public WallpaperFit? PreviewFitOverride { init => _previewFit = value; }
    private readonly WallpaperFit? _previewFit;
    public string? PreviewColor { get => _previewColor ?? Color; init => _previewColor = value; }
    private readonly string? _previewColor;
    /// <summary>Общий прямоугольник мониторов относительно этого монитора; Empty — картинка только своя.</summary>
    public System.Windows.Rect SpanArea { get; init; } = System.Windows.Rect.Empty;

    /// <summary>Кнопка режима картинки: при растяжке «охватить / растянуть / вписать» режим один на все — кнопки нет.</summary>
    public bool ShowFitButton => HasImage && !IsSpanFollower && Span is not (WallpaperSpan.Span or WallpaperSpan.Stretch or WallpaperSpan.Fit);
    public bool ShowColorButton => ShowColor && !IsSpanFollower;

    public string SpanToolTip => Span == WallpaperSpan.None
        ? "Одна картинка этого монитора на все мониторы: выключено.\nКлик ЛКМ — " + MainViewModel.SpanName(WallpaperSpan.Span)
        : $"Одна картинка на все мониторы: {MainViewModel.SpanName(Span)}.\nКлик ЛКМ — {MainViewModel.SpanName(MainViewModel.NextSpan(Span))}.\n«✕» — выключить (сплошной цвет)";

    public string FollowerToolTip => $"Монитор {Monitor.Number} показывает свой кусок картинки монитора 1 ({MainViewModel.SpanName(Span)}).\nВыключить — «✕» у монитора 1";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Окно «Фоновая заставка и слайдшоу экрана» (докладка 2026-09-30/10-01): статичная заставка — плитки мониторов (у каждого своя
/// картинка, режим, цвет), слайдшоу, «Закрепить фоны», экспорт/импорт. Состояние — в MainViewModel (MainViewModel.Wallpaper.cs).
/// </summary>
public sealed class DesktopBackgroundViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private string _status = string.Empty;

    public DesktopBackgroundViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    /// <summary>Тумблеры, «Закрепить фоны» и расписание привязаны прямо к MainViewModel.</summary>
    public MainViewModel Main => _main;

    public ObservableCollection<MonitorFitItem> Monitors { get; } = new();

    /// <summary>Строка под плитками: включена одна картинка на все мониторы — каким способом (докладка 2026-10-01).</summary>
    public string SpanStatus
    {
        get => _spanStatus;
        private set { _spanStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSpanStatus)); }
    }
    private string _spanStatus = string.Empty;
    public bool HasSpanStatus => _spanStatus.Length > 0;

    /// <summary>Строка под плитками — только когда что-то не получилось.</summary>
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    /// <summary>Окно выбора картинки монитора открывает View (нужен владелец окна).</summary>
    public Action<WallpaperMonitor>? OpenImagePicker { get; set; }

    /// <summary>Плитки мониторов и мониторы слайдшоу — из настроек.</summary>
    public void Reload()
    {
        Monitors.Clear();
        var monitors = WallpaperService.Monitors();
        var spanMain = monitors.Count > 1 ? MainViewModel.SpanMainMonitor(monitors) : null;
        string? spanImage = spanMain is null ? null : _main.WallpaperImageFor(spanMain.Id);
        var span = spanImage is null ? WallpaperSpan.None : _main.WallpaperSpan;
        var area = monitors.Count > 0 ? MainViewModel.SpanArea(monitors) : System.Drawing.Rectangle.Empty;
        SpanStatus = span == WallpaperSpan.None ? string.Empty
            : span switch // коротко — только что сейчас используется
            {
                WallpaperSpan.Span => "Охват всех мониторов",
                WallpaperSpan.Stretch => "Растянуто на все мониторы",
                WallpaperSpan.Fit => "Вписано во все мониторы",
                _ => "Одна картинка на каждом мониторе",
            };
        foreach (var m in monitors)
        {
            bool isMain = spanMain is not null && m.Id == spanMain.Id;
            bool follower = span != WallpaperSpan.None && !isMain;
            bool spanArea = span is WallpaperSpan.Span or WallpaperSpan.Stretch or WallpaperSpan.Fit;
            Monitors.Add(new MonitorFitItem(m, _main.WallpaperImageFor(m.Id), _main.WallpaperFitFor(m.Id), _main.WallpaperColorFor(m.Id),
                OnFitChanged, OnColorChanged,
                open: item => OpenImagePicker?.Invoke(item.Monitor),
                clear: item => { _main.BlackoutMonitorWallpaper(item.Monitor.Id); Status = string.Empty; },
                restore: item => { _main.RestoreMonitorWallpaper(item.Monitor.Id); Status = string.Empty; })
            {
                Span = span,
                IsSpanMain = isMain,
                IsSpanFollower = follower,
                SpanCommand = isMain ? new RelayCommand(() => { Status = _main.CycleWallpaperSpan(); Reload(); }) : null,
                CopyNextCommand = monitors.Any(o => o.Number > m.Number) ? new RelayCommand(() => { Status = _main.CopyWallpaperToNext(m.Id); Reload(); }) : null,
                PreviewImage = span != WallpaperSpan.None ? spanImage : null,
                PreviewFitOverride = span == WallpaperSpan.None ? null : spanArea ? MainViewModel.SpanFit(span) : _main.WallpaperFitFor(spanMain!.Id),
                PreviewColor = span != WallpaperSpan.None ? _main.WallpaperColorFor(spanMain!.Id) ?? "#000000" : null,
                SpanArea = spanArea ? new System.Windows.Rect(area.X - m.Bounds.X, area.Y - m.Bounds.Y, area.Width, area.Height) : System.Windows.Rect.Empty,
            });
        }
        ReloadSlideshowMonitors();
    }

    private void OnFitChanged(MonitorFitItem item)
    {
        bool ok = _main.SetWallpaperFit(item.Monitor.Id, item.Fit);
        Status = ok ? string.Empty : "Не удалось поставить картинку — файл повреждён или недоступен.";
        Reload(); // превью — в новом режиме
    }

    private void OnColorChanged(MonitorFitItem item)
    {
        if (item.Color is { } hex)
            Status = _main.SetWallpaperColor(item.Monitor.Id, hex) ? string.Empty : "Не удалось поставить фон.";
        Reload();
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

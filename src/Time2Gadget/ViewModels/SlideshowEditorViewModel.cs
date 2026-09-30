using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Миниатюры картинок для окна шагов: файл не держится открытым (его можно удалить), уменьшено при чтении.</summary>
internal static class Thumbnails
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string? path)
    {
        if (path is null) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // прочитать сразу и отпустить файл
            bitmap.DecodePixelWidth = 160;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch { /* битый файл — без миниатюры */ }
        return Cache[path] = image;
    }

    public static void Forget(string path) => Cache.Remove(path);
}

/// <summary>Картинка в списке слева: миниатюра, имя, «✕» — удалить безвозвратно.</summary>
public sealed class LibraryItem
{
    public LibraryItem(string path, Action<LibraryItem> delete)
    {
        Path = path;
        DeleteCommand = new RelayCommand(() => delete(this));
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public ImageSource? Thumb => Thumbnails.For(Path);
    public RelayCommand DeleteCommand { get; }
}

/// <summary>Шаг слайдшоу: пусто / картинка (со своим режимом) / сплошной цвет.</summary>
public sealed class SlotItem : INotifyPropertyChanged
{
    private SlideshowSlot? _slot;
    private bool _isColorMenuOpen;

    public SlotItem(int number, SlideshowSlot? slot)
    {
        Number = number;
        _slot = slot?.Clone();
        ClearCommand = new RelayCommand(() => Set(null));
        CycleFitCommand = new RelayCommand(() =>
        {
            if (_slot?.Image is null) return;
            // у шагов «не отображать» нет — пустой шаг и так показывает прежний фон
            _slot.Fit = _slot.Fit >= WallpaperFit.Center ? WallpaperFit.Stretch : _slot.Fit + 1;
            RaiseAll();
        });
        OpenColorMenuCommand = new RelayCommand(() => { if (_slot?.Image is null) IsColorMenuOpen = true; });
        ChooseColorCommand = new RelayCommand(p =>
        {
            IsColorMenuOpen = false;
            if (p is string hex) Set(new SlideshowSlot { Color = hex });
        });
    }

    public int Number { get; }
    public SlideshowSlot? Slot => _slot;

    private bool _isActive = true;
    /// <summary>Шаг входит в цикл (иначе приглушён — «Шагов в цикле» меньше его номера).</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; Raise(nameof(IsActive)); } }
    }

    public bool IsEmpty => _slot is null || _slot.IsEmpty;
    public bool HasImage => _slot?.Image is not null;
    public bool HasColor => _slot?.Image is null && _slot?.Color is not null;
    public ImageSource? Thumb => Thumbnails.For(_slot?.Image);
    public string ColorBrush => HasColor ? _slot!.Color! : "Transparent";
    public WallpaperFit Fit => _slot?.Fit ?? WallpaperFit.Fill;

    public string ToolTip => HasImage
        ? $"Шаг {Number}: {System.IO.Path.GetFileName(_slot!.Image)}, {MonitorFitItem.FitName(Fit)}"
        : HasColor
            ? $"Шаг {Number}: сплошной цвет {MonitorFitItem.BasicColors.FirstOrDefault(c => c.Hex == _slot!.Color)?.Name ?? _slot!.Color}. Щелчок — другой цвет"
            : $"Шаг {Number}: пусто — прежний фон монитора. Перетащите сюда картинку или щёлкните — сплошной цвет";

    public RelayCommand ClearCommand { get; }
    public RelayCommand CycleFitCommand { get; }
    public RelayCommand OpenColorMenuCommand { get; }
    public RelayCommand ChooseColorCommand { get; }

    public bool IsColorMenuOpen
    {
        get => _isColorMenuOpen;
        set { if (_isColorMenuOpen != value) { _isColorMenuOpen = value; Raise(nameof(IsColorMenuOpen)); } }
    }

    public void SetImage(string path) => Set(new SlideshowSlot { Image = path, Fit = WallpaperFit.Fill });

    public void Set(SlideshowSlot? slot)
    {
        _slot = slot;
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Slot), nameof(IsEmpty), nameof(HasImage), nameof(HasColor), nameof(Thumb), nameof(ColorBrush), nameof(Fit), nameof(ToolTip) })
            Raise(name);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Монитор в меню «Скопировать на другой монитор».</summary>
public sealed class CopyTargetItem : INotifyPropertyChanged
{
    private bool _isChecked;
    public CopyTargetItem(WallpaperMonitor monitor) => Monitor = monitor;
    public WallpaperMonitor Monitor { get; }
    public string Label => $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}";
    public bool IsChecked { get => _isChecked; set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Окно шагов слайдшоу одного монитора (докладка 2026-09-30): слева картинки из папки программы (добавить — до 30; «✕» —
/// удалить безвозвратно, шаги с ней очищаются), справа 30 шагов — картинки перетаскиваются, пустой шаг по щелчку — сплошной
/// цвет. «Сохранить» — в настройки, «✕» внизу — отмена. «Скопировать на другой монитор» — текущие шаги на отмеченные мониторы.
/// </summary>
public sealed class SlideshowEditorViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private string _status = string.Empty;
    private bool _isCopyMenuOpen;

    public SlideshowEditorViewModel(MainViewModel main, WallpaperMonitor monitor)
    {
        _main = main;
        Monitor = monitor;
        var slots = main.SlideshowSlotsFor(monitor.Id);
        for (int i = 0; i < slots.Count; i++)
        {
            var item = new SlotItem(i + 1, slots[i]);
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SlotItem.Slot)) RefreshCycle(); };
            Slots.Add(item);
        }
        _cycleEnabled = main.SlideshowCycleSetting(monitor.Id) is not null;
        _cycleLength = main.SlideshowCycleSetting(monitor.Id) ?? Math.Max(2, main.SlideshowLastFilled(monitor.Id));
        RefreshCycle();
        foreach (var m in WallpaperService.Monitors().Where(m => m.Id != monitor.Id)) CopyTargets.Add(new CopyTargetItem(m));
        ReloadLibrary();
    }

    public WallpaperMonitor Monitor { get; }
    public string Title => $"Слайдшоу — монитор {Monitor.Number} ({Monitor.Bounds.Width}×{Monitor.Bounds.Height}) — Тайм2гаджет";

    public ObservableCollection<LibraryItem> Library { get; } = new();
    public ObservableCollection<SlotItem> Slots { get; } = new();
    public ObservableCollection<CopyTargetItem> CopyTargets { get; } = new();

    public string LibraryCaption => $"Картинки ({Library.Count} из {WallpaperService.LibraryLimit})";
    public bool HasCopyTargets => CopyTargets.Count > 0;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private void ReloadLibrary()
    {
        Library.Clear();
        foreach (var file in WallpaperService.ListImages()) Library.Add(new LibraryItem(file, Delete));
        OnPropertyChanged(nameof(LibraryCaption));
    }

    public RelayCommand AddImagesCommand => _addImagesCommand ??= new RelayCommand(() =>
    {
        var (_, message) = DesktopBackgroundViewModel.PickImages(_main);
        Status = message;
        ReloadLibrary();
    });
    private RelayCommand? _addImagesCommand;

    /// <summary>«✕» у картинки: удалить безвозвратно; шаги с ней (здесь и в сохранённых настройках) очищаются.</summary>
    private void Delete(LibraryItem item)
    {
        foreach (var slot in Slots)
            if (string.Equals(slot.Slot?.Image, item.Path, StringComparison.OrdinalIgnoreCase)) slot.Set(null);
        Thumbnails.Forget(item.Path);
        Status = _main.DeleteLibraryImage(item.Path) ? string.Empty : $"Не удалось удалить «{item.Name}» — файл занят.";
        ReloadLibrary();
    }

    // ---- Шагов в цикле (решение пользователя 2026-09-30) ----
    // Тумблер выключен: цикл — до последнего заполненного шага (1 и 5 → 5, только 13 → 13). Включён: ползунок 2–30, шаги
    // после выбранного числа приглушены. Меньше двух шагов — предупреждение: слайдшоу не из чего менять.

    private bool _cycleEnabled;
    private int _cycleLength;

    public bool CycleEnabled
    {
        get => _cycleEnabled;
        set { _cycleEnabled = value; OnPropertyChanged(); RefreshCycle(); }
    }

    public int CycleLength
    {
        get => _cycleLength;
        set { _cycleLength = Math.Clamp(value, 2, SlideshowSettings.SlotCount); OnPropertyChanged(); RefreshCycle(); }
    }

    private int LastFilled => Slots.LastOrDefault(s => !s.IsEmpty)?.Number ?? 0;

    /// <summary>Длина цикла сейчас (как её посчитает слайдшоу).</summary>
    public int EffectiveCycle => LastFilled == 0 ? 0 : CycleEnabled ? CycleLength : LastFilled;

    /// <summary>Число справа от ползунка: включено — выбранное, выключено — по последнему заполненному шагу.</summary>
    public string CycleInfo => CycleEnabled
        ? CycleLength.ToString()
        : LastFilled == 0 ? "по заполненным" : $"{LastFilled} (по заполненным)";

    public string CycleWarning => EffectiveCycle == 1
        ? "Для слайдшоу нужно минимум 2 шага: заполните ещё шаг или включите «Шагов в цикле» и выберите 2 и больше."
        : string.Empty;

    private void RefreshCycle()
    {
        foreach (var s in Slots) s.IsActive = !CycleEnabled || s.Number <= CycleLength;
        OnPropertyChanged(nameof(EffectiveCycle));
        OnPropertyChanged(nameof(CycleInfo));
        OnPropertyChanged(nameof(CycleWarning));
    }

    private int? CycleSetting => CycleEnabled ? CycleLength : null;

    /// <summary>Картинку перетащили на шаг.</summary>
    public void DropImage(SlotItem slot, string path) => slot.SetImage(path);

    public bool IsCopyMenuOpen
    {
        get => _isCopyMenuOpen;
        set { if (_isCopyMenuOpen != value) { _isCopyMenuOpen = value; OnPropertyChanged(); } }
    }

    public RelayCommand OpenCopyMenuCommand => _openCopyMenuCommand ??= new RelayCommand(() => IsCopyMenuOpen = true);
    private RelayCommand? _openCopyMenuCommand;

    /// <summary>«Применить» в меню копирования: текущие шаги (как сейчас в окне) — на отмеченные мониторы.</summary>
    public RelayCommand ApplyCopyCommand => _applyCopyCommand ??= new RelayCommand(() =>
    {
        var targets = CopyTargets.Where(t => t.IsChecked).ToList();
        IsCopyMenuOpen = false;
        if (targets.Count == 0) return;
        foreach (var t in targets) _main.SetSlideshowSlots(t.Monitor.Id, Slots.Select(s => s.Slot), CycleSetting);
        Status = "Шаги скопированы: " + string.Join(", ", targets.Select(t => $"монитор {t.Monitor.Number}")) + ".";
        foreach (var t in targets) t.IsChecked = false;
    });
    private RelayCommand? _applyCopyCommand;

    /// <summary>«Сохранить»: шаги — в настройки (слайдшоу включено — сразу на экран).</summary>
    public void Save() => _main.SetSlideshowSlots(Monitor.Id, Slots.Select(s => s.Slot), CycleSetting);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>Пункт списка картинок: «Выбрать свой файл…» (первый) или картинка из папки программы.</summary>
public sealed record WallpaperImageOption(string? Path, string Label)
{
    public bool IsBrowse => Path is null;
}

/// <summary>Кнопка «Мон N»: щелчок — следующий режим (растянуть → по размеру → заполнить → по центру).</summary>
public sealed class MonitorFitItem : INotifyPropertyChanged
{
    private readonly Action<MonitorFitItem> _onChanged;
    private WallpaperFit _fit;

    public MonitorFitItem(WallpaperMonitor monitor, WallpaperFit fit, Action<MonitorFitItem> onChanged)
    {
        Monitor = monitor;
        _fit = fit;
        _onChanged = onChanged;
        CycleCommand = new RelayCommand(() =>
        {
            Fit = Fit == WallpaperFit.Center ? WallpaperFit.Stretch : Fit + 1;
            _onChanged(this);
        });
    }

    public WallpaperMonitor Monitor { get; }
    public string Label => $"Мон{Monitor.Number}";
    public RelayCommand CycleCommand { get; }

    public WallpaperFit Fit
    {
        get => _fit;
        private set
        {
            _fit = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Fit)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
        }
    }

    public static string FitName(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "по размеру",
        WallpaperFit.Fill => "заполнить",
        WallpaperFit.Center => "по центру",
        _ => "растянуть",
    };

    public string ToolTip =>
        $"Монитор {Monitor.Number} — {Monitor.Bounds.Width}×{Monitor.Bounds.Height}: {FitName(Fit)}.\n" +
        $"Щелчок — {FitName(Fit == WallpaperFit.Center ? WallpaperFit.Stretch : Fit + 1)}";

    public event PropertyChangedEventHandler? PropertyChanged;
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

    /// <summary>Выбор в списке: «Выбрать свой файл…» открывает выбор файлов; картинка — сразу ставится на мониторы.</summary>
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

    public RelayCommand ClearCommand => _clearCommand ??= new RelayCommand(() =>
    {
        _main.ClearWallpaper();
        Reload();
        Status = string.Empty;
    });
    private RelayCommand? _clearCommand;

    /// <summary>Список картинок, мониторы с режимами и выбранная картинка — из настроек.</summary>
    public void Reload()
    {
        Images.Clear();
        Images.Add(new WallpaperImageOption(null, "Выбрать свой файл…"));
        foreach (var file in WallpaperService.ListImages())
            Images.Add(new WallpaperImageOption(file, Path.GetFileName(file)));
        _selectedImage = _main.WallpaperImage is { } current
            ? Images.FirstOrDefault(i => string.Equals(i.Path, current, StringComparison.OrdinalIgnoreCase))
            : null;
        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(HasImage));

        Monitors.Clear();
        foreach (var m in WallpaperService.Monitors())
            Monitors.Add(new MonitorFitItem(m, _main.WallpaperFitFor(m.Id), OnFitChanged));
    }

    private void OnFitChanged(MonitorFitItem item)
    {
        bool ok = _main.SetWallpaperFit(item.Monitor.Id, item.Fit);
        if (HasImage) Status = ok ? string.Empty : "Не удалось поставить картинку — файл повреждён или недоступен.";
    }

    /// <summary>Выбрать один или несколько файлов — копии в папку программы; первая выбранная сразу ставится.</summary>
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите картинки для фона",
            Filter = "Картинки (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        var imported = WallpaperService.ImportImages(dialog.FileNames);
        if (imported.Count == 0) { Status = "Не удалось скопировать файлы в папку программы."; return; }
        Reload();
        SelectedImage = Images.FirstOrDefault(i => string.Equals(i.Path, imported[0], StringComparison.OrdinalIgnoreCase));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

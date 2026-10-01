using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Time2Gadget.Models;
using Time2Gadget.Services;

namespace Time2Gadget.ViewModels;

/// <summary>
/// Окно выбора картинки монитора для статичной заставки (докладка 2026-10-01): как окно шагов слайдшоу, но справа не 30 шагов,
/// а одна картинка — превью монитора в его режиме. Слева картинки из папки программы (добавить — до 30 за раз, всего до 500;
/// «✕» — удалить безвозвратно). «Сохранить» — картинка монитору, «✕» внизу — отмена.
/// </summary>
public sealed class WallpaperPickerViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private string? _selected;
    private string _status = string.Empty;

    public WallpaperPickerViewModel(MainViewModel main, WallpaperMonitor monitor)
    {
        _main = main;
        Monitor = monitor;
        _selected = main.WallpaperImageFor(monitor.Id);
        Fit = main.WallpaperFitFor(monitor.Id);
        Color = main.WallpaperColorFor(monitor.Id);
        ReloadLibrary();
    }

    public WallpaperMonitor Monitor { get; }
    public string Title => $"Картинка — монитор {Monitor.Number} ({Monitor.Bounds.Width}×{Monitor.Bounds.Height}) — Тайм2гаджет";
    public double MonitorWidth => Monitor.Bounds.Width;
    public double MonitorHeight => Monitor.Bounds.Height;
    public WallpaperFit Fit { get; }
    public string? Color { get; }

    public ObservableCollection<LibraryItem> Library { get; } = new();
    public string LibraryCaption => $"Картинки ({Library.Count} из {WallpaperService.LibraryLimit})";

    /// <summary>Выбранная картинка (превью справа).</summary>
    public string? Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedName)); }
    }

    public string SelectedName => _selected is null ? "Картинка не выбрана" : System.IO.Path.GetFileName(_selected);

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
        var (imported, message) = DesktopBackgroundViewModel.PickImages(_main);
        Status = message;
        ReloadLibrary();
        if (imported.Count > 0) Selected = imported[0];
    });
    private RelayCommand? _addImagesCommand;

    /// <summary>«✕» у картинки: удалить безвозвратно (где она выбрана — там пусто).</summary>
    private void Delete(LibraryItem item)
    {
        if (string.Equals(Selected, item.Path, StringComparison.OrdinalIgnoreCase)) Selected = null;
        Thumbnails.Forget(item.Path);
        Controls.MonitorPreview.Forget(item.Path);
        Status = _main.DeleteLibraryImage(item.Path) ? string.Empty : $"Не удалось удалить «{item.Name}» — файл занят.";
        ReloadLibrary();
    }

    /// <summary>«Сохранить»: картинка — монитору (не выбрана — ничего не меняется).</summary>
    public void Save()
    {
        if (Selected is { } path) _main.SetWallpaperImageFor(Monitor.Id, path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
